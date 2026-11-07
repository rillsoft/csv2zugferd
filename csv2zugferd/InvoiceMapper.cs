using System.Globalization;
using System.Text.RegularExpressions;
using Csv2Zugferd.Models;
using s2industries.ZUGFeRD;
using Serilog;

namespace Csv2Zugferd;

public static class InvoiceMapper
{
    public static InvoiceDescriptor Map(List<Dictionary<string, string>> rows, AppConfig config)
        => Map(rows, config, null);

    /// <summary>
    /// Mapping der CSV-Zeilen auf den InvoiceDescriptor. <paramref name="diagnostics"/> sammelt optional alle
    /// strukturierten Befunde (Warnungen, Rückfälle, ignorierte Felder); die bisherigen Logtexte bleiben erhalten.
    /// </summary>
    public static InvoiceDescriptor Map(List<Dictionary<string, string>> rows, AppConfig config, ICollection<MappingDiagnostic>? diagnostics)
    {
        var ctx = new MappingContext(config.Csv, diagnostics);
        var mapping = config.Mapping;
        var csvConfig = config.Csv;
        var firstRow = rows[0];

        ReportIgnoredFields(ctx, mapping);

        var invoiceNo = ctx.Resolve(mapping.Invoice?.InvoiceNumber, firstRow, "mapping.invoice.invoiceNumber");
        var invoiceDateStr = ctx.Resolve(mapping.Invoice?.InvoiceDate, firstRow, "mapping.invoice.invoiceDate");
        var currency = ctx.ParseCurrency(ctx.Resolve(mapping.Invoice?.Currency, firstRow, "mapping.invoice.currency"), "mapping.invoice.currency");
        var orderRefId = ctx.Resolve(mapping.Invoice?.OrderReferenceId, firstRow, "mapping.invoice.orderReferenceId");

        var invoiceDate = ctx.ParseDate(invoiceDateStr, "mapping.invoice.invoiceDate");
        var desc = InvoiceDescriptor.CreateInvoice(invoiceNo, invoiceDate, currency, orderRefId);
        Log.Debug("Invoice erstellt: Nr={InvoiceNo}, Datum={InvoiceDate}, Währung={Currency}", invoiceNo, invoiceDate, currency);

        var typeCodeStr = ctx.Resolve(mapping.Invoice?.TypeCode, firstRow, "mapping.invoice.typeCode");
        if (!string.IsNullOrEmpty(typeCodeStr))
        {
            desc.Type = ParseInvoiceType(typeCodeStr);
            Log.Debug("Rechnungstyp (BT-3): {InvoiceType} = {Code}", desc.Type, ConfigValues.GetInvoiceTypeCode(desc.Type));
        }

        var name = ctx.Resolve(mapping.Invoice?.Name, firstRow, "mapping.invoice.name");
        if (!string.IsNullOrEmpty(name))
            desc.Name = name;

        var refOrderNo = ctx.Resolve(mapping.Invoice?.ReferenceOrderNo, firstRow, "mapping.invoice.referenceOrderNo");
        if (!string.IsNullOrEmpty(refOrderNo))
            desc.ReferenceOrderNo = refOrderNo;

        var note = ctx.Resolve(mapping.Invoice?.Note, firstRow, "mapping.invoice.note");
        if (!string.IsNullOrEmpty(note))
            desc.AddNote(note);

        MapPrecedingInvoices(ctx, desc, mapping.Invoice?.PrecedingInvoices, firstRow);

        MapBuyer(ctx, desc, mapping.Buyer, firstRow);
        MapSeller(ctx, desc, mapping.Seller, firstRow);
        MapDelivery(ctx, desc, mapping.Delivery, firstRow);
        MapOrderReference(ctx, desc, mapping.OrderReference, firstRow);

        var lineItems = mapping.LineItems;
        if (lineItems != null)
        {
            if (ConfigValues.ParseLineItemsMode(lineItems.Mode) == ConfigValues.LineItemsModeColumns)
                MapLineItemsColumns(ctx, desc, lineItems, firstRow, csvConfig);
            else
                MapLineItemsRows(ctx, desc, lineItems, rows, csvConfig);
        }

        MapTotals(ctx, desc, mapping.Totals, firstRow, csvConfig);
        MapPaymentTerms(ctx, desc, mapping.PaymentTerms, firstRow);
        MapPaymentMeans(ctx, desc, mapping.PaymentMeans, firstRow);

        foreach (var diagnostic in CheckDocumentDiagnostics(desc, config.Zugferd.Profile).Concat(CheckPaymentMeansDiagnostics(desc, config.Zugferd.Profile)))
        {
            Log.Warning("{Warning}", diagnostic.Message);
            ctx.Add(diagnostic);
        }

        return desc;
    }

