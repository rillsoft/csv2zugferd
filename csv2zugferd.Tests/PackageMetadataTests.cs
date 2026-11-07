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
}
