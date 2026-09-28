using System.Globalization;
using System.Text.RegularExpressions;
using Csv2Zugferd.Models;
using s2industries.ZUGFeRD;
using Serilog;

namespace Csv2Zugferd;

public static class InvoiceMapper
{
    public static InvoiceDescriptor Map(List<Dictionary<string, string>> rows, AppConfig config)
    {
        var mapping = config.Mapping;
        var csvConfig = config.Csv;
        var firstRow = rows[0];

        var invoiceNo = Resolve(mapping.Invoice?.InvoiceNumber, firstRow);
        var invoiceDateStr = Resolve(mapping.Invoice?.InvoiceDate, firstRow);
        var currency = ResolveCurrency(Resolve(mapping.Invoice?.Currency, firstRow));
        var orderRefId = Resolve(mapping.Invoice?.OrderReferenceId, firstRow);

        var invoiceDate = ParseDate(invoiceDateStr, csvConfig.DateFormat);
        var desc = InvoiceDescriptor.CreateInvoice(invoiceNo, invoiceDate, currency, orderRefId);
        Log.Debug("Invoice erstellt: Nr={InvoiceNo}, Datum={InvoiceDate}, Währung={Currency}", invoiceNo, invoiceDate, currency);

        var name = Resolve(mapping.Invoice?.Name, firstRow);
        if (!string.IsNullOrEmpty(name))
            desc.Name = name;

        var refOrderNo = Resolve(mapping.Invoice?.ReferenceOrderNo, firstRow);
        if (!string.IsNullOrEmpty(refOrderNo))
            desc.ReferenceOrderNo = refOrderNo;

        var note = Resolve(mapping.Invoice?.Note, firstRow);
        if (!string.IsNullOrEmpty(note))
            desc.AddNote(note);

        MapBuyer(desc, mapping.Buyer, firstRow);
        MapSeller(desc, mapping.Seller, firstRow);
        MapDelivery(desc, mapping.Delivery, firstRow, csvConfig.DateFormat);
        MapOrderReference(desc, mapping.OrderReference, firstRow, csvConfig.DateFormat);

        var lineItems = mapping.LineItems;
        if (lineItems != null)
        {
            if (lineItems.Mode.Equals("columns", StringComparison.OrdinalIgnoreCase))
                MapLineItemsColumns(desc, lineItems, firstRow, csvConfig);
            else
                MapLineItemsRows(desc, lineItems, rows, csvConfig);
        }

        MapTotals(desc, mapping.Totals, firstRow, csvConfig);
        MapPaymentTerms(desc, mapping.PaymentTerms, firstRow, csvConfig.DateFormat);
        MapPaymentMeans(desc, mapping.PaymentMeans, firstRow);

        foreach (var warning in CheckPaymentMeans(desc, config.Zugferd.Profile))
            Log.Warning("{Warning}", warning);

        return desc;
    }

    private static void MapBuyer(InvoiceDescriptor desc, BuyerMapping? buyer, Dictionary<string, string> row)
    {
        if (buyer == null) return;

        var name = Resolve(buyer.Name, row);
        var postalCode = Resolve(buyer.PostalCode, row);
        var city = Resolve(buyer.City, row);
        var street = Resolve(buyer.Street, row);
        var country = ResolveCountry(Resolve(buyer.Country, row));

        if (!string.IsNullOrEmpty(name))
        {
            desc.SetBuyer(name, postalCode, city, street, country);
            Log.Debug("Käufer: {Name}, {PostalCode} {City}, {Country}", name, postalCode, city, country);
        }

        var taxId = Resolve(buyer.TaxId, row);
        var taxScheme = Resolve(buyer.TaxScheme, row);
        if (!string.IsNullOrEmpty(taxId))
            desc.AddBuyerTaxRegistration(taxId, ParseTaxScheme(taxScheme));

        var contact = Resolve(buyer.Contact, row);
        if (!string.IsNullOrEmpty(contact))
            desc.SetBuyerContact(contact);

        var eAddr = Resolve(buyer.ElectronicAddress, row);
        if (!string.IsNullOrEmpty(eAddr))
        {
            var scheme = ParseElectronicAddressScheme(buyer.ElectronicAddress?.Scheme);
            desc.SetBuyerElectronicAddress(eAddr, scheme);
        }
    }

