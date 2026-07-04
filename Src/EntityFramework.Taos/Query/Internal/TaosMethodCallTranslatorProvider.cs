using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Taos.Query.Internal;

public sealed class TaosMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    public TaosMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        AddTranslators([
            new TaosStringMethodTranslator(dependencies.SqlExpressionFactory)
        ]);
    }
}
