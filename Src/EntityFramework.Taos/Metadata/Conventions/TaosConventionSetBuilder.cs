using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace EntityFramework.Taos.Metadata.Conventions;

// Relational conventions build the runtime table/column mappings used by EF query translation.
// Without this provider builder, runtime properties can diverge from column mappings and root LINQ queries fail.
public sealed class TaosConventionSetBuilder(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies)
    : RelationalConventionSetBuilder(dependencies, relationalDependencies);
