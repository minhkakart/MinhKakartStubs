using System.Collections.Immutable;
using System.Reflection;

namespace DiDecoration.Utils;

public static class AppUtils
{
    public static ImmutableList<Type> GetClassTypes(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();
        var types = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToImmutableList();
        return types;
    }
}