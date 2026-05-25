using System.Collections.Immutable;
using System.Reflection;

namespace DiDecoration.Utils;

public static class AppUtils
{
    public static ImmutableList<Type> GetClassTypes(Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        var types = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => scanOptions is null || MatchesScanOptions(t, scanOptions))
            .ToImmutableList();
        return types;
    }

    private static bool MatchesScanOptions(Type type, DecorationScanOptions scanOptions)
    {
        if (!scanOptions.IncludeInternalTypes && !(type.IsPublic || type.IsNestedPublic))
        {
            return false;
        }

        if (scanOptions.NamespacePrefix is not null)
        {
            if (type.Namespace is null || !type.Namespace.StartsWith(scanOptions.NamespacePrefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return scanOptions.Predicate?.Invoke(type) ?? true;
    }
}