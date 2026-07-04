using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFramework.Taos.Metadata.Internal;

public sealed class TaosAnnotationProvider : RelationalAnnotationProvider
{
    public TaosAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
        {
            yield return annotation;
        }

        if (table.EntityTypeMappings.Any(
                mapping => mapping.TypeBase.FindAnnotation(TaosAnnotationNames.IsStable)?.Value as bool? == true))
        {
            yield return new Annotation(TaosAnnotationNames.IsStable, true);
        }
    }

    public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
    {
        foreach (var annotation in base.For(column, designTime))
        {
            yield return annotation;
        }

        if (column.PropertyMappings.Any(
                mapping => mapping.Property.FindAnnotation(TaosAnnotationNames.IsTag)?.Value as bool? == true))
        {
            yield return new Annotation(TaosAnnotationNames.IsTag, true);
        }

        if (column.PropertyMappings.Any(
                mapping => mapping.Property.FindAnnotation(TaosAnnotationNames.IsTimestamp)?.Value as bool? == true))
        {
            yield return new Annotation(TaosAnnotationNames.IsTimestamp, true);
        }
    }
}