    /// <summary>Meldet konfigurierte Felder, die in der gewählten Betriebsart oder grundsätzlich keine Wirkung haben.</summary>
    private static void ReportIgnoredFields(MappingContext ctx, MappingConfig mapping)
    {
        var lineItems = mapping.LineItems;
        if (lineItems != null)
        {
            if (lineItems.ColumnsFields is { Count: > 0 })
                ctx.Ignored("mapping.lineItems.columnsFields", "wird vom Mapper nie gelesen; Positionsfelder gehören nach lineItems.fields");
            if (lineItems.Fields?.DiscountReason != null)
                ctx.Ignored("mapping.lineItems.fields.discountReason", "hat keine Ausgabewirkung; ein Rabattgrund wird im rows-Modus über lineItems.allowanceCharge.reason geschrieben");

            var mode = ConfigValues.ParseLineItemsMode(lineItems.Mode);
            if (!ConfigValues.LineItemsMode.IsKnown(lineItems.Mode))
                ctx.Fallback("mapping.lineItems.mode", lineItems.Mode, ConfigValues.LineItemsMode);

            if (mode == ConfigValues.LineItemsModeRows)
            {
                if (lineItems.ColumnPattern != null)
                    ctx.Ignored("mapping.lineItems.columnPattern", "wirkt nur im columns-Modus");
                if (lineItems.FixedFields is { Count: > 0 })
                    ctx.Ignored("mapping.lineItems.fixedFields", "wirkt nur im columns-Modus; im rows-Modus gelten lineItems.fields.unitCode, taxType und taxCategoryCode");
            }
            else
            {
                if (lineItems.AllowanceCharge != null)
                    ctx.Ignored("mapping.lineItems.allowanceCharge", "wirkt nur im rows-Modus; im columns-Modus Rabatte über lineItems.fields.discountPercent");
                if (lineItems.Fields?.UnitCode != null)
                    ctx.Ignored("mapping.lineItems.fields.unitCode", "wirkt nur im rows-Modus; im columns-Modus gilt lineItems.fixedFields.unitCode");
                if (lineItems.Fields?.TaxType != null)
                    ctx.Ignored("mapping.lineItems.fields.taxType", "wirkt nur im rows-Modus; im columns-Modus gilt lineItems.fixedFields.taxType");
                if (lineItems.Fields?.TaxCategoryCode != null)
                    ctx.Ignored("mapping.lineItems.fields.taxCategoryCode", "wirkt nur im rows-Modus; im columns-Modus gilt lineItems.fixedFields.taxCategoryCode");
                if (lineItems.ColumnPattern != null && !ConfigValues.ColumnStyle.IsKnown(lineItems.ColumnPattern.Style))
                    ctx.Fallback("mapping.lineItems.columnPattern.style", lineItems.ColumnPattern.Style, ConfigValues.ColumnStyle);
                if (lineItems.FixedFields != null)
                {
                    foreach (var key in lineItems.FixedFields.Keys.Where(k => !YamlConfigLoader.EffectiveFixedFieldKeys.Contains(k, StringComparer.Ordinal)))
                        ctx.Ignored($"mapping.lineItems.fixedFields.{key}", "unbekannter Schlüssel; wirksam sind nur unitCode, taxType und taxCategoryCode");
                }
            }
        }

        var totals = mapping.Totals;
        if (totals != null)
        {
            if (!ConfigValues.TotalsMode.IsKnown(totals.Mode))
                ctx.Fallback("mapping.totals.mode", totals.Mode, ConfigValues.TotalsMode);
            if (ConfigValues.ParseTotalsMode(totals.Mode) == ConfigValues.TotalsModeAuto)
            {
                foreach (var (fieldName, field) in new (string, FieldMapping?)[]
                {
                    ("lineTotalAmount", totals.LineTotalAmount), ("allowanceTotalAmount", totals.AllowanceTotalAmount),
                    ("taxBasisAmount", totals.TaxBasisAmount), ("taxTotalAmount", totals.TaxTotalAmount),
                    ("grandTotalAmount", totals.GrandTotalAmount), ("totalPrepaidAmount", totals.TotalPrepaidAmount),
                })
                {
                    if (field != null)
                        ctx.Ignored($"mapping.totals.{fieldName}", "wirkt nur bei totals.mode manual");
                }
            }
        }
    }

    private static void MapPrecedingInvoices(MappingContext ctx, InvoiceDescriptor desc, List<PrecedingInvoiceMapping>? precedingInvoices, Dictionary<string, string> row)
    {
        if (precedingInvoices == null) return;

        for (var i = 0; i < precedingInvoices.Count; i++)
        {
            var reference = precedingInvoices[i];
            var path = $"mapping.invoice.precedingInvoices[{i}]";
            var number = ctx.Resolve(reference.Number, row, path + ".number");
            if (string.IsNullOrEmpty(number)) continue;

            var dateStr = ctx.Resolve(reference.Date, row, path + ".date");
            var date = !string.IsNullOrEmpty(dateStr) ? ctx.ParseDate(dateStr, path + ".date") : (DateTime?)null;
            desc.AddInvoiceReferencedDocument(number, date, null);
            Log.Debug("Vorgängerrechnung (BT-25): {Number}, Datum={Date}", number, date);
        }
    }

    private static void MapBuyer(MappingContext ctx, InvoiceDescriptor desc, BuyerMapping? buyer, Dictionary<string, string> row)
    {
        if (buyer == null) return;

        var name = ctx.Resolve(buyer.Name, row, "mapping.buyer.name");
        var postalCode = ctx.Resolve(buyer.PostalCode, row, "mapping.buyer.postalCode");
        var city = ctx.Resolve(buyer.City, row, "mapping.buyer.city");
        var street = ctx.Resolve(buyer.Street, row, "mapping.buyer.street");
        var country = ctx.ParseCountry(ctx.Resolve(buyer.Country, row, "mapping.buyer.country"), "mapping.buyer.country");
        var id = NullIfEmpty(ctx.Resolve(buyer.Id, row, "mapping.buyer.id"));

        if (!string.IsNullOrEmpty(name))
        {
            desc.SetBuyer(name, postalCode, city, street, country, id);
            Log.Debug("Käufer: {Name}, {PostalCode} {City}, {Country}, {ID}", name, postalCode, city, country, id);
        }

        var taxId = ctx.Resolve(buyer.TaxId, row, "mapping.buyer.taxId");
        var taxScheme = ctx.Resolve(buyer.TaxScheme, row, "mapping.buyer.taxScheme");
        if (!string.IsNullOrEmpty(taxId))
            desc.AddBuyerTaxRegistration(taxId, ctx.ParseTaxScheme(taxScheme, "mapping.buyer.taxScheme"));

        var contact = ctx.Resolve(buyer.Contact, row, "mapping.buyer.contact");
        if (!string.IsNullOrEmpty(contact))
            desc.SetBuyerContact(contact);

        var eAddr = ctx.Resolve(buyer.ElectronicAddress, row, "mapping.buyer.electronicAddress");
        if (!string.IsNullOrEmpty(eAddr))
        {
            var scheme = ctx.ParseElectronicAddressScheme(buyer.ElectronicAddress?.Scheme, "mapping.buyer.electronicAddress.scheme");
            desc.SetBuyerElectronicAddress(eAddr, scheme);
        }
    }

    private static void MapSeller(MappingContext ctx, InvoiceDescriptor desc, SellerMapping? seller, Dictionary<string, string> row)
    {
        if (seller == null) return;

        var name = ctx.Resolve(seller.Name, row, "mapping.seller.name");
        var postalCode = ctx.Resolve(seller.PostalCode, row, "mapping.seller.postalCode");
        var city = ctx.Resolve(seller.City, row, "mapping.seller.city");
        var street = ctx.Resolve(seller.Street, row, "mapping.seller.street");
        var country = ctx.ParseCountry(ctx.Resolve(seller.Country, row, "mapping.seller.country"), "mapping.seller.country");
        var id = NullIfEmpty(ctx.Resolve(seller.Id, row, "mapping.seller.id"));

        if (!string.IsNullOrEmpty(name))
        {
            desc.SetSeller(name, postalCode, city, street, country, id);
            Log.Debug("Verkäufer: {Name}, {PostalCode} {City}, {Country}, {ID}", name, postalCode, city, country, id);
        }

        if (seller.TaxRegistrations != null)
        {
            for (var i = 0; i < seller.TaxRegistrations.Count; i++)
            {
                var reg = seller.TaxRegistrations[i];
                var taxId = !string.IsNullOrEmpty(reg.Value)
                    ? reg.Value
                    : row.GetValueOrDefault(reg.Column ?? "") ?? "";
                if (!string.IsNullOrEmpty(taxId))
                    desc.AddSellerTaxRegistration(taxId, ctx.ParseTaxScheme(reg.Scheme, $"mapping.seller.taxRegistrations[{i}].scheme"));
            }
        }

        var eAddr = ctx.Resolve(seller.ElectronicAddress, row, "mapping.seller.electronicAddress");
        if (!string.IsNullOrEmpty(eAddr))
        {
            var scheme = ctx.ParseElectronicAddressScheme(seller.ElectronicAddress?.Scheme, "mapping.seller.electronicAddress.scheme");
            desc.SetSellerElectronicAddress(eAddr, scheme);
        }
    }