    private static void MapSeller(InvoiceDescriptor desc, SellerMapping? seller, Dictionary<string, string> row)
    {
        if (seller == null) return;

        var name = Resolve(seller.Name, row);
        var postalCode = Resolve(seller.PostalCode, row);
        var city = Resolve(seller.City, row);
        var street = Resolve(seller.Street, row);
        var country = ResolveCountry(Resolve(seller.Country, row));

        if (!string.IsNullOrEmpty(name))
        {
            desc.SetSeller(name, postalCode, city, street, country);
            Log.Debug("Verkäufer: {Name}, {PostalCode} {City}, {Country}", name, postalCode, city, country);
        }

        if (seller.TaxRegistrations != null)
        {
            foreach (var reg in seller.TaxRegistrations)
            {
                var taxId = !string.IsNullOrEmpty(reg.Value)
                    ? reg.Value
                    : row.GetValueOrDefault(reg.Column ?? "") ?? "";
                if (!string.IsNullOrEmpty(taxId))
                    desc.AddSellerTaxRegistration(taxId, ParseTaxScheme(reg.Scheme));
            }
        }

        var eAddr = Resolve(seller.ElectronicAddress, row);
        if (!string.IsNullOrEmpty(eAddr))
        {
            var scheme = ParseElectronicAddressScheme(seller.ElectronicAddress?.Scheme);
            desc.SetSellerElectronicAddress(eAddr, scheme);
        }
    }

    private static void MapDelivery(InvoiceDescriptor desc, DeliveryMapping? delivery, Dictionary<string, string> row, string dateFormat)
    {
        if (delivery == null) return;

        var noteNo = Resolve(delivery.DeliveryNoteNumber, row);
        var noteDateStr = Resolve(delivery.DeliveryNoteDate, row);
        if (!string.IsNullOrEmpty(noteNo))
        {
            var noteDate = !string.IsNullOrEmpty(noteDateStr) ? ParseDate(noteDateStr, dateFormat) : (DateTime?)null;
            desc.SetDeliveryNoteReferenceDocument(noteNo, noteDate);
        }

        var actualDateStr = Resolve(delivery.ActualDeliveryDate, row);
        if (!string.IsNullOrEmpty(actualDateStr))
            desc.ActualDeliveryDate = ParseDate(actualDateStr, dateFormat);
    }

    private static void MapOrderReference(InvoiceDescriptor desc, OrderReferenceMapping? orderRef, Dictionary<string, string> row, string dateFormat)
    {
        if (orderRef == null) return;

        var orderNo = Resolve(orderRef.OrderNumber, row);
        var orderDateStr = Resolve(orderRef.OrderDate, row);
        if (!string.IsNullOrEmpty(orderNo))
        {
            var orderDate = !string.IsNullOrEmpty(orderDateStr) ? ParseDate(orderDateStr, dateFormat) : (DateTime?)null;
            desc.SetBuyerOrderReferenceDocument(orderNo, orderDate);
        }
    }

