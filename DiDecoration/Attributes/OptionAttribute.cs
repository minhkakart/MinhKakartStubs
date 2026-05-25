namespace DiDecoration.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OptionAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}