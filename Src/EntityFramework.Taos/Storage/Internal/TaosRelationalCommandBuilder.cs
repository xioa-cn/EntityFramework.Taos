using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalCommandBuilder : RelationalCommandBuilder
{
    public TaosRelationalCommandBuilder(RelationalCommandBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IRelationalCommand Build()
    {
        var commandText = ToString();

#if NET10_0_OR_GREATER
        return new TaosRelationalCommand(Dependencies, commandText, commandText, Parameters.ToList());
#else
        return new TaosRelationalCommand(Dependencies, commandText, Parameters.ToList());
#endif
    }
}
