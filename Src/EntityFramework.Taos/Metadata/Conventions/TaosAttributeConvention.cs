using System.Reflection;
using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFramework.Taos.Metadata.Conventions;

public sealed class TaosAttributeConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            ApplyStableAttribute(entityType);

            var timestampProperties = new List<IConventionProperty>();
            foreach (var property in entityType.GetProperties())
            {
                if (ApplyPropertyAttributes(property))
                {
                    timestampProperties.Add(property);
                }
            }

            if (entityType.FindPrimaryKey() is null && timestampProperties.Count == 1)
            {
                entityType.Builder.PrimaryKey(timestampProperties, fromDataAnnotation: true);
            }
        }
    }

    private static void ApplyStableAttribute(IConventionEntityType entityType)
    {
        var stableAttribute = entityType.ClrType.GetCustomAttribute<TaosStableAttribute>(inherit: false);
        if (stableAttribute is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(stableAttribute.Name))
        {
            entityType.SetTableName(stableAttribute.Name, fromDataAnnotation: true);
        }

        entityType.SetAnnotation(TaosAnnotationNames.IsStable, true, fromDataAnnotation: true);
    }

    private static bool ApplyPropertyAttributes(IConventionProperty property)
    {
        var memberInfo = property.PropertyInfo ?? (MemberInfo?)property.FieldInfo;
        if (memberInfo is null)
        {
            return false;
        }

        var isTimestamp = false;
        if (memberInfo.GetCustomAttribute<TaosTimestampAttribute>(inherit: true) is not null)
        {
            property.SetColumnType("timestamp", fromDataAnnotation: true);
            property.SetAnnotation(TaosAnnotationNames.IsTimestamp, true, fromDataAnnotation: true);
            isTimestamp = true;
        }

        if (memberInfo.GetCustomAttribute<TaosTagAttribute>(inherit: true) is not null)
        {
            property.SetAnnotation(TaosAnnotationNames.IsTag, true, fromDataAnnotation: true);
        }

        return isTimestamp;
    }
}
