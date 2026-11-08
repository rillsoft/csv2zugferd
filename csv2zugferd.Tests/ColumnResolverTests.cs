using Csv2Zugferd.Models;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class ColumnResolverTests
{
    [Test]
    public void ColumnResolver_PrefixAndSuffix_PreserveMapperResults()
    {
        var prefix = new ColumnPatternConfig { Style = "prefix", Prefix = "Pos", Separator = "_", StartIndex = 1, ZeroPadding = 0 };
        var suffix = new ColumnPatternConfig { Style = "suffix", Separator = "_", StartIndex = 1, ZeroPadding = 3 };

        ColumnResolver.BuildColumnName(prefix, 1, "Artikel").ShouldBe("Pos1_Artikel");
        ColumnResolver.BuildColumnName(prefix, 12, "Menge").ShouldBe("Pos12_Menge");
        ColumnResolver.BuildColumnName(suffix, 1, "Artikel").ShouldBe("Artikel_001");
        ColumnResolver.BuildColumnName(suffix, 12, "Menge").ShouldBe("Menge_012");

        // Dieselben Spalten findet der Mapper in den Testdaten
        var prefixDesc = TestHelper.MapFromTestData("config_columns_prefix.yml", "test_columns_prefix.csv");
        var suffixDesc = TestHelper.MapFromTestData("config_columns_suffix.yml", "test_columns_suffix.csv");
        prefixDesc.TradeLineItems.Select(i => i.Name).ShouldBe(["Test1", "Test2"]);
        suffixDesc.TradeLineItems.Select(i => i.Name).ShouldBe(["Test1", "Test2"]);
    }

    [TestCase("prefix", true)]
    [TestCase("PREFIX", true)]
    [TestCase("suffix", false)]
    [TestCase("", false)]
    [TestCase("unbekannt", false)]
    public void ColumnResolver_IsPrefixStyle_MatchesMapperFallback(string style, bool expected)
    {
        ColumnResolver.IsPrefixStyle(new ColumnPatternConfig { Style = style }).ShouldBe(expected);
    }

    [Test]
    public void ColumnResolver_EnumerateCandidates_ListsDirectThenIndexedColumns()
    {
        var pattern = new ColumnPatternConfig { Style = "suffix", Separator = "_", StartIndex = 0 };

        ColumnResolver.EnumerateCandidates(pattern, "PRODUCT_CODE", 3)
            .ShouldBe(["PRODUCT_CODE", "PRODUCT_CODE_0", "PRODUCT_CODE_1", "PRODUCT_CODE_2"]);
    }

    [Test]
    public void ColumnResolver_FormatIndex_ZeroPadding()
    {
        ColumnResolver.FormatIndex(new ColumnPatternConfig { ZeroPadding = 0 }, 7).ShouldBe("7");
        ColumnResolver.FormatIndex(new ColumnPatternConfig { ZeroPadding = 2 }, 7).ShouldBe("07");
        ColumnResolver.FormatIndex(new ColumnPatternConfig { ZeroPadding = 2 }, 123).ShouldBe("123");
    }
}
