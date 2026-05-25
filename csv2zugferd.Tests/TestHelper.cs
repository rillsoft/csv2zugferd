using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;

namespace Csv2Zugferd.Tests;

public static class TestHelper
{
    public static string GetTestDataPath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", fileName);

    public static AppConfig LoadTestConfig(string configFile)
        => YamlConfigLoader.Load(GetTestDataPath(configFile));

    public static List<Dictionary<string, string>> ReadTestCsv(string csvFile, CsvConfig csvConfig)
        => CsvInvoiceReader.Read(GetTestDataPath(csvFile), csvConfig);

    public static InvoiceDescriptor MapFromTestData(string configFile, string csvFile)
    {
        var config = LoadTestConfig(configFile);
        var rows = ReadTestCsv(csvFile, config.Csv);
        return InvoiceMapper.Map(rows, config);
    }
}
