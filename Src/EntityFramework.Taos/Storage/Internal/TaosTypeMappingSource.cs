using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly BoolTypeMapping Bool = new("bool", DbType.Boolean);
    private static readonly ByteTypeMapping Byte = new("tinyint", DbType.Byte);
    private static readonly ShortTypeMapping Short = new("smallint", DbType.Int16);
    private static readonly IntTypeMapping Int = new("int", DbType.Int32);
    private static readonly LongTypeMapping Long = new("bigint", DbType.Int64);
    private static readonly FloatTypeMapping Float = new("float", DbType.Single);
    private static readonly DoubleTypeMapping Double = new("double", DbType.Double);
    private static readonly DecimalTypeMapping Decimal = new("double", DbType.Decimal);
    private static readonly DateTimeTypeMapping DateTime = new("timestamp", DbType.DateTime);
    private static readonly StringTypeMapping String = new("nchar(255)", DbType.String);
    private static readonly ByteArrayTypeMapping Bytes = new("varbinary(1024)", DbType.Binary);

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrMappings = new()
    {
        [typeof(bool)] = Bool,
        [typeof(byte)] = Byte,
        [typeof(short)] = Short,
        [typeof(int)] = Int,
        [typeof(long)] = Long,
        [typeof(float)] = Float,
        [typeof(double)] = Double,
        [typeof(decimal)] = Decimal,
        [typeof(DateTime)] = DateTime,
        [typeof(string)] = String,
        [typeof(byte[])] = Bytes
    };

    private static readonly Dictionary<string, RelationalTypeMapping> StoreMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["bool"] = Bool,
            ["tinyint"] = Byte,
            ["smallint"] = Short,
            ["int"] = Int,
            ["bigint"] = Long,
            ["float"] = Float,
            ["double"] = Double,
            ["timestamp"] = DateTime,
            ["nchar"] = String,
            ["varchar"] = String,
            ["binary"] = Bytes,
            ["varbinary"] = Bytes
        };

    public TaosTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var storeTypeName = mappingInfo.StoreTypeName;
        if (!string.IsNullOrWhiteSpace(storeTypeName)
            && StoreMappings.TryGetValue(UnwrapStoreType(storeTypeName), out var storeMapping))
        {
            // 优先尊重显式 HasColumnType，包括 nchar(64) 这类带长度的形式。
            return storeMapping;
        }

        var clrType = mappingInfo.ClrType;
        if (clrType is not null)
        {
            clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (ClrMappings.TryGetValue(clrType, out var clrMapping))
            {
                return clrMapping;
            }
        }

        return base.FindMapping(mappingInfo);
    }

    private static string UnwrapStoreType(string storeType)
    {
        // EF 传入完整 store type 名称；TDengine 映射按基础类型作为键。
        var parenIndex = storeType.IndexOf('(');
        return (parenIndex < 0 ? storeType : storeType[..parenIndex]).Trim();
    }
}