    private static void MapDelivery(MappingContext ctx, InvoiceDescriptor desc, DeliveryMapping? delivery, Dictionary<string, string> row)
    {
        if (delivery != null)
        {
            var noteNo = ctx.Resolve(delivery.DeliveryNoteNumber, row, "mapping.delivery.deliveryNoteNumber");
            var noteDateStr = ctx.Resolve(delivery.DeliveryNoteDate, row, "mapping.delivery.deliveryNoteDate");
            if (!string.IsNullOrEmpty(noteNo))
            {
                var noteDate = !string.IsNullOrEmpty(noteDateStr) ? ctx.ParseDate(noteDateStr, "mapping.delivery.deliveryNoteDate") : (DateTime?)null;
                desc.SetDeliveryNoteReferenceDocument(noteNo, noteDate);
            }

            var actualDateStr = ctx.Resolve(delivery.ActualDeliveryDate, row, "mapping.delivery.actualDeliveryDate");
            if (!string.IsNullOrEmpty(actualDateStr))
                desc.ActualDeliveryDate = ctx.ParseDate(actualDateStr, "mapping.delivery.actualDeliveryDate");
        }

        // ApplicableHeaderTradeDelivery ist im CII-Schema Pflicht (1..1). Die Bibliothek lässt leere Elemente weg,
        // deshalb braucht der Block mindestens einen Inhalt. Ohne Lieferdaten gilt: Leistungsdatum = Rechnungsdatum (BT-72).
        if (desc.ActualDeliveryDate == null && desc.DeliveryNoteReferencedDocument == null && desc.InvoiceDate != null)
        {
            desc.ActualDeliveryDate = desc.InvoiceDate;
            Log.Warning("Keine Lieferdaten konfiguriert: Rechnungsdatum {InvoiceDate:yyyy-MM-dd} wird als Leistungsdatum (BT-72) verwendet",
                desc.InvoiceDate);
            ctx.Add(new MappingDiagnostic(DiagnosticCodes.DeliveryDateDefaulted, "mapping.delivery",
                $"Keine Lieferdaten konfiguriert: Rechnungsdatum {desc.InvoiceDate:yyyy-MM-dd} wird als Leistungsdatum (BT-72) verwendet"));
        }
    }

    private static void MapOrderReference(MappingContext ctx, InvoiceDescriptor desc, OrderReferenceMapping? orderRef, Dictionary<string, string> row)
    {
        if (orderRef == null) return;

        var orderNo = ctx.Resolve(orderRef.OrderNumber, row, "mapping.orderReference.orderNumber");
        var orderDateStr = ctx.Resolve(orderRef.OrderDate, row, "mapping.orderReference.orderDate");
        if (!string.IsNullOrEmpty(orderNo))
        {
            var orderDate = !string.IsNullOrEmpty(orderDateStr) ? ctx.ParseDate(orderDateStr, "mapping.orderReference.orderDate") : (DateTime?)null;
            desc.SetBuyerOrderReferenceDocument(orderNo, orderDate);
        }
    }

