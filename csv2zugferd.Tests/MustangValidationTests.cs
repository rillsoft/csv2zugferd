using System.Diagnostics;
using System.Xml.Linq;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests;

/// <summary>
/// Validiert die erzeugten ZUGFeRD-XML-Dateien mit dem Mustang-CLI (Schema, EN16931- und XRechnung-Schematron).
/// Läuft nur bei expliziter Auswahl, weil Java und das Mustang-CLI-Jar lokal vorhanden sein müssen:
/// <c>dotnet test --filter Category=ExternalValidation</c>.
/// Jar-Pfad: Umgebungsvariable <c>MUSTANG_CLI_JAR</c> oder <c>tools/Mustang-CLI.jar</c> im Repository (siehe tools/Get-MustangCli.ps1).
/// </summary>
[TestFixture]
[Explicit("Benötigt Java und das Mustang-CLI-Jar (tools/Get-MustangCli.ps1)")]
[Category("ExternalValidation")]
public class MustangValidationTests
{
    private const string JarEnvVar = "MUSTANG_CLI_JAR";
    private const string JarRelativePath = "tools/Mustang-CLI.jar";

    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestCase("config_rows.yml", "test_rows.csv")]
    [TestCase("config_columns_suffix.yml", "test_columns_suffix.csv")]
    [TestCase("config_columns_prefix.yml", "test_columns_prefix.csv")]
    [TestCase("config_creditnote.yml", "test_creditnote.csv")]
    [TestCase("config_directdebit.yml", "test_directdebit.csv")]
    public void SaveXml_TestData_IsValidAccordingToMustang(string configFile, string csvFile)
    {
        var jarPath = ResolveJarPath();
        var config = TestHelper.LoadTestConfig(configFile);
        var desc = TestHelper.MapFromTestData(configFile, csvFile);
        var xmlPath = Path.Combine(_tempDir, Path.ChangeExtension(csvFile, ".xml"));

        InvoiceExporter.SaveXml(desc, xmlPath, config.Zugferd);

        var result = RunMustangValidate(jarPath, xmlPath);

        TestContext.Out.WriteLine(result.Report);
        result.Status.ShouldBe("valid", $"Mustang meldet Fehler für {csvFile}:{Environment.NewLine}{result.Messages}");
        result.ExitCode.ShouldBe(0, $"Mustang-CLI Exit-Code für {csvFile}");
    }

    private static string ResolveJarPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(JarEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        var fromRepo = repoRoot is null ? null : Path.Combine(repoRoot, JarRelativePath);
        if (fromRepo is not null && File.Exists(fromRepo))
            return fromRepo;

        Assert.Inconclusive(
            $"Mustang-CLI-Jar nicht gefunden. Setze {JarEnvVar} oder lade es mit tools/Get-MustangCli.ps1 nach {JarRelativePath}.");
        return null!;
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "csv2zugferd.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static MustangResult RunMustangValidate(string jarPath, string xmlPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "java",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(xmlPath)!,
        };
        psi.ArgumentList.Add("-Xmx1G");
        psi.ArgumentList.Add("-Dfile.encoding=UTF-8");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(jarPath);
        psi.ArgumentList.Add("--no-notices");
        psi.ArgumentList.Add("--action");
        psi.ArgumentList.Add("validate");
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(xmlPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Java-Prozess konnte nicht gestartet werden.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Mustang-CLI hat nicht innerhalb von 3 Minuten geantwortet.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return ParseReport(stdout, stderr, process.ExitCode);
    }

    private static MustangResult ParseReport(string stdout, string stderr, int exitCode)
    {
        var start = stdout.IndexOf("<validation", StringComparison.Ordinal);
        if (start < 0)
            Assert.Fail($"Keine Mustang-Validierungsausgabe gefunden (Exit-Code {exitCode}).{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var report = stdout[start..];
        var doc = XDocument.Parse(report);
        var status = doc.Descendants("summary").Attributes("status").Select(a => a.Value).FirstOrDefault() ?? "unknown";
        var messages = string.Join(Environment.NewLine, doc.Descendants("messages").Elements().Select(e => e.ToString()));

        return new MustangResult(status, messages, report, exitCode);
    }

    private sealed record MustangResult(string Status, string Messages, string Report, int ExitCode);
}
