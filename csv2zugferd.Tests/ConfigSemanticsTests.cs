using System.Reflection;
using Csv2Zugferd.Models;
using NUnit.Framework;
using Shouldly;
using YamlDotNet.Serialization.NamingConventions;

namespace Csv2Zugferd.Tests;

/// <summary>
/// Vollständigkeit aus Struktur: Jede öffentliche Eigenschaft der Modellklassen trägt eine englische Beschreibung
/// und hat einen Eintrag in <see cref="ConfigSemantics"/>; umgekehrt zeigt jeder Semantikpfad auf ein Modellfeld.
/// </summary>
[TestFixture]
public class ConfigSemanticsTests
{
    private static readonly Type[] s_genericTypes = [typeof(FieldMapping), typeof(FieldRule), typeof(FieldRuleCondition)];

    public static IEnumerable<Type> ModelTypes() =>
        typeof(AppConfig).Assembly.GetTypes()
            .Where(t => t.Namespace == "Csv2Zugferd.Models" && t.IsClass && t.IsPublic)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    [TestCaseSource(nameof(ModelTypes))]
    public void Models_AllPublicProperties_HaveDescription(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var description = property.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;
            description.ShouldNotBeNullOrWhiteSpace($"{type.Name}.{property.Name}");
            description.All(c => c < 128).ShouldBeTrue($"{type.Name}.{property.Name}: Beschreibung muss ASCII sein");
        }
    }

    [Test]
    public void Models_PropertyCount_Is114()
    {
        ModelTypes().Sum(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length).ShouldBe(114);
    }

    [Test]
    public void ConfigSemantics_CoversEveryModelPropertyPath_BothDirections()
    {
        var modelPaths = new List<string>();
        Collect(typeof(AppConfig), string.Empty, modelPaths);

        var semanticPaths = ConfigSemantics.All.Select(s => s.Path).ToList();
        semanticPaths.ShouldBeUnique();

        var missingInSemantics = modelPaths.Where(p => !semanticPaths.Contains(p)).ToList();
        missingInSemantics.ShouldBeEmpty("Modellpfade ohne Semantik");

        var fixedFieldPaths = YamlConfigLoader.EffectiveFixedFieldKeys.Select(k => "mapping.lineItems.fixedFields." + k).ToList();
        var missingInModel = semanticPaths
            .Where(p => !p.StartsWith("fieldMapping.", StringComparison.Ordinal))
            .Where(p => !fixedFieldPaths.Contains(p))
            .Where(p => !modelPaths.Contains(p))
            .ToList();
        missingInModel.ShouldBeEmpty("Semantikpfade ohne Modellfeld");

        foreach (var key in fixedFieldPaths)
            semanticPaths.ShouldContain(key);
    }

    [Test]
    public void ConfigSemantics_GenericFieldMappingKeys_AreDescribed()
    {
        foreach (var property in typeof(FieldMapping).GetProperties())
            ConfigSemantics.Find("fieldMapping." + Camel(property.Name)).ShouldNotBeNull(property.Name);
        ConfigSemantics.Find("fieldMapping.rules[].when").ShouldNotBeNull();
        ConfigSemantics.Find("fieldMapping.rules[].value").ShouldNotBeNull();
        ConfigSemantics.Find("fieldMapping.rules[].when.column").ShouldNotBeNull();
        ConfigSemantics.Find("fieldMapping.rules[].when.regex").ShouldNotBeNull();
    }

    [Test]
    public void ConfigSemantics_Descriptions_AreAsciiAndNonEmpty()
    {
        foreach (var semantic in ConfigSemantics.All)
        {
            semantic.Description.ShouldNotBeNullOrWhiteSpace(semantic.Path);
            semantic.Description.All(c => c < 128).ShouldBeTrue(semantic.Path);
            foreach (var source in semantic.Sources)
                source.ShouldBeOneOf("value", "column", "rules", "default");
        }
    }

    [Test]
    public void ConfigSemantics_IgnoredFields_AreExactlyTheKnownOnes()
    {
        ConfigSemantics.All.Where(s => s.Status == FieldStatus.Ignored).Select(s => s.Path)
            .ShouldBe(["mapping.lineItems.fields.discountReason", "mapping.lineItems.columnsFields"], ignoreOrder: true);
    }

    private static void Collect(Type type, string prefix, List<string> paths)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix.Length == 0 ? Camel(property.Name) : $"{prefix}.{Camel(property.Name)}";
            paths.Add(path);

            var propertyType = property.PropertyType;
            if (s_genericTypes.Contains(propertyType))
                continue;

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var element = propertyType.GetGenericArguments()[0];
                if (!s_genericTypes.Contains(element) && element.Namespace == "Csv2Zugferd.Models")
                    Collect(element, path + "[]", paths);
                continue;
            }

            if (propertyType.Namespace == "Csv2Zugferd.Models" && propertyType.IsClass)
                Collect(propertyType, path, paths);
        }
    }

    private static string Camel(string name) => CamelCaseNamingConvention.Instance.Apply(name);
}
