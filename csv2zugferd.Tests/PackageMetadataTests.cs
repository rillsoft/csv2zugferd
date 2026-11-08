using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests;

/// <summary>Versionen und Registry-Marker von Build, Paket und Manifest stimmen überein.</summary>
[TestFixture]
public class PackageMetadataTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository-Wurzel nicht gefunden");
    }

    private static string BuildVersion()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        return Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
    }

    [Test]
    public void ServerJson_Version_MatchesBuildVersion()
    {
        var version = BuildVersion();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ".mcp", "server.json")));
        var root = document.RootElement;

        root.GetProperty("version").GetString().ShouldBe(version);
        var package = root.GetProperty("packages")[0];
        package.GetProperty("version").GetString().ShouldBe(version);
        package.GetProperty("registryType").GetString().ShouldBe("nuget");
        package.GetProperty("identifier").GetString().ShouldBe("csv2zugferd");
        package.GetProperty("transport").GetProperty("type").GetString().ShouldBe("stdio");
        package.GetProperty("packageArguments")[0].GetProperty("value").GetString().ShouldBe("mcp");
        root.GetProperty("description").GetString()!.Length.ShouldBeLessThanOrEqualTo(100);
    }

    [Test]
    public void ServerJson_Name_MatchesReadmeMarker()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ".mcp", "server.json")));
        var name = document.RootElement.GetProperty("name").GetString()!;
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

        name.ShouldBe("de.zugferd-tools/csv2zugferd");
        readme.ShouldContain($"<!-- mcp-name: {name} -->");
    }

    [Test]
    public void ServerVersion_MatchesBuildVersion()
    {
        Csv2Zugferd.Mcp.McpServerCommand.GetVersion().ShouldBe(BuildVersion());
    }

    [Test]
    public void Dockerfile_VersionLabelAndServerName_MatchManifest()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot(), "Dockerfile"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ".mcp", "server.json")));
        var name = document.RootElement.GetProperty("name").GetString()!;

        Regex.Match(dockerfile, @"ARG APP_VERSION=(\S+)").Groups[1].Value.ShouldBe(BuildVersion());
        dockerfile.ShouldContain($"io.modelcontextprotocol.server.name=\"{name}\"");
        dockerfile.ShouldContain("ENTRYPOINT [\"dotnet\", \"/app/csv2zugferd.dll\"]");
        dockerfile.ShouldContain("CMD [\"mcp\"]");
    }

    /// <summary>
    /// Ein Rückstrich in PackagePath ist unter Linux kein Verzeichnistrenner; dotnet pack erzeugte so
    /// in der Pipeline den Eintrag ".mcp//server.json", den nuget.org nicht als Manifest erkannte (1.3.0).
    /// </summary>
    [Test]
    public void Csproj_PackagePaths_UseForwardSlashesOnly()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "csv2zugferd", "csv2zugferd.csproj"));
        var packagePaths = Regex.Matches(csproj, "PackagePath=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        packagePaths.ShouldContain(".mcp/");
        packagePaths.ShouldAllBe(path => !path.Contains('\\'));
    }

    [Test]
    public void GlamaJson_NamesMaintainer()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "glama.json")));
        document.RootElement.GetProperty("$schema").GetString().ShouldBe("https://glama.ai/mcp/schemas/server.json");
        document.RootElement.GetProperty("maintainers").EnumerateArray().Select(m => m.GetString()).ShouldContain("rillsoft");
    }
}
