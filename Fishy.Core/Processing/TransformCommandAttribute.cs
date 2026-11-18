namespace Fishy.Processing;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TransformCommandAttribute(string tool, string name) : Attribute
{
    public string Tool { get; } = tool;
    public string Name { get; } = name;
    public string[] Aliases { get; init; } = [];
    public int Order { get; init; } = 100;
    public bool IncludeInFull { get; init; }
    public bool AutoSelectOnDrop { get; init; }
}
