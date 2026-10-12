using System.Reflection;

namespace ApiGateway.Host.CompositionRoot;

/// <summary>
/// Configuration binding that <em>replaces</em> array defaults instead of appending to them. The stock binder keeps
/// an array's initial elements and appends the configured ones, so a default like <c>["en", "tr"]</c> plus a
/// configured <c>["en", "de"]</c> would become four entries. Arrays that the configuration sets are cleared first
/// (recursively through nested options objects); arrays it does not mention keep their defaults.
/// </summary>
internal static class ConfigurationBinding
{
    public static void Bind(IConfiguration section, object options)
    {
        ClearConfiguredArrays(section, options, depth: 0);
        section.Bind(options);
    }

    private static void ClearConfiguredArrays(IConfiguration section, object target, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (var property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var child = section.GetSection(property.Name);
            if (!child.GetChildren().Any())
            {
                continue;
            }

            if (property.PropertyType.IsArray && property.CanWrite)
            {
                property.SetValue(target, Array.CreateInstance(property.PropertyType.GetElementType()!, 0));
            }
            else if (IsOptionsObject(property.PropertyType) && property.GetValue(target) is { } nested)
            {
                ClearConfiguredArrays(child, nested, depth + 1);
            }
        }
    }

    private static bool IsOptionsObject(Type type) =>
        type.IsClass
        && type != typeof(string)
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
        && type.Namespace?.StartsWith("ApiGateway", StringComparison.Ordinal) == true;
}
