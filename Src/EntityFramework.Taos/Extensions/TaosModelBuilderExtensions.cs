using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFramework.Taos.Extensions;

public static class TaosModelBuilderExtensions
{
    public static EntityTypeBuilder<TEntity> ToStable<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string name)
        where TEntity : class
    {
        // A TDengine stable is still a relational table from EF Core's point of view.
        // The provider annotation tells DDL and INSERT generation to emit STABLE/USING/TAGS syntax.
        builder.ToTable(name);
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsStable, true);
        return builder;
    }

    public static PropertyBuilder<TProperty> IsTaosTimestamp<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        // TDengine requires the timestamp column to be part of the normal column list,
        // and provider DDL/INSERT generation orders it before other value columns.
        builder.HasColumnType("timestamp");
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsTimestamp, true);
        return builder;
    }

    public static PropertyBuilder<TProperty> IsTaosTag<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        // Tags are modeled as EF properties so LINQ can still filter by them.
        // DDL and INSERT generation move these properties to TAGS instead of value columns.
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsTag, true);
        return builder;
    }
}