    private static void MapLineItemsRows(MappingContext ctx, InvoiceDescriptor desc, LineItemsMapping lineItems, List<Dictionary<string, string>> rows, CsvConfig csvConfig)
    {
        var fields = lineItems.Fields;
        if (fields == null) return;

        const string p = "mapping.lineItems.fields.";
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var where = $"Zeile {rowIndex + 1}";
            var name = ctx.Resolve(fields.Name, row, p + "name");
            if (string.IsNullOrEmpty(name)) continue;

            var netPrice = ctx.ParseDecimal(ctx.Resolve(fields.NetUnitPrice, row, p + "netUnitPrice"), p + "netUnitPrice", where);
            var quantity = ctx.ParseDecimal(ctx.Resolve(fields.BilledQuantity, row, p + "billedQuantity"), p + "billedQuantity", where);
            var unitCode = ctx.ParseUnitCode(ctx.Resolve(fields.UnitCode, row, p + "unitCode"), p + "unitCode", where);
            var description = NullIfEmpty(ctx.Resolve(fields.Description, row, p + "description"));
            var sellerAssignedId = NullIfEmpty(ctx.Resolve(fields.SellerAssignedId, row, p + "sellerAssignedId"));
            var buyerAssignedId = NullIfEmpty(ctx.Resolve(fields.BuyerAssignedId, row, p + "buyerAssignedId"));
            var taxPercent = ctx.ParseDecimal(ctx.Resolve(fields.TaxPercent, row, p + "taxPercent"), p + "taxPercent", where);
            var taxType = ctx.ParseTaxType(ctx.Resolve(fields.TaxType, row, p + "taxType"), p + "taxType", where);
            var categoryCode = ctx.ParseTaxCategory(ctx.Resolve(fields.TaxCategoryCode, row, p + "taxCategoryCode"), p + "taxCategoryCode", where);
            var discountPercent = ctx.ParseDecimal(ctx.Resolve(fields.DiscountPercent, row, p + "discountPercent"), p + "discountPercent", where);
            var discountedNetPrice = ApplyDiscount(netPrice, discountPercent);
            var grossUnitPrice = discountPercent > 0 ? netPrice : (decimal?)null;
            var lineTotal = CalculateLineTotal(discountedNetPrice, quantity);

            var item = desc.AddTradeLineItem(
                name: name,
                netUnitPrice: discountedNetPrice,
                description: description,
                unitCode: unitCode,
                unitQuantity: 1m,
                grossUnitPrice: grossUnitPrice,
                billedQuantity: quantity,
                lineTotalAmount: lineTotal,
                taxType: taxType,
                categoryCode: categoryCode,
                taxPercent: taxPercent,
                sellerAssignedID: sellerAssignedId,
                buyerAssignedID: buyerAssignedId
            );

            Log.Debug(
                "Position (rows): {Name}, Menge={Quantity}, Preis={Price}, Rabatt={Discount}%, MwSt={Tax}%, Einheitscode={UnitCode}, Steuerart={TaxType}, Steuerkategorie={TaxCategoryCode}",
                name,
                quantity,
                netPrice,
                discountPercent,
                taxPercent,
                unitCode,
                taxType,
                categoryCode);
            MapAllowanceCharge(ctx, item, lineItems.AllowanceCharge, row, lineTotal, where);
        }
    }

    private static void MapLineItemsColumns(MappingContext ctx, InvoiceDescriptor desc, LineItemsMapping lineItems, Dictionary<string, string> row, CsvConfig csvConfig)
    {
        var pattern = lineItems.ColumnPattern;
        if (pattern == null) return;

        var fields = lineItems.Fields;
        var nameFragment = fields?.Name?.Column ?? ColumnResolver.DefaultNameFragment;
        var descFragment = fields?.Description?.Column ?? ColumnResolver.DefaultDescriptionFragment;
        var priceFragment = fields?.NetUnitPrice?.Column ?? ColumnResolver.DefaultPriceFragment;
        var qtyFragment = fields?.BilledQuantity?.Column ?? ColumnResolver.DefaultQuantityFragment;
        var taxFragment = fields?.TaxPercent?.Column;
        var discountFragment = fields?.DiscountPercent?.Column;
        var sellerAssignedIdFragment = fields?.SellerAssignedId?.Column;
        var buyerAssignedIdFragment = fields?.BuyerAssignedId?.Column;

        Log.Debug("Columns-Modus: Style={Style}, Prefix={Prefix}, Separator={Separator}, StartIndex={StartIndex}, ZeroPadding={ZeroPadding}",
            pattern.Style, pattern.Prefix, pattern.Separator, pattern.StartIndex, pattern.ZeroPadding);
        Log.Debug("Field-Fragmente: Name={Name}, Desc={Desc}, Price={Price}, Qty={Qty}, Tax={Tax}",
            nameFragment, descFragment, priceFragment, qtyFragment, taxFragment);

        var fixedUnitCode = lineItems.FixedFields?.GetValueOrDefault("unitCode");
        var fixedTaxType = lineItems.FixedFields?.GetValueOrDefault("taxType");
        var fixedTaxCategory = lineItems.FixedFields?.GetValueOrDefault("taxCategoryCode");

        const string p = "mapping.lineItems.fields.";
        const string f = "mapping.lineItems.fixedFields.";
        for (int i = pattern.StartIndex; ; i++)
        {
            var where = $"Position {i}";

            string BuildColumnName(string fieldFragment) => ColumnResolver.BuildColumnName(pattern, i, fieldFragment);

            string ResolveColumnValue(FieldMapping? fieldMapping, string path, string? defaultFragment = null)
            {
                if (!string.IsNullOrEmpty(fieldMapping?.Value))
                    return fieldMapping.Value;

                var configuredColumn = fieldMapping?.Column;
                if (!string.IsNullOrEmpty(configuredColumn))
                {
                    if (row.TryGetValue(configuredColumn, out var directValue) && !string.IsNullOrEmpty(directValue))
                        return directValue ?? "";

                    var indexedValue = row.GetValueOrDefault(BuildColumnName(configuredColumn));
                    if (!string.IsNullOrEmpty(indexedValue))
                        return indexedValue;
                }

                var ruleValue = ctx.ResolveRule(fieldMapping, ResolveColumnName, path);
                if (!string.IsNullOrEmpty(ruleValue))
                    return ruleValue;

                if (!string.IsNullOrEmpty(fieldMapping?.Default))
                    return fieldMapping.Default;

                if (!string.IsNullOrEmpty(defaultFragment))
                    return row.GetValueOrDefault(BuildColumnName(defaultFragment)) ?? "";

                return "";
            }

            string ResolveColumnName(string column)
            {
                if (row.TryGetValue(column, out var directValue) && !string.IsNullOrEmpty(directValue))
                    return directValue;

                return row.GetValueOrDefault(BuildColumnName(column)) ?? "";
            }

            var nameCol = BuildColumnName(nameFragment);
            var nameVal = row.GetValueOrDefault(nameCol) ?? "";
            if (string.IsNullOrEmpty(nameVal))
            {
                Log.Debug("Columns-Iteration beendet bei Index {Index} (Spalte {Column} leer)", i, nameCol);
                break;
            }

            var descVal = ResolveColumnValue(fields?.Description, p + "description", descFragment);
            var netPrice = ctx.ParseDecimal(ResolveColumnValue(fields?.NetUnitPrice, p + "netUnitPrice", priceFragment), p + "netUnitPrice", where);
            var quantity = ctx.ParseDecimal(ResolveColumnValue(fields?.BilledQuantity, p + "billedQuantity", qtyFragment), p + "billedQuantity", where);
            var taxPercent = ctx.ParseDecimal(ResolveColumnValue(fields?.TaxPercent, p + "taxPercent", taxFragment), p + "taxPercent", where);
            var sellerAssignedId = NullIfEmpty(ResolveColumnValue(fields?.SellerAssignedId, p + "sellerAssignedId", sellerAssignedIdFragment));
            var buyerAssignedId = NullIfEmpty(ResolveColumnValue(fields?.BuyerAssignedId, p + "buyerAssignedId", buyerAssignedIdFragment));
            var discountPercent = ctx.ParseDecimal(ResolveColumnValue(fields?.DiscountPercent, p + "discountPercent", discountFragment), p + "discountPercent", where);
            var discountedNetPrice = ApplyDiscount(netPrice, discountPercent);
            var grossUnitPrice = discountPercent > 0 ? netPrice : (decimal?)null;
            var lineTotal = CalculateLineTotal(discountedNetPrice, quantity);

            var unitCodeValue = ResolveColumnValue(fixedUnitCode, f + "unitCode");
            var taxTypeValue = ResolveColumnValue(fixedTaxType, f + "taxType");
            var taxCategoryValue = ResolveColumnValue(fixedTaxCategory, f + "taxCategoryCode");
            var unitCode = ctx.ParseUnitCode(!string.IsNullOrEmpty(unitCodeValue) ? unitCodeValue : "C62", f + "unitCode", where);
            var taxType = ctx.ParseTaxType(!string.IsNullOrEmpty(taxTypeValue) ? taxTypeValue : "VAT", f + "taxType", where);
            var categoryCode = ctx.ParseTaxCategory(!string.IsNullOrEmpty(taxCategoryValue) ? taxCategoryValue : "S", f + "taxCategoryCode", where);

            var item = desc.AddTradeLineItem(
                name: nameVal,
                netUnitPrice: discountedNetPrice,
                description: descVal,
                unitCode: unitCode,
                unitQuantity: 1m,
                grossUnitPrice: grossUnitPrice,
                billedQuantity: quantity,
                lineTotalAmount: lineTotal,
                taxType: taxType,
                categoryCode: categoryCode,
                taxPercent: taxPercent,
                sellerAssignedID: sellerAssignedId,
                buyerAssignedID: buyerAssignedId
            );

            Log.Debug(
                "Position (columns #{Index}): {Name}, Menge={Quantity}, Preis={Price}, Rabatt={Discount}%, MwSt={Tax}%, Einheitscode={UnitCode}, Steuerart={TaxType}, Steuerkategorie={TaxCategoryCode}",
                i,
                nameVal,
                quantity,
                netPrice,
                discountPercent,
                taxPercent,
                unitCode,
                taxType,
                categoryCode);
        }
    }

    private static void MapAllowanceCharge(MappingContext ctx, TradeLineItem item, AllowanceChargeMapping? charge, Dictionary<string, string> row, decimal basisAmount, string where)
    {
        if (charge == null || !charge.Enabled) return;

        const string p = "mapping.lineItems.allowanceCharge.";
        var percentStr = ctx.Resolve(charge.ChargePercentage, row, p + "chargePercentage");
        if (string.IsNullOrEmpty(percentStr)) return;

        var percent = ctx.ParseDecimal(percentStr, p + "chargePercentage", where);
        if (percent <= 0) return;

        var reason = ctx.Resolve(charge.Reason, row, p + "reason") ?? "Rabatt";
        var isDiscount = ctx.Resolve(charge.IsDiscount, row, p + "isDiscount")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;

        var actualAmount = RoundAmount(basisAmount * (percent / 100m));

        // Positionsrabatt/-zuschlag auf Abrechnungsebene (BG-27/BG-28):
        // SpecifiedLineTradeSettlement/SpecifiedTradeAllowanceCharge. Der Zeilennettobetrag (BT-131)
        // muss den Betrag bereits enthalten, sonst scheitern BR-CO-10/BR-CO-13 und die Steuerbasis stimmt nicht.
        if (isDiscount)
        {
            item.AddSpecifiedTradeAllowance(CurrencyCodes.EUR, basisAmount, actualAmount, percent, reason);
            item.LineTotalAmount = RoundAmount(basisAmount - actualAmount);
            Log.Debug("  Rabatt: {Percent}% = {Amount} (Grund: {Reason}), Zeilennetto={LineTotal}", percent, actualAmount, reason, item.LineTotalAmount);
        }
        else
        {
            item.AddSpecifiedTradeCharge(CurrencyCodes.EUR, basisAmount, actualAmount, percent, reason);
            item.LineTotalAmount = RoundAmount(basisAmount + actualAmount);
            Log.Debug("  Zuschlag: {Percent}% = {Amount} (Grund: {Reason}), Zeilennetto={LineTotal}", percent, actualAmount, reason, item.LineTotalAmount);
        }
    }

    private static void MapTotals(MappingContext ctx, InvoiceDescriptor desc, TotalsMapping? totals, Dictionary<string, string> row, CsvConfig csvConfig)
    {
        if (totals == null || ConfigValues.ParseTotalsMode(totals.Mode) == ConfigValues.TotalsModeAuto)
        {
            CalculateAutoTotals(desc, csvConfig);
            return;
        }

        // Manual mode
        const string p = "mapping.totals.";
        var lineTotalAmount = ctx.ParseDecimal(ctx.Resolve(totals.LineTotalAmount, row, p + "lineTotalAmount"), p + "lineTotalAmount");
        var allowanceTotalAmount = ctx.ParseDecimal(ctx.Resolve(totals.AllowanceTotalAmount, row, p + "allowanceTotalAmount"), p + "allowanceTotalAmount");
        var taxBasisAmount = ctx.ParseDecimal(ctx.Resolve(totals.TaxBasisAmount, row, p + "taxBasisAmount"), p + "taxBasisAmount");
        var taxTotalAmount = ctx.ParseDecimal(ctx.Resolve(totals.TaxTotalAmount, row, p + "taxTotalAmount"), p + "taxTotalAmount");
        var grandTotalAmount = ctx.ParseDecimal(ctx.Resolve(totals.GrandTotalAmount, row, p + "grandTotalAmount"), p + "grandTotalAmount");
        var totalPrepaidAmount = ctx.ParseDecimal(ctx.Resolve(totals.TotalPrepaidAmount, row, p + "totalPrepaidAmount"), p + "totalPrepaidAmount");
        var duePayableAmount = grandTotalAmount - totalPrepaidAmount;

        AddTaxBreakdownFromLineItems(desc, taxTotalAmount);

        desc.SetTotals(lineTotalAmount, 0m, allowanceTotalAmount, taxBasisAmount, taxTotalAmount, grandTotalAmount, totalPrepaidAmount, duePayableAmount);
    }

    private static void CalculateAutoTotals(InvoiceDescriptor desc, CsvConfig csvConfig)
    {
        decimal lineTotalAmount = 0m;
        // Positionsrabatte stecken bereits im Zeilennettobetrag (BT-131). AllowanceTotalAmount (BT-107)
        // ist nur für Rabatte auf Dokumentebene gedacht, die das Tool nicht erzeugt.
        const decimal allowanceTotalAmount = 0m;

        // Group tax amounts by percent
        var taxGroups = new Dictionary<decimal, decimal>();

        foreach (var item in desc.TradeLineItems)
        {
            var lineTotal = GetLineTotal(item);
            lineTotalAmount += lineTotal;

            var taxPercent = item.TaxPercent;

            if (!taxGroups.ContainsKey(taxPercent))
                taxGroups[taxPercent] = 0m;
            taxGroups[taxPercent] += lineTotal;
        }

        var taxBasisAmount = lineTotalAmount - allowanceTotalAmount;
        decimal taxTotalAmount = 0m;

        foreach (var (percent, basisAmount) in taxGroups)
        {
            var taxAmount = RoundAmount(basisAmount * percent / 100m);
            taxTotalAmount += taxAmount;
            desc.AddApplicableTradeTax(basisAmount, percent, taxAmount, TaxTypes.VAT, TaxCategoryCodes.S);
        }

        var grandTotalAmount = taxBasisAmount + taxTotalAmount;

        desc.SetTotals(lineTotalAmount, 0m, allowanceTotalAmount, taxBasisAmount, taxTotalAmount, grandTotalAmount, 0m, grandTotalAmount);
        Log.Debug("Summen (auto): Netto={LineTotalAmount}, Rabatte={AllowanceTotalAmount}, Steuerbasis={TaxBasis}, MwSt={TaxTotal}, Brutto={GrandTotal}",
            lineTotalAmount, allowanceTotalAmount, taxBasisAmount, taxTotalAmount, grandTotalAmount);
    }

    private static void AddTaxBreakdownFromLineItems(InvoiceDescriptor desc, decimal expectedTaxTotalAmount)
    {
        if (desc.AnyApplicableTradeTaxes())
            return;

        var taxGroups = new Dictionary<(decimal Percent, TaxTypes Type, TaxCategoryCodes Category), decimal>();

        foreach (var item in desc.TradeLineItems)
        {
            var taxableAmount = GetLineTotal(item);
            var key = (item.TaxPercent, item.TaxType ?? TaxTypes.VAT, item.TaxCategoryCode ?? TaxCategoryCodes.S);

            if (!taxGroups.TryAdd(key, taxableAmount))
                taxGroups[key] += taxableAmount;
        }

        var roundedTaxAmounts = taxGroups
            .Select(group => new
            {
                group.Key.Percent,
                group.Key.Type,
                group.Key.Category,
                BasisAmount = group.Value,
                TaxAmount = RoundAmount(group.Value * group.Key.Percent / 100m),
            })
            .ToList();

        var calculatedTaxTotal = roundedTaxAmounts.Sum(group => group.TaxAmount);
        var taxDifference = expectedTaxTotalAmount - calculatedTaxTotal;

        for (var i = 0; i < roundedTaxAmounts.Count; i++)
        {
            var group = roundedTaxAmounts[i];
            var taxAmount = i == roundedTaxAmounts.Count - 1
                ? group.TaxAmount + taxDifference
                : group.TaxAmount;
            desc.AddApplicableTradeTax(group.BasisAmount, group.Percent, taxAmount, group.Type, group.Category);
        }
    }

    private static void MapPaymentTerms(MappingContext ctx, InvoiceDescriptor desc, PaymentTermsMapping? payment, Dictionary<string, string> row)
    {
        if (payment == null) return;

        var description = ctx.Resolve(payment.Description, row, "mapping.paymentTerms.description");
        var dueDateStr = ctx.Resolve(payment.DueDate, row, "mapping.paymentTerms.dueDate");

        if (!string.IsNullOrEmpty(description))
        {
            var dueDate = !string.IsNullOrEmpty(dueDateStr) ? ctx.ParseDate(dueDateStr, "mapping.paymentTerms.dueDate") : (DateTime?)null;
            desc.AddTradePaymentTerms(description, dueDate);
        }
    }

    private static void MapPaymentMeans(MappingContext ctx, InvoiceDescriptor desc, PaymentMeansMapping? paymentMeans, Dictionary<string, string> row)
    {
        if (paymentMeans == null) return;

        const string p = "mapping.paymentMeans.";
        var typeCodeStr = ctx.Resolve(paymentMeans.TypeCode, row, p + "typeCode");
        if (string.IsNullOrEmpty(typeCodeStr))
        {
            Log.Warning("paymentMeans ohne typeCode (BT-81) wird ignoriert");
            ctx.Add(new MappingDiagnostic(DiagnosticCodes.PaymentMeansWithoutTypeCode, p + "typeCode", "paymentMeans ohne typeCode (BT-81) wird ignoriert"));
            return;
        }

        var typeCode = ParsePaymentMeansTypeCode(typeCodeStr);
        var information = NullIfEmpty(ctx.Resolve(paymentMeans.Information, row, p + "information"));
        var isDirectDebit = IsDirectDebit(typeCode);
        var directDebit = paymentMeans.DirectDebit;

        if (directDebit != null && !isDirectDebit)
        {
            Log.Warning("paymentMeans.directDebit wird ignoriert, da typeCode {TypeCode} keine Lastschrift ist", typeCode);
            ctx.Add(new MappingDiagnostic(DiagnosticCodes.DirectDebitIgnored, p + "directDebit",
                $"paymentMeans.directDebit wird ignoriert, da typeCode {typeCode} keine Lastschrift ist"));
        }

        var creditorId = isDirectDebit ? NullIfEmpty(NormalizeAccountId(ctx.Resolve(directDebit?.CreditorId, row, p + "directDebit.creditorId"))) : null;
        var mandateReference = isDirectDebit ? NullIfEmpty(ctx.Resolve(directDebit?.MandateReference, row, p + "directDebit.mandateReference")) : null;

        desc.SetPaymentMeans(typeCode, information, creditorId, mandateReference);
        Log.Debug("Zahlungsmittel: {TypeCode}, Gläubiger-ID={CreditorId}, Mandat={Mandate}", typeCode, creditorId, mandateReference);

        var paymentReference = ctx.Resolve(paymentMeans.PaymentReference, row, p + "paymentReference");
        if (!string.IsNullOrEmpty(paymentReference))
            desc.PaymentReference = paymentReference;

        if (paymentMeans.SellerAccounts != null)
        {
            for (var i = 0; i < paymentMeans.SellerAccounts.Count; i++)
            {
                var account = paymentMeans.SellerAccounts[i];
                var accountPath = $"{p}sellerAccounts[{i}].";
                var iban = NormalizeAccountId(ctx.Resolve(account.Iban, row, accountPath + "iban"));
                if (string.IsNullOrEmpty(iban)) continue;

                var bic = NullIfEmpty(NormalizeAccountId(ctx.Resolve(account.Bic, row, accountPath + "bic")));
                var name = NullIfEmpty(ctx.Resolve(account.Name, row, accountPath + "name"));
                desc.AddCreditorFinancialAccount(iban, bic, name: name);
                Log.Debug("Verkäuferkonto: IBAN={Iban}, BIC={Bic}", iban, bic);
            }
        }

        if (isDirectDebit)
        {
            var buyerIban = NormalizeAccountId(ctx.Resolve(directDebit?.BuyerIban, row, p + "directDebit.buyerIban"));
            if (!string.IsNullOrEmpty(buyerIban))
            {
                var buyerBic = NullIfEmpty(NormalizeAccountId(ctx.Resolve(directDebit?.BuyerBic, row, p + "directDebit.buyerBic")));
                desc.AddDebitorFinancialAccount(buyerIban, buyerBic);
                Log.Debug("Käuferkonto (Lastschrift): IBAN={Iban}", MaskIban(buyerIban));
            }
        }
    }

    // Codes, die XRechnung in BT-3 zulässt (KoSIT-Schematron XRechnung-CII-validation.sch, Regel BR-DE-17).
    private static readonly HashSet<string> XRechnungInvoiceTypeCodes =
        ["326", "380", "381", "384", "389", "875", "876", "877"];

    public static IReadOnlyList<string> CheckDocument(InvoiceDescriptor desc, string? profile) =>
        CheckDocumentDiagnostics(desc, profile).Select(d => d.Message).ToList();

    /// <summary>Strukturierter Kern der Dokumentprüfung; <see cref="CheckDocument"/> liefert dieselben Texte.</summary>
    public static IReadOnlyList<MappingDiagnostic> CheckDocumentDiagnostics(InvoiceDescriptor desc, string? profile)
    {
        var warnings = new List<MappingDiagnostic>();
        var typeCode = ConfigValues.GetInvoiceTypeCode(desc.Type);

        if (IsXRechnung(profile) && !XRechnungInvoiceTypeCodes.Contains(typeCode))
            warnings.Add(new MappingDiagnostic(DiagnosticCodes.XrechnungTypeCodeNotAllowed, "mapping.invoice.typeCode",
                $"XRechnung erlaubt Rechnungstyp {typeCode} (BT-3) nicht; zulässig sind {string.Join(", ", XRechnungInvoiceTypeCodes)} (BR-DE-17)"));

        var hasPrecedingInvoice = desc.GetInvoiceReferencedDocuments().Count > 0;

        if (desc.Type is InvoiceType.CreditNote or InvoiceType.Correction && !hasPrecedingInvoice)
        {
            var rule = IsXRechnung(profile) && desc.Type == InvoiceType.Correction ? " (BR-DE-26)" : string.Empty;
            warnings.Add(new MappingDiagnostic(DiagnosticCodes.PrecedingInvoiceMissing, "mapping.invoice.precedingInvoices",
                $"Rechnungstyp {typeCode} ohne Vorgängerrechnung (BT-25), invoice.precedingInvoices fehlt{rule}"));
        }

        if (hasPrecedingInvoice && string.Equals(profile, "minimum", StringComparison.OrdinalIgnoreCase))
            warnings.Add(new MappingDiagnostic(DiagnosticCodes.PrecedingInvoiceNotInMinimum, "zugferd.profile",
                "Profil MINIMUM enthält keine Vorgängerrechnung (BT-25), invoice.precedingInvoices wird nicht geschrieben"));

        return warnings;
    }

    private static bool IsXRechnung(string? profile) =>
        string.Equals(profile, "xrechnung", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> CheckPaymentMeans(InvoiceDescriptor desc, string? profile) =>
        CheckPaymentMeansDiagnostics(desc, profile).Select(d => d.Message).ToList();

    /// <summary>Strukturierter Kern der Zahlungsprüfung; <see cref="CheckPaymentMeans"/> liefert dieselben Texte.</summary>
    public static IReadOnlyList<MappingDiagnostic> CheckPaymentMeansDiagnostics(InvoiceDescriptor desc, string? profile)
    {
        const string p = "mapping.paymentMeans";
        var warnings = new List<MappingDiagnostic>();
        var typeCode = desc.PaymentMeans?.TypeCode;

        if (typeCode == null)
        {
            if (IsXRechnung(profile))
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.PaymentMeansMissingXrechnung, p,
                    "XRechnung verlangt Zahlungsanweisungen (BR-DE-1), paymentMeans fehlt"));
            return warnings;
        }

        if (typeCode is PaymentMeansTypeCodes.CreditTransferNonSEPA or PaymentMeansTypeCodes.SEPACreditTransfer
            && desc.CreditorBankAccounts.Count == 0)
            warnings.Add(new MappingDiagnostic(DiagnosticCodes.CreditTransferWithoutIban, p + ".sellerAccounts",
                $"Zahlungsart {(int)typeCode} (Überweisung) ohne IBAN des Verkäufers (BR-61)"));

        if (typeCode == PaymentMeansTypeCodes.SEPADirectDebit)
        {
            if (string.IsNullOrEmpty(desc.PaymentMeans!.SEPACreditorIdentifier))
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.DirectDebitWithoutCreditorId, p + ".directDebit.creditorId", "SEPA-Lastschrift ohne Gläubiger-ID (BT-90)"));
            if (string.IsNullOrEmpty(desc.PaymentMeans.SEPAMandateReference))
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.DirectDebitWithoutMandate, p + ".directDebit.mandateReference", "SEPA-Lastschrift ohne Mandatsreferenz (BT-89)"));
            if (desc.DebitorBankAccounts.Count == 0)
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.DirectDebitWithoutBuyerIban, p + ".directDebit.buyerIban", "SEPA-Lastschrift ohne IBAN des Käufers (BT-91)"));
        }

        for (var i = 0; i < desc.CreditorBankAccounts.Count; i++)
        {
            var account = desc.CreditorBankAccounts[i];
            if (!string.IsNullOrEmpty(account.IBAN) && !IsValidIban(account.IBAN))
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.InvalidIbanChecksum, $"{p}.sellerAccounts[{i}].iban",
                    $"IBAN des Verkäufers {account.IBAN} hat eine ungültige Prüfsumme"));
        }

        foreach (var account in desc.DebitorBankAccounts)
        {
            if (!string.IsNullOrEmpty(account.IBAN) && !IsValidIban(account.IBAN))
                warnings.Add(new MappingDiagnostic(DiagnosticCodes.InvalidIbanChecksum, p + ".directDebit.buyerIban",
                    $"IBAN des Käufers {MaskIban(account.IBAN)} hat eine ungültige Prüfsumme"));
        }

        return warnings;
    }

    // --- Kontext: reicht die Diagnosesammlung an die privaten Methoden durch ---

    private sealed class MappingContext(CsvConfig csvConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        public void Add(MappingDiagnostic diagnostic) => diagnostics?.Add(diagnostic);

        public void Ignored(string path, string reason)
        {
            var message = $"{path} ist konfiguriert, {reason}";
            Log.Warning("{Warning}", message);
            Add(new MappingDiagnostic(DiagnosticCodes.ConfigFieldIgnored, path, message));
        }

        public void Fallback(string path, string value, ValueSet valueSet, string? where = null)
        {
            var location = where == null ? path : $"{path} ({where})";
            var message = $"Unbekannter Wert '{value}' in {location}, verwendet wird {valueSet.InvalidFallback}; zulässig sind {Summarize(valueSet.Canonical)}";
            Log.Warning("{Warning}", message);
            Add(new MappingDiagnostic(DiagnosticCodes.ConfigValueFallback, path, message));
        }

        public string Resolve(FieldMapping? field, Dictionary<string, string> row, string path)
        {
            if (field == null) return string.Empty;
            if (!string.IsNullOrEmpty(field.Value)) return field.Value;
            if (!string.IsNullOrEmpty(field.Column))
            {
                var columnValue = row.GetValueOrDefault(field.Column);
                if (!string.IsNullOrEmpty(columnValue))
                    return columnValue;
            }

            var ruleValue = ResolveRule(field, column => row.GetValueOrDefault(column) ?? string.Empty, path);
            if (!string.IsNullOrEmpty(ruleValue))
                return ruleValue;

            if (!string.IsNullOrEmpty(field.Default))
                return field.Default;

            return string.Empty;
        }

        public string ResolveRule(FieldMapping? field, Func<string, string> resolveColumn, string path)
        {
            if (field?.Rules == null) return string.Empty;

            for (var i = 0; i < field.Rules.Count; i++)
            {
                var rule = field.Rules[i];
                var column = rule.When?.Column;
                var pattern = rule.When?.Regex;
                if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(pattern))
                    continue;

                var value = resolveColumn(column);
                if (string.IsNullOrEmpty(value))
                    continue;

                try
                {
                    if (Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
                        return rule.Value ?? string.Empty;
                }
                catch (ArgumentException ex)
                {
                    Log.Warning(ex, "Ungültiger Regex im YAML-Mapping ignoriert: {Regex}", pattern);
                    Add(new MappingDiagnostic(DiagnosticCodes.InvalidRegex, $"{path}.rules[{i}].when.regex",
                        $"Ungültiger Regex '{pattern}' wird ignoriert: {ex.Message}"));
                }
            }

            return string.Empty;
        }

        public DateTime ParseDate(string value, string path)
        {
            if (string.IsNullOrEmpty(value)) return DateTime.MinValue;
            if (DateTime.TryParseExact(value, csvConfig.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date;

            var message = $"Datum '{value}' in {path} entspricht nicht dem Format {csvConfig.DateFormat}; verwendet wird {DateTime.MinValue:yyyy-MM-dd}";
            Log.Warning("{Warning}", message);
            Add(new MappingDiagnostic(DiagnosticCodes.InvalidDate, path, message));
            return DateTime.MinValue;
        }

        public decimal ParseDecimal(string value, string path, string? where = null)
        {
            if (string.IsNullOrEmpty(value)) return 0m;
            var normalized = value.Trim().TrimEnd('%').Trim();
            normalized = csvConfig.DecimalSeparator == "," ? normalized.Replace(",", ".") : normalized;
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return result;

            var location = where == null ? path : $"{path} ({where})";
            var message = $"Zahl '{value}' in {location} ist nicht lesbar (Dezimaltrennzeichen '{csvConfig.DecimalSeparator}'); verwendet wird 0";
            Log.Warning("{Warning}", message);
            Add(new MappingDiagnostic(DiagnosticCodes.InvalidDecimal, path, message));
            return 0m;
        }

        public CurrencyCodes ParseCurrency(string value, string path)
        {
            if (ConfigValues.TryParseCurrency(value, out var code) || string.IsNullOrEmpty(value)) return code;
            Fallback(path, value, ConfigValues.Currency);
            return code;
        }

        public CountryCodes ParseCountry(string value, string path)
        {
            if (ConfigValues.TryParseCountry(value, out var code) || string.IsNullOrEmpty(value)) return code;
            Fallback(path, value, ConfigValues.Country);
            return code;
        }

        public QuantityCodes ParseUnitCode(string value, string path, string where)
        {
            if (ConfigValues.TryParseUnitCode(value, out var code) || string.IsNullOrEmpty(value)) return code;
            Fallback(path, value, ConfigValues.UnitCode, where);
            return code;
        }

        public TaxTypes ParseTaxType(string value, string path, string where)
        {
            if (ConfigValues.TryParseTaxType(value, out var code) || string.IsNullOrEmpty(value)) return code;
            Fallback(path, value, ConfigValues.TaxType, where);
            return code;
        }

        public TaxCategoryCodes ParseTaxCategory(string value, string path, string where)
        {
            if (ConfigValues.TryParseTaxCategory(value, out var code) || string.IsNullOrEmpty(value)) return code;
            Fallback(path, value, ConfigValues.TaxCategory, where);
            return code;
        }

        public TaxRegistrationSchemeID ParseTaxScheme(string? value, string path)
        {
            if (ConfigValues.TryParseTaxScheme(value, out var scheme) || string.IsNullOrEmpty(value)) return scheme;
            Fallback(path, value, ConfigValues.TaxScheme);
            return scheme;
        }

        public ElectronicAddressSchemeIdentifiers ParseElectronicAddressScheme(string? value, string path)
        {
            if (ConfigValues.TryParseElectronicAddressScheme(value, out var scheme) || string.IsNullOrEmpty(value)) return scheme;
            Fallback(path, value, ConfigValues.ElectronicAddressScheme);
            return scheme;
        }

        private static string Summarize(IReadOnlyList<string> values) =>
            values.Count <= 12 ? string.Join(", ", values) : string.Join(", ", values.Take(12)) + $", ... ({values.Count} Werte)";
    }

    // --- Helper methods ---

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal ApplyDiscount(decimal netPrice, decimal discountPercent) =>
        discountPercent > 0 ? Math.Round(netPrice * (1m - discountPercent / 100m), 4) : netPrice;

    // Kaufmännische Rundung auf zwei Stellen, wie sie die Bibliothek beim Schreiben der Beträge anwendet.
    private static decimal RoundAmount(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Zeilennettobetrag (BT-131): einmal berechnen, gerundet an die Bibliothek übergeben und für alle Summen wiederverwenden.
    private static decimal CalculateLineTotal(decimal netUnitPrice, decimal quantity) =>
        RoundAmount(netUnitPrice * quantity);

    private static decimal GetLineTotal(TradeLineItem item) =>
        item.LineTotalAmount ?? CalculateLineTotal(item.NetUnitPrice, item.BilledQuantity);

    private static PaymentMeansTypeCodes ParsePaymentMeansTypeCode(string value)
    {
        if (ConfigValues.TryParsePaymentMeansTypeCode(value, out var code))
            return code;
        throw new Csv2ZugferdException(ErrorCodes.PaymentMeansTypeUnknown, $"Unbekannter Zahlungsart-Code (BT-81) in paymentMeans.typeCode: '{value}'");
    }

    private static InvoiceType ParseInvoiceType(string value)
    {
        if (ConfigValues.TryParseInvoiceType(value, out var type))
            return type;
        throw new Csv2ZugferdException(ErrorCodes.InvoiceTypeUnknown, $"Unbekannter Rechnungstyp (BT-3) in invoice.typeCode: '{value}'");
    }

    private static bool IsDirectDebit(PaymentMeansTypeCodes typeCode) =>
        typeCode is PaymentMeansTypeCodes.SEPADirectDebit or PaymentMeansTypeCodes.DirectDebit;

    private static string NormalizeAccountId(string value) =>
        Regex.Replace(value, @"\s+", string.Empty).ToUpperInvariant();

    private static string MaskIban(string iban) =>
        iban.Length <= 8 ? new string('*', iban.Length) : iban[..4] + new string('*', iban.Length - 8) + iban[^4..];

    private static bool IsValidIban(string iban)
    {
        if (iban.Length < 15 || iban.Length > 34 || !iban.All(char.IsAsciiLetterOrDigit))
            return false;

        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var digits = char.IsAsciiDigit(c) ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            remainder = digits < 10 ? (remainder * 10 + digits) % 97 : (remainder * 100 + digits) % 97;
        }
        return remainder == 1;
    }
}
