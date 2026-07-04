namespace EntityFramework.Taos.Metadata;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TaosStableAttribute : Attribute
{
    public TaosStableAttribute()
    {
    }

    public TaosStableAttribute(string name)
        => Name = name;

    public string? Name { get; }
}
