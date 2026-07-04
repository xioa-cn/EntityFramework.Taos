using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFramework.Taos.Metadata.Conventions;

// 关系型约定会构建 EF 查询翻译使用的运行时表/列映射。
// 没有这个 provider builder，运行时属性可能和列映射分离，根 LINQ 查询会失败。
public sealed class TaosConventionSetBuilder(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies)
    : RelationalConventionSetBuilder(dependencies, relationalDependencies)
{
    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();
        conventionSet.ModelFinalizingConventions.Add(new TaosAttributeConvention());

        return conventionSet;
    }
}
