namespace DiDecoration.Attributes;

/// <summary>
/// Marks a class as bindable from a configuration section.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OptionAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}