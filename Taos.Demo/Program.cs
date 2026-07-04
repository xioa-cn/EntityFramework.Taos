using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EntityFramework.Taos.Extensions;
using EntityFramework.Taos.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var connectionString = Environment.GetEnvironmentVariable("TAOS_CONNECTION")
                       ??
                       "host=localhost;port=6041;username=root;password=taosdata;db=efcore_taos_demo;protocol=WebSocket";

Console.WriteLine($"TDengine connection: {connectionString}");

await using var db = new SensorDbContext(connectionString);

//await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

Console.WriteLine();
Console.WriteLine("建表脚本对比：");
Console.WriteLine(db.Database.GenerateCreateScript());

var now = DateTime.UtcNow;

db.Readings.AddRange(
    new SensorReading
    {
        Ts = now.AddSeconds(-20),
        DeviceId = "meter-001",
        Location = "workshop-a",
        Temperature = 22.8,
        Humidity = 48.2
    },
    new SensorReading
    {
        Ts = now.AddSeconds(-10),
        DeviceId = "meter-001",
        Location = "workshop-a",
        Temperature = 23.1,
        Humidity = 47.9
    },
    new SensorReading
    {
        Ts = now,
        DeviceId = "meter-002",
        Location = "workshop-b",
        Temperature = 21.9,
        Humidity = 52.4
    });

db.Logs.AddRange(
    new DeviceLog
    {
        Ts = now.AddSeconds(-15),
        DeviceId = "meter-001",
        Level = "info",
        Message = "普通表直接写入完整设备日志"
    },
    new DeviceLog
    {
        Ts = now.AddSeconds(-5),
        DeviceId = "meter-002",
        Level = "warn",
        Message = "普通表没有 TAGS，也不会创建子表"
    });

await db.SaveChangesAsync();

var stableRows = await db.Readings
    .AsNoTracking()
    .OrderBy(x => x.DeviceId)
    .ThenBy(x => x.Ts)
    .ToListAsync();

var tableRows = await db.Logs
    .AsNoTracking()
    .OrderBy(x => x.Ts)
    .ToListAsync();

Console.WriteLine("超级表 sensor_readings：DeviceId/Location 是 TAGS，写入时会走 USING ... TAGS ... 并按标签落到子表。");
foreach (var item in stableRows)
{
    Console.WriteLine(
        $"{item.Ts:O} {item.DeviceId} {item.Location} temp={item.Temperature:F1} humidity={item.Humidity:F1}");
}

Console.WriteLine();
Console.WriteLine("普通表 device_logs：所有属性都是普通列，写入时就是 INSERT INTO device_logs。");
foreach (var item in tableRows)
{
    Console.WriteLine($"{item.Ts:O} {item.DeviceId} {item.Level} {item.Message}");
}

Console.WriteLine();
Console.WriteLine("对比结论：超级表把设备标识、位置建成 TAGS，适合按设备维度分子表；普通表只有固定列结构，不包含 TAGS/子表路由。");

public sealed class SensorDbContext(string connectionString) : DbContext
{
    public DbSet<SensorReading> Readings => Set<SensorReading>();
    public DbSet<DeviceLog> Logs => Set<DeviceLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseTaos(connectionString)
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging();
    }

    // protected override void OnModelCreating(ModelBuilder modelBuilder)
    // {
    //     modelBuilder.Entity<SensorReading>(builder =>
    //     {
    //         builder.HasKey(x => x.Ts);
    //
    //         builder.Property(x => x.Ts)
    //             .ValueGeneratedNever()
    //             .HasColumnName("ts");
    //
    //         builder.Property(x => x.DeviceId)
    //             .HasColumnName("device_id")
    //             .HasColumnType("nchar(64)");
    //
    //         builder.Property(x => x.Location)
    //             .HasColumnName("location")
    //             .HasColumnType("nchar(64)");
    //
    //         builder.Property(x => x.Temperature)
    //             .HasColumnName("temperature")
    //             .HasColumnType("double");
    //
    //         builder.Property(x => x.Humidity)
    //             .HasColumnName("humidity")
    //             .HasColumnType("double");
    //     });
    //
    //     modelBuilder.Entity<DeviceLog>(builder =>
    //     {
    //         builder.ToTable("device_logs");
    //         builder.HasKey(x => x.Ts);
    //
    //         builder.Property(x => x.Ts)
    //             .ValueGeneratedNever()
    //             .HasColumnName("ts");
    //
    //         builder.Property(x => x.DeviceId)
    //             .HasColumnName("device_id")
    //             .HasColumnType("nchar(64)");
    //
    //         builder.Property(x => x.Level)
    //             .HasColumnName("level")
    //             .HasColumnType("nchar(16)");
    //
    //         builder.Property(x => x.Message)
    //             .HasColumnName("message")
    //             .HasColumnType("nchar(255)");
    //     });
    // }
}

[TaosStable("sensor_readings")]
public sealed class SensorReading
{
    [TaosTimestamp] [Key] public DateTime Ts { get; set; }
    [TaosTag] public string DeviceId { get; set; } = string.Empty;
    [TaosTag] public string Location { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Humidity { get; set; }
}
[Table("device_logs")]
public sealed class DeviceLog
{
    [TaosTimestamp] [Key] public DateTime Ts { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}