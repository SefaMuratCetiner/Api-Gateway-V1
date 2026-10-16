using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ApiGateway.Abstractions.Errors;
using ApiGateway.Abstractions.Routing;
using ApiGateway.Errors;
using ApiGateway.Observability;
using ApiGateway.Observability.Serilog;
using ApiGateway.Proxy.Routing;
using Microsoft.Extensions.Options;

namespace ApiGateway.UnitTests.Architecture;

/// <summary>
/// The conventions described in <c>docs/architecture.md</c>, checked so they cannot drift. Each test guards one rule;
/// when one fails, either fix the code or change the rule in the document and here together.
/// </summary>
public sealed partial class ConventionTests
{
    private static readonly Assembly[] Libraries =
    [
        typeof(GatewayErrors).Assembly,
        typeof(ErrorsOptions).Assembly,
        typeof(RoutingOptions).Assembly,
        typeof(ObservabilityOptions).Assembly,
        typeof(LoggingOptions).Assembly,
    ];

    private static readonly Type[] LibraryTypes = [.. Libraries.SelectMany(static assembly => assembly.GetTypes())];

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Projects_reference_only_the_projects_below_them()
    {
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ApiGateway.Abstractions"] = [],
            ["ApiGateway.Errors"] = ["ApiGateway.Abstractions"],
            ["ApiGateway.Proxy"] = ["ApiGateway.Abstractions"],
            ["ApiGateway.Observability"] = ["ApiGateway.Abstractions"],
            ["ApiGateway.Observability.Serilog"] = ["ApiGateway.Abstractions", "ApiGateway.Observability"],
        };

        foreach (var (project, mayReference) in allowed)
        {
            ProjectReferencesOf(project).ShouldBeSubsetOf(mayReference, $"{project} references a project it must not depend on.");
        }

