namespace EntityFramework.Taos.Metadata.Internal;

public static class TaosAnnotationNames
{
    public const string Prefix = "Taos:";
    public const string IsStable = Prefix + nameof(IsStable);
    public const string IsTag = Prefix + nameof(IsTag);
    public const string IsTimestamp = Prefix + nameof(IsTimestamp);
}
