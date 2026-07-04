using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosSqlGenerationHelper : RelationalSqlGenerationHelper
{
    public TaosSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
        : base(dependencies)
    {
    }

    public override string DelimitIdentifier(string identifier)
        => $"`{EscapeIdentifier(identifier)}`";

    public override void DelimitIdentifier(StringBuilder builder, string identifier)
        => builder.Append('`').Append(EscapeIdentifier(identifier)).Append('`');

    public override string EscapeIdentifier(string identifier)
        => identifier.Replace("`", "``", StringComparison.Ordinal);
}