        PackageReferencesOf("ApiGateway.Abstractions").ShouldBeEmpty("Abstractions has no package references, so backends can adopt the error contract.");
    }

    [Fact]
    public void Request_features_live_in_Abstractions_Features()
    {
        var misplaced = LibraryTypes
            .Where(static type => type.IsInterface && FeatureInterface().IsMatch(type.Name))
            .Where(static type => !string.Equals(type.Namespace, "ApiGateway.Abstractions.Features", StringComparison.Ordinal))
            .Select(static type => type.FullName);

        misplaced.ShouldBeEmpty();
    }

    [Fact]
    public void Only_bound_configuration_is_named_Options()
    {
        var roots = LibraryTypes.Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true });
        var bound = new HashSet<Type>();
        foreach (var root in roots)
        {
            CollectOptionsTypes(root, bound);
        }

        roots.ShouldAllBe(static type => type.Name.EndsWith("Options", StringComparison.Ordinal));
        LibraryTypes
            .Where(static type => type.IsClass && !type.IsNested && type.Name.EndsWith("Options", StringComparison.Ordinal))
            .Where(type => !bound.Contains(type))
            .Select(static type => type.FullName)
            .ShouldBeEmpty("Types named *Options must be a configuration section or part of one.");
    }

    [Fact]
    public void Validators_are_named_after_their_options_and_kind()
    {
        foreach (var validator in LibraryTypes.Where(static type => type.IsClass && !type.IsAbstract))
        {
            var validated = validator.GetInterfaces()
                .FirstOrDefault(static contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidateOptions<>))
                ?.GetGenericArguments()[0];
            if (validated is null)
            {
                continue;
            }

            var generated = validator.GetCustomAttributesData().Any(static attribute => attribute.AttributeType == typeof(OptionsValidatorAttribute));
            var expected = validated.Name + (generated ? "Validator" : "ConsistencyValidator");
            validator.Name.ShouldBe(expected, generated
                ? "[OptionsValidator] validators are named XOptionsValidator."
                : "Hand-written validators are named XOptionsConsistencyValidator.");
        }
    }

    [Fact]
    public void Metadata_keys_start_with_their_feature_folder()
    {
        foreach (var validator in LibraryTypes.Where(static type => type.IsClass && !type.IsAbstract && typeof(IRouteMetadataValidator).IsAssignableFrom(type)))
        {
            var prefix = ((IRouteMetadataValidator)RuntimeHelpers.GetUninitializedObject(validator)).Prefix;
            ShouldFollowKeyConvention(validator, prefix);
        }

        // Destination metadata has no parser interface; its owners declare MetadataPrefix.
        foreach (var owner in LibraryTypes.Where(static type => type.GetField("MetadataPrefix", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true }))
        {
            ShouldFollowKeyConvention(owner, (string)owner.GetField("MetadataPrefix")!.GetRawConstantValue()!);
        }
    }

    [Fact]
    public void Instruments_are_created_only_in_metrics_classes()
    {
        SourceFiles("src")
            .Where(static file => Instrument().IsMatch(File.ReadAllText(file)))
            .Select(static file => Path.GetFileName(file))
            .ShouldAllBe(static name => name.EndsWith("Metrics.cs", StringComparison.Ordinal) || name.EndsWith("Gauge.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Files_are_named_after_a_type_they_declare_and_namespaces_follow_folders()
    {
        var problems = new List<string>();
        foreach (var file in SourceFiles("src"))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(Path.Combine(RepositoryRoot, "src"), file);
            var typeName = Path.GetFileName(file).Split('.')[0];

            var declared = TopLevelType().Matches(text).Select(static match => match.Groups["name"].Value).ToList();
            if (!declared.Contains(typeName, StringComparer.Ordinal))
            {
                problems.Add($"{relative}: declares [{string.Join(", ", declared)}], none named {typeName}");
            }

            if (Namespace().Match(text) is { Success: true } match)
            {
                var folder = Path.GetDirectoryName(relative)!.Replace(Path.DirectorySeparatorChar, '.');
                if (!string.Equals(match.Groups["name"].Value, folder, StringComparison.Ordinal))
                {
                    problems.Add($"{relative}: namespace {match.Groups["name"].Value}, expected {folder}");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Features_register_services_and_leave_ordering_to_the_composition_root()
    {
        var problems = SourceFiles(Path.Combine("src", "ApiGateway.Host", "Features"))
            .SelectMany(static file => OrderSensitiveCall().Matches(File.ReadAllText(file))
                .Select(match => $"{Path.GetFileName(file)}: {match.Value}"));

        problems.ShouldBeEmpty("Middleware, endpoints, YARP config filters and transforms are ordered in GatewayCompositionRoot.");
    }

    [Fact]
    public void Text_uses_American_spelling()
    {
        string[] roots = ["src", "tests", "docs", "deploy"];
        var problems = roots
            .SelectMany(static root => SourceFiles(root, "*.*"))
            .Where(static file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".md", StringComparison.Ordinal)
                || file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".yaml", StringComparison.Ordinal))
            .Where(static file => !file.EndsWith("packages.lock.json", StringComparison.Ordinal) && !file.EndsWith(nameof(ConventionTests) + ".cs", StringComparison.Ordinal))
            .SelectMany(static file => BritishSpelling().Matches(File.ReadAllText(file))
                .Select(match => $"{Path.GetRelativePath(RepositoryRoot, file)}: {match.Value}"));

        problems.ShouldBeEmpty();
    }

    private static void ShouldFollowKeyConvention(Type owner, string prefix)
    {
        prefix.ShouldBe(owner.Namespace!.Split('.')[^1] + ".", $"{owner.Name}: metadata keys are prefixed with the feature folder.");

        foreach (var key in owner.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Key", StringComparison.Ordinal)))
        {
            ((string)key.GetRawConstantValue()!).ShouldStartWith(prefix, Case.Sensitive, $"{owner.Name}.{key.Name}");
        }
    }

    /// <summary>Adds <paramref name="type"/> and every options type reachable through its properties, arrays and dictionaries.</summary>
    private static void CollectOptionsTypes(Type type, HashSet<Type> found)
    {
        if (!found.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var candidate = property.PropertyType;
            candidate = candidate.IsArray ? candidate.GetElementType()! : candidate;
            candidate = candidate.IsGenericType ? candidate.GetGenericArguments()[^1] : candidate;
            if (candidate.IsClass && candidate.Name.EndsWith("Options", StringComparison.Ordinal) && Libraries.Contains(candidate.Assembly))
            {
                CollectOptionsTypes(candidate, found);
            }
        }
    }

    private static string[] ProjectReferencesOf(string project) =>
        [.. ProjectFile(project).Descendants("ProjectReference")
            .Select(static reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))];

    private static string[] PackageReferencesOf(string project) =>
        [.. ProjectFile(project).Descendants("PackageReference").Select(static reference => reference.Attribute("Include")!.Value)];

    private static XDocument ProjectFile(string project) =>
        XDocument.Load(Path.Combine(RepositoryRoot, "src", project, project + ".csproj"));

    private static IEnumerable<string> SourceFiles(string relativeRoot, string pattern = "*.cs") =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativeRoot), pattern, SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApiGateway.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (ApiGateway.slnx) not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^I\w+Feature$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FeatureInterface();

    [GeneratedRegex(@"\.Create(Counter|Histogram|UpDownCounter|Gauge|Observable\w+)<", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Instrument();

    [GeneratedRegex(@"^(public|internal|file)\s[^(\n]*?\b(class|record|struct|interface|enum)\s+(?<name>\w+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TopLevelType();

    [GeneratedRegex(@"^namespace (?<name>[\w.]+);", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Namespace();

    [GeneratedRegex(@"\b(UseMiddleware|AddTransforms|AddConfigFilter|MapReverseProxy|Map(Get|Post|Methods|HealthChecks))\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OrderSensitiveCall();

    [GeneratedRegex(@"\b\w*(catalogue|behaviour|labelled|cancelled|licence|defence|honour|neighbour|colour|favour|analyse|initialis|normalis|serialis|organis|recognis|authoris|customis|optimis|summaris|prioritis|utilis|anonymis|sanitis|tokenis)\w*", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BritishSpelling();
}