    private static void MapLineItemsRows(InvoiceDescriptor desc, LineItemsMapping lineItems, List<Dictionary<string, string>> rows, CsvConfig csvConfig)
    {
        var fields = lineItems.Fields;
        if (fields == null) return;

        foreach (var row in rows)
        {
            var name = Resolve(fields.Name, row);
            if (string.IsNullOrEmpty(name)) continue;

            var netPrice = ParseDecimal(Resolve(fields.NetUnitPrice, row), csvConfig.DecimalSeparator);
            var quantity = ParseDecimal(Resolve(fields.BilledQuantity, row), csvConfig.DecimalSeparator);
            var unitCode = ParseUnitCode(Resolve(fields.UnitCode, row));
            var description = NullIfEmpty(Resolve(fields.Description, row));
            var sellerAssignedId = NullIfEmpty(Resolve(fields.SellerAssignedId, row));
            var buyerAssignedId = NullIfEmpty(Resolve(fields.BuyerAssignedId, row));
            var taxPercent = ParseDecimal(Resolve(fields.TaxPercent, row), csvConfig.DecimalSeparator);
            var taxType = ParseTaxType(Resolve(fields.TaxType, row));
            var categoryCode = ParseTaxCategory(Resolve(fields.TaxCategoryCode, row));
            var discountPercent = GetDiscountPercent(fields.DiscountPercent, row, csvConfig);
            var discountedNetPrice = ApplyDiscount(netPrice, discountPercent);
            var grossUnitPrice = discountPercent > 0 ? netPrice : (decimal?)null;

            var item = desc.AddTradeLineItem(
                name: name,
                netUnitPrice: discountedNetPrice,
                description: description,
                unitCode: unitCode,
                unitQuantity: 1m,
                grossUnitPrice: grossUnitPrice,
                billedQuantity: quantity,
                lineTotalAmount: null,
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
            MapAllowanceCharge(item, lineItems.AllowanceCharge, row, csvConfig, discountedNetPrice, quantity);
        }
    }

    private static void MapLineItemsColumns(InvoiceDescriptor desc, LineItemsMapping lineItems, Dictionary<string, string> row, CsvConfig csvConfig)
    {
        var pattern = lineItems.ColumnPattern;
        if (pattern == null) return;

        var fields = lineItems.Fields;
        var nameFragment = fields?.Name?.Column ?? "Artikel";
        var descFragment = fields?.Description?.Column ?? "Beschreibung";
        var priceFragment = fields?.NetUnitPrice?.Column ?? "Einzelpreis";
        var qtyFragment = fields?.BilledQuantity?.Column ?? "Menge";
        var taxFragment = fields?.TaxPercent?.Column;
        var discountFragment = fields?.DiscountPercent?.Column;
        var discountReasonFragment = fields?.DiscountReason?.Column;
        var sellerAssignedIdFragment = fields?.SellerAssignedId?.Column;
        var buyerAssignedIdFragment = fields?.BuyerAssignedId?.Column;

        Log.Debug("Columns-Modus: Style={Style}, Prefix={Prefix}, Separator={Separator}, StartIndex={StartIndex}, ZeroPadding={ZeroPadding}",
            pattern.Style, pattern.Prefix, pattern.Separator, pattern.StartIndex, pattern.ZeroPadding);
        Log.Debug("Field-Fragmente: Name={Name}, Desc={Desc}, Price={Price}, Qty={Qty}, Tax={Tax}",
            nameFragment, descFragment, priceFragment, qtyFragment, taxFragment);

        var fixedUnitCode = lineItems.FixedFields?.GetValueOrDefault("unitCode");
        var fixedTaxType = lineItems.FixedFields?.GetValueOrDefault("taxType");
        var fixedTaxCategory = lineItems.FixedFields?.GetValueOrDefault("taxCategoryCode");

        for (int i = pattern.StartIndex; ; i++)
        {
            var idx = pattern.ZeroPadding > 0
                ? i.ToString().PadLeft(pattern.ZeroPadding, '0')
                : i.ToString();

            string BuildColumnName(string fieldFragment)
            {
                return pattern.Style.Equals("prefix", StringComparison.OrdinalIgnoreCase)
                    ? $"{pattern.Prefix}{idx}{pattern.Separator}{fieldFragment}"
                    : $"{fieldFragment}{pattern.Separator}{idx}";
            }

            string ResolveColumnValue(FieldMapping? fieldMapping, string? defaultFragment = null)
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

                var ruleValue = ResolveRule(fieldMapping, ResolveColumnName);
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

            var descVal = ResolveColumnValue(fields?.Description, descFragment);
            var netPrice = ParseDecimal(ResolveColumnValue(fields?.NetUnitPrice, priceFragment), csvConfig.DecimalSeparator);
            var quantity = ParseDecimal(ResolveColumnValue(fields?.BilledQuantity, qtyFragment), csvConfig.DecimalSeparator);
            var taxPercent = ParseDecimal(ResolveColumnValue(fields?.TaxPercent, taxFragment), csvConfig.DecimalSeparator);
            var sellerAssignedId = NullIfEmpty(ResolveColumnValue(fields?.SellerAssignedId, sellerAssignedIdFragment));
            var buyerAssignedId = NullIfEmpty(ResolveColumnValue(fields?.BuyerAssignedId, buyerAssignedIdFragment));
            var discountPercent = ParseDecimal(ResolveColumnValue(fields?.DiscountPercent, discountFragment), csvConfig.DecimalSeparator);
            var discountedNetPrice = ApplyDiscount(netPrice, discountPercent);
            var grossUnitPrice = discountPercent > 0 ? netPrice : (decimal?)null;

            var unitCodeValue = ResolveColumnValue(fixedUnitCode);
            var taxTypeValue = ResolveColumnValue(fixedTaxType);
            var taxCategoryValue = ResolveColumnValue(fixedTaxCategory);
            var unitCode = ParseUnitCode(!string.IsNullOrEmpty(unitCodeValue) ? unitCodeValue : "C62");
            var taxType = ParseTaxType(!string.IsNullOrEmpty(taxTypeValue) ? taxTypeValue : "VAT");
            var categoryCode = ParseTaxCategory(!string.IsNullOrEmpty(taxCategoryValue) ? taxCategoryValue : "S");

            var item = desc.AddTradeLineItem(
                name: nameVal,
                netUnitPrice: discountedNetPrice,
                description: descVal,
                unitCode: unitCode,
                unitQuantity: 1m,
                grossUnitPrice: grossUnitPrice,
                billedQuantity: quantity,
                lineTotalAmount: null,
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

    private static void MapAllowanceCharge(TradeLineItem item, AllowanceChargeMapping? charge, Dictionary<string, string> row, CsvConfig csvConfig, decimal netPrice, decimal quantity)
    {
        if (charge == null || !charge.Enabled) return;

        var percentStr = Resolve(charge.ChargePercentage, row);
        if (string.IsNullOrEmpty(percentStr)) return;

        var percent = ParseDecimal(percentStr, csvConfig.DecimalSeparator);
        if (percent <= 0) return;

        var reason = Resolve(charge.Reason, row) ?? "Rabatt";
        var isDiscount = Resolve(charge.IsDiscount, row)?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;

        var basisAmount = netPrice * quantity;
        var actualAmount = basisAmount * (percent / 100m);

        if (isDiscount)
        {
            item.AddTradeAllowance(CurrencyCodes.EUR, basisAmount, actualAmount, percent, reason);
            Log.Debug("  Rabatt: {Percent}% = {Amount} (Grund: {Reason})", percent, actualAmount, reason);
        }
        else
        {
            item.AddTradeCharge(CurrencyCodes.EUR, basisAmount, actualAmount, percent, reason);
            Log.Debug("  Zuschlag: {Percent}% = {Amount} (Grund: {Reason})", percent, actualAmount, reason);
        }
    }

    private static void MapTotals(InvoiceDescriptor desc, TotalsMapping? totals, Dictionary<string, string> row, CsvConfig csvConfig)
    {
        if (totals == null || totals.Mode.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            CalculateAutoTotals(desc, csvConfig);
            return;
        }

        // Manual mode
        var lineTotalAmount = ParseDecimal(Resolve(totals.LineTotalAmount, row), csvConfig.DecimalSeparator);
        var allowanceTotalAmount = ParseDecimal(Resolve(totals.AllowanceTotalAmount, row), csvConfig.DecimalSeparator);
        var taxBasisAmount = ParseDecimal(Resolve(totals.TaxBasisAmount, row), csvConfig.DecimalSeparator);
        var taxTotalAmount = ParseDecimal(Resolve(totals.TaxTotalAmount, row), csvConfig.DecimalSeparator);
        var grandTotalAmount = ParseDecimal(Resolve(totals.GrandTotalAmount, row), csvConfig.DecimalSeparator);
        var totalPrepaidAmount = ParseDecimal(Resolve(totals.TotalPrepaidAmount, row), csvConfig.DecimalSeparator);
        var duePayableAmount = grandTotalAmount - totalPrepaidAmount;

        AddTaxBreakdownFromLineItems(desc, taxTotalAmount);

        desc.SetTotals(lineTotalAmount, 0m, allowanceTotalAmount, taxBasisAmount, taxTotalAmount, grandTotalAmount, totalPrepaidAmount, duePayableAmount);
    }

    private static void CalculateAutoTotals(InvoiceDescriptor desc, CsvConfig csvConfig)
    {
        decimal lineTotalAmount = 0m;
        decimal allowanceTotalAmount = 0m;

        // Group tax amounts by percent
        var taxGroups = new Dictionary<decimal, decimal>();

        foreach (var item in desc.TradeLineItems)
        {
            var lineTotal = item.NetUnitPrice * item.BilledQuantity;
            lineTotalAmount += lineTotal;

            decimal lineAllowance = 0m;
            foreach (var charge in item.GetTradeAllowanceCharges())
            {
                if (charge.ChargeIndicator == false) // discount
                    lineAllowance += charge.ActualAmount;
            }
            allowanceTotalAmount += lineAllowance;

            var taxPercent = item.TaxPercent;
            var taxableAmount = lineTotal - lineAllowance;

            if (!taxGroups.ContainsKey(taxPercent))
                taxGroups[taxPercent] = 0m;
            taxGroups[taxPercent] += taxableAmount;
        }

        var taxBasisAmount = lineTotalAmount - allowanceTotalAmount;
        decimal taxTotalAmount = 0m;

        foreach (var (percent, basisAmount) in taxGroups)
        {
            var taxAmount = Math.Round(basisAmount * percent / 100m, 2);
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
            var lineAllowance = item.GetTradeAllowanceCharges()
                .Where(charge => charge.ChargeIndicator == false)
                .Sum(charge => charge.ActualAmount);
            var taxableAmount = item.NetUnitPrice * item.BilledQuantity - lineAllowance;
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
                TaxAmount = Math.Round(group.Value * group.Key.Percent / 100m, 2),
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

    private static void MapPaymentTerms(InvoiceDescriptor desc, PaymentTermsMapping? payment, Dictionary<string, string> row, string dateFormat)
    {
        if (payment == null) return;

        var description = Resolve(payment.Description, row);
        var dueDateStr = Resolve(payment.DueDate, row);

        if (!string.IsNullOrEmpty(description))
        {
            var dueDate = !string.IsNullOrEmpty(dueDateStr) ? ParseDate(dueDateStr, dateFormat) : (DateTime?)null;
            desc.AddTradePaymentTerms(description, dueDate);
        }
    }

    private static void MapPaymentMeans(InvoiceDescriptor desc, PaymentMeansMapping? paymentMeans, Dictionary<string, string> row)
    {
        if (paymentMeans == null) return;

        var typeCodeStr = Resolve(paymentMeans.TypeCode, row);
        if (string.IsNullOrEmpty(typeCodeStr))
        {
            Log.Warning("paymentMeans ohne typeCode (BT-81) wird ignoriert");
            return;
        }

        var typeCode = ParsePaymentMeansTypeCode(typeCodeStr);
        var information = NullIfEmpty(Resolve(paymentMeans.Information, row));
        var isDirectDebit = IsDirectDebit(typeCode);
        var directDebit = paymentMeans.DirectDebit;

        if (directDebit != null && !isDirectDebit)
            Log.Warning("paymentMeans.directDebit wird ignoriert, da typeCode {TypeCode} keine Lastschrift ist", typeCode);

        var creditorId = isDirectDebit ? NullIfEmpty(NormalizeAccountId(Resolve(directDebit?.CreditorId, row))) : null;
        var mandateReference = isDirectDebit ? NullIfEmpty(Resolve(directDebit?.MandateReference, row)) : null;

        desc.SetPaymentMeans(typeCode, information, creditorId, mandateReference);
        Log.Debug("Zahlungsmittel: {TypeCode}, Gläubiger-ID={CreditorId}, Mandat={Mandate}", typeCode, creditorId, mandateReference);

        var paymentReference = Resolve(paymentMeans.PaymentReference, row);
        if (!string.IsNullOrEmpty(paymentReference))
            desc.PaymentReference = paymentReference;

        if (paymentMeans.SellerAccounts != null)
        {
            foreach (var account in paymentMeans.SellerAccounts)
            {
                var iban = NormalizeAccountId(Resolve(account.Iban, row));
                if (string.IsNullOrEmpty(iban)) continue;

                var bic = NullIfEmpty(NormalizeAccountId(Resolve(account.Bic, row)));
                var name = NullIfEmpty(Resolve(account.Name, row));
                desc.AddCreditorFinancialAccount(iban, bic, name: name);
                Log.Debug("Verkäuferkonto: IBAN={Iban}, BIC={Bic}", iban, bic);
            }
        }

        if (isDirectDebit)
        {
            var buyerIban = NormalizeAccountId(Resolve(directDebit?.BuyerIban, row));
            if (!string.IsNullOrEmpty(buyerIban))
            {
                var buyerBic = NullIfEmpty(NormalizeAccountId(Resolve(directDebit?.BuyerBic, row)));
                desc.AddDebitorFinancialAccount(buyerIban, buyerBic);
                Log.Debug("Käuferkonto (Lastschrift): IBAN={Iban}", MaskIban(buyerIban));
            }
        }
    }

    public static IReadOnlyList<string> CheckPaymentMeans(InvoiceDescriptor desc, string? profile)
    {
        var warnings = new List<string>();
        var typeCode = desc.PaymentMeans?.TypeCode;

        if (typeCode == null)
        {
            if (string.Equals(profile, "xrechnung", StringComparison.OrdinalIgnoreCase))
                warnings.Add("XRechnung verlangt Zahlungsanweisungen (BR-DE-1), paymentMeans fehlt");
            return warnings;
        }

        if (typeCode is PaymentMeansTypeCodes.CreditTransferNonSEPA or PaymentMeansTypeCodes.SEPACreditTransfer
            && desc.CreditorBankAccounts.Count == 0)
            warnings.Add($"Zahlungsart {(int)typeCode} (Überweisung) ohne IBAN des Verkäufers (BR-61)");

        if (typeCode == PaymentMeansTypeCodes.SEPADirectDebit)
        {
            if (string.IsNullOrEmpty(desc.PaymentMeans!.SEPACreditorIdentifier))
                warnings.Add("SEPA-Lastschrift ohne Gläubiger-ID (BT-90)");
            if (string.IsNullOrEmpty(desc.PaymentMeans.SEPAMandateReference))
                warnings.Add("SEPA-Lastschrift ohne Mandatsreferenz (BT-89)");
            if (desc.DebitorBankAccounts.Count == 0)
                warnings.Add("SEPA-Lastschrift ohne IBAN des Käufers (BT-91)");
        }

        foreach (var account in desc.CreditorBankAccounts)
        {
            if (!string.IsNullOrEmpty(account.IBAN) && !IsValidIban(account.IBAN))
                warnings.Add($"IBAN des Verkäufers {account.IBAN} hat eine ungültige Prüfsumme");
        }

        foreach (var account in desc.DebitorBankAccounts)
        {
            if (!string.IsNullOrEmpty(account.IBAN) && !IsValidIban(account.IBAN))
                warnings.Add($"IBAN des Käufers {MaskIban(account.IBAN)} hat eine ungültige Prüfsumme");
        }

        return warnings;
    }

    // --- Helper methods ---

    private static string Resolve(FieldMapping? field, Dictionary<string, string> row)
    {
        if (field == null) return string.Empty;
        if (!string.IsNullOrEmpty(field.Value)) return field.Value;
        if (!string.IsNullOrEmpty(field.Column))
        {
            var columnValue = row.GetValueOrDefault(field.Column);
            if (!string.IsNullOrEmpty(columnValue))
                return columnValue;
        }

        var ruleValue = ResolveRule(field, column => row.GetValueOrDefault(column) ?? string.Empty);
        if (!string.IsNullOrEmpty(ruleValue))
            return ruleValue;

        if (!string.IsNullOrEmpty(field.Default))
            return field.Default;

        return string.Empty;
    }

    private static string ResolveRule(FieldMapping? field, Func<string, string> resolveColumn)
    {
        if (field?.Rules == null) return string.Empty;

        foreach (var rule in field.Rules)
        {
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
            }
        }

        return string.Empty;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal GetDiscountPercent(FieldMapping? field, Dictionary<string, string> row, CsvConfig csvConfig) =>
        ParseDecimal(Resolve(field, row), csvConfig.DecimalSeparator);

    private static decimal ApplyDiscount(decimal netPrice, decimal discountPercent) =>
        discountPercent > 0 ? Math.Round(netPrice * (1m - discountPercent / 100m), 4) : netPrice;

    private static DateTime ParseDate(string value, string format)
    {
        if (string.IsNullOrEmpty(value)) return DateTime.MinValue;
        if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;
        return DateTime.MinValue;
    }

    private static decimal ParseDecimal(string value, string decimalSeparator)
    {
        if (string.IsNullOrEmpty(value)) return 0m;
        var normalized = value.Trim().TrimEnd('%').Trim();
        normalized = decimalSeparator == "," ? normalized.Replace(",", ".") : normalized;
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static CurrencyCodes ResolveCurrency(string value) =>
        Enum.TryParse<CurrencyCodes>(value, true, out var code) ? code : CurrencyCodes.EUR;

    private static CountryCodes ResolveCountry(string value) =>
        Enum.TryParse<CountryCodes>(value, true, out var code) ? code : CountryCodes.DE;

    private static TaxRegistrationSchemeID ParseTaxScheme(string? value) =>
        value?.ToUpperInvariant() switch
        {
            "FC" => TaxRegistrationSchemeID.FC,
            "VA" => TaxRegistrationSchemeID.VA,
            _ => TaxRegistrationSchemeID.VA,
        };

    private static ElectronicAddressSchemeIdentifiers ParseElectronicAddressScheme(string? value) =>
        value switch
        {
            "GermanyVatNumber" => ElectronicAddressSchemeIdentifiers.GermanyVatNumber,
            "LuxemburgVatNumber" => ElectronicAddressSchemeIdentifiers.LuxemburgVatNumber,
            _ => ElectronicAddressSchemeIdentifiers.GermanyVatNumber,
        };

    private static PaymentMeansTypeCodes ParsePaymentMeansTypeCode(string value)
    {
        var trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined(typeof(PaymentMeansTypeCodes), number))
            return (PaymentMeansTypeCodes)number;
        if (!int.TryParse(trimmed, out _) && Enum.TryParse<PaymentMeansTypeCodes>(trimmed, true, out var code))
            return code;
        throw new InvalidOperationException($"Unbekannter Zahlungsart-Code (BT-81) in paymentMeans.typeCode: '{value}'");
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

    private static QuantityCodes ParseUnitCode(string value) =>
        Enum.TryParse<QuantityCodes>(value, true, out var code) ? code : QuantityCodes.C62;

    private static TaxTypes ParseTaxType(string value) =>
        Enum.TryParse<TaxTypes>(value, true, out var code) ? code : TaxTypes.VAT;

    private static TaxCategoryCodes ParseTaxCategory(string value) =>
        Enum.TryParse<TaxCategoryCodes>(value, true, out var code) ? code : TaxCategoryCodes.S;
}
