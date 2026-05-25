namespace DiDecoration.Utils;

/// <summary>
/// Controls which classes are included when scanning an assembly for decoration-based registrations.
/// </summary>
public sealed class DecorationScanOptions
{
    /// <summary>
    /// Gets or sets the namespace prefix filter. When set, only classes whose namespace starts with this value are included.
    /// </summary>
    public string? NamespacePrefix { get; init; }

    /// <summary>
    /// Gets or sets an additional predicate used to include or exclude a type.
    /// </summary>
    public Func<Type, bool>? Predicate { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether internal types should be included in the scan.
    /// </summary>
    public bool IncludeInternalTypes { get; init; } = true;
}

