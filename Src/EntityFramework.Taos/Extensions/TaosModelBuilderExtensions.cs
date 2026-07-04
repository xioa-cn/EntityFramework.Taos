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
        // 从 EF Core 视角看，TDengine 超级表仍然是关系型表。
        // provider 注解会让 DDL 和 INSERT 生成 STABLE/USING/TAGS 语法。
        builder.ToTable(name);
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsStable, true);
        return builder;
    }

    public static PropertyBuilder<TProperty> IsTaosTimestamp<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        // TDengine 要求时间戳列属于普通列列表，
        // provider 在生成 DDL/INSERT 时会把它排在其他值列之前。
        builder.HasColumnType("timestamp");
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsTimestamp, true);
        return builder;
    }

    public static PropertyBuilder<TProperty> IsTaosTag<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        // 标签建模为 EF 属性，这样 LINQ 仍然可以按标签过滤。
        // 生成 DDL 和 INSERT 时，这些属性会进入 TAGS，而不是普通值列。
        builder.Metadata.SetAnnotation(TaosAnnotationNames.IsTag, true);
        return builder;
    }
}
