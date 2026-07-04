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

await db.Database.EnsureDeletedAsync();
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
        Status = SensorStatus.Online,
        Temperature = 22.8,
        Humidity = 48.2
    },
    new SensorReading
    {
        Ts = now.AddSeconds(-10),
        DeviceId = "meter-001",
        Location = "workshop-a",
        Status = SensorStatus.Online,
        Temperature = 23.1,
        Humidity = 47.9
    },
    new SensorReading
    {
        Ts = now,
        DeviceId = "meter-002",
        Location = "workshop-b",
        Status = SensorStatus.Offline,
        Temperature = 21.9,
        Humidity = 52.4
    });

db.Logs.AddRange(
    new DeviceLog
    {
        Ts = now.AddSeconds(-15),
        DeviceId = "meter-001",
        Level = DeviceLogLevel.Info,
        Message = "普通表直接写入完整设备日志"
    },
    new DeviceLog
    {
        Ts = now.AddSeconds(-5),
        DeviceId = "meter-002",
        Level = DeviceLogLevel.Warn,
        Message = "普通表没有 TAGS，也不会创建子表"
    });

await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var startTime = now.AddSeconds(-30);
var endTime = now.AddSeconds(1);
var devicePrefix = "meter-";
var locationKeyword = "workshop";
var requiredStatus = SensorStatus.Online;
var stableQuery = db.Readings
    .AsNoTracking()
    .Where(x => x.Ts > startTime
                && x.Ts <= endTime
                && x.DeviceId.StartsWith(devicePrefix)
                && x.Location.Contains(locationKeyword)
                && x.Status == requiredStatus)
    .OrderBy(x => x.DeviceId)
    .ThenBy(x => x.Ts)
    .AsQueryable();

Console.WriteLine();
Console.WriteLine("查询 SQL 对比：时间范围 + StartsWith + Contains + enum 条件");
Console.WriteLine(stableQuery.ToQueryString());

var stableRows = await stableQuery.ToListAsync();

var tableRows = await db.Logs
    .AsNoTracking()
    .OrderBy(x => x.Ts)
    .ToListAsync();

await RunCommonQueryExamples(db, now, startTime, endTime);
await RunAdvancedQueryExamples(db, now, startTime, endTime);

Console.WriteLine("超级表 sensor_readings：DeviceId/Location 是 TAGS，写入时会走 USING ... TAGS ... 并按标签落到子表。");
foreach (var item in stableRows)
{
    Console.WriteLine(
        $"{item.Ts:O} {item.DeviceId} {item.Location} status={item.Status} temp={item.Temperature:F1} humidity={item.Humidity:F1}");
}

Console.WriteLine();
Console.WriteLine("普通表 device_logs：所有属性都是普通列，写入时就是 INSERT INTO device_logs。");
foreach (var item in tableRows)
{
    Console.WriteLine($"{item.Ts:O} {item.DeviceId} {item.Level} {item.Message}");
}

Console.WriteLine();
Console.WriteLine("对比结论：超级表把设备标识、位置建成 TAGS，适合按设备维度分子表；普通表只有固定列结构，不包含 TAGS/子表路由。");

static async Task RunCommonQueryExamples(
    SensorDbContext db,
    DateTime now,
    DateTime startTime,
    DateTime endTime)
{
    Console.WriteLine();
    Console.WriteLine("常用查询示例：Count / OrderBy / GroupBy / Select / FindAsync / FirstAsync / SingleAsync / SingleOrDefaultAsync");

    var totalCount = await db.Readings
        .AsNoTracking()
        .CountAsync();
    Console.WriteLine($"CountAsync：超级表总行数 = {totalCount}");

    var rangeCount = await db.Readings
        .AsNoTracking()
        .CountAsync(x => x.Ts >= startTime && x.Ts <= endTime);
    Console.WriteLine($"CountAsync(predicate)：时间范围内行数 = {rangeCount}");

    var latestReading = await db.Readings
        .AsNoTracking()
        .OrderByDescending(x => x.Ts)
        .FirstAsync();
    Console.WriteLine($"OrderByDescending + FirstAsync：最新读数 = {latestReading.DeviceId} {latestReading.Ts:O}");

    var projections = await db.Readings
        .AsNoTracking()
        .OrderBy(x => x.DeviceId)
        .ThenBy(x => x.Ts)
        .Select(x => new
        {
            x.DeviceId,
            x.Location,
            x.Status,
            TemperatureText = x.Temperature
        })
        .ToListAsync();
    Console.WriteLine($"Select：投影行数 = {projections.Count}");

    var groupedRows = await db.Readings
        .AsNoTracking()
        .GroupBy(x => x.DeviceId)
        .Select(g => new
        {
            DeviceId = g.Key,
            Count = g.Count(),
            AvgTemperature = g.Average(x => x.Temperature)
        })
        .OrderBy(x => x.DeviceId)
        .ToListAsync();
    foreach (var item in groupedRows)
    {
        Console.WriteLine($"GroupBy：{item.DeviceId} count={item.Count} avgTemp={item.AvgTemperature:F1}");
    }

    var logKey = now.AddSeconds(-15);
    var foundLog = await db.Logs.FindAsync(logKey);
    Console.WriteLine($"FindAsync：{foundLog?.DeviceId} {foundLog?.Level}");
    db.Entry(foundLog!).State = EntityState.Detached;

    var firstWarnLog = await db.Logs
        .AsNoTracking()
        .Where(x => x.Level == DeviceLogLevel.Warn)
        .FirstAsync();
    Console.WriteLine($"FirstAsync：第一条 Warn 日志 = {firstWarnLog.DeviceId} {firstWarnLog.Ts:O}");

    var singleLog = await db.Logs
        .AsNoTracking()
        .SingleAsync(x => x.Ts == logKey);
    Console.WriteLine($"SingleAsync：唯一日志 = {singleLog.DeviceId} {singleLog.Message}");

    var missingLogTime = now.AddMinutes(-10);
    var missingLog = await db.Logs
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.Ts == missingLogTime);
    Console.WriteLine($"SingleOrDefaultAsync：不存在日志 = {(missingLog is null ? "null" : missingLog.DeviceId)}");

    var firstOrDefaultReading = await db.Readings
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.DeviceId == "not-exists");
    Console.WriteLine($"FirstOrDefaultAsync：不存在设备 = {(firstOrDefaultReading is null ? "null" : firstOrDefaultReading.DeviceId)}");
}

static async Task RunAdvancedQueryExamples(
    SensorDbContext db,
    DateTime now,
    DateTime startTime,
    DateTime endTime)
{
    Console.WriteLine();
    Console.WriteLine("进阶查询示例：IN / Take / Skip / Any / 复杂条件 / 非空判断 / IsNullOrEmpty / 聚合 / DTO / FromSqlRaw / 动态 Where");

    var deviceIds = new[] { "meter-001", "meter-003" };
    var inRows = await db.Readings
        .AsNoTracking()
        .Where(x => deviceIds.Contains(x.DeviceId))
        .OrderBy(x => x.DeviceId)
        .ThenBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"IN 数组 Contains：命中行数 = {inRows.Count}");

    var pagedRows = await db.Readings
        .AsNoTracking()
        .OrderBy(x => x.Ts)
        .Skip(1)
        .Take(1)
        .ToListAsync();
    Console.WriteLine($"Skip + Take 分页：页内行数 = {pagedRows.Count}");

    var skippedRows = await db.Readings
        .AsNoTracking()
        .OrderBy(x => x.Ts)
        .Skip(1)
        .ToListAsync();
    Console.WriteLine($"Skip 跳过：剩余行数 = {skippedRows.Count}");

    var hasAnyReading = await db.Readings
        .AsNoTracking()
        .AnyAsync();
    var hasOfflineReading = await db.Readings
        .AsNoTracking()
        .AnyAsync(x => x.Status == SensorStatus.Offline);
    Console.WriteLine($"AnyAsync：存在数据 = {hasAnyReading}，存在离线设备 = {hasOfflineReading}");

    var rangeRows = await db.Readings
        .AsNoTracking()
        .Where(x => x.Ts >= startTime
                    && x.Ts <= endTime
                    && x.Temperature >= 22
                    && x.Temperature <= 24
                    && x.Humidity >= 47
                    && x.Humidity <= 50)
        .OrderBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"数值区间多条件：命中行数 = {rangeRows.Count}");

    var complexRows = await db.Readings
        .AsNoTracking()
        .Where(x => (x.DeviceId == "meter-001" && x.Temperature > 22)
                    || (x.DeviceId == "meter-002" && x.Status == SensorStatus.Offline))
        .OrderBy(x => x.DeviceId)
        .ThenBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"&& || 组合复杂条件：命中行数 = {complexRows.Count}");

    var nonEmptyLogs = await db.Logs
        .AsNoTracking()
        .Where(x => x.Message != null && x.Message != string.Empty)
        .OrderBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"字段非空判断：日志行数 = {nonEmptyLogs.Count}");

    var hasEmptyMessage = await db.Logs
        .AsNoTracking()
        .AnyAsync(x => string.IsNullOrEmpty(x.Message));
    Console.WriteLine($"string.IsNullOrEmpty：存在空消息 = {hasEmptyMessage}");

    var aggregateRows = await db.Readings
        .AsNoTracking()
        .GroupBy(x => x.DeviceId)
        .Select(g => new DeviceAggregateDto
        {
            DeviceId = g.Key,
            Count = g.Count(),
            MaxTemperature = g.Max(x => x.Temperature),
            MinTemperature = g.Min(x => x.Temperature),
            TotalHumidity = g.Sum(x => x.Humidity)
        })
        .OrderBy(x => x.DeviceId)
        .ToListAsync();
    foreach (var item in aggregateRows)
    {
        Console.WriteLine(
            $"聚合搭配分组：{item.DeviceId} count={item.Count} max={item.MaxTemperature:F1} min={item.MinTemperature:F1} sumHumidity={item.TotalHumidity:F1}");
    }

    var dtoRows = await db.Readings
        .AsNoTracking()
        .Where(x => x.Ts >= startTime && x.Ts <= endTime)
        .OrderBy(x => x.Ts)
        .Select(x => new SensorReadingDto
        {
            Time = x.Ts,
            DeviceId = x.DeviceId,
            Location = x.Location,
            Status = x.Status,
            Temperature = x.Temperature
        })
        .ToListAsync();
    Console.WriteLine($"Select 映射正式 DTO：行数 = {dtoRows.Count}");

    var rawRows = await db.Readings
        .FromSqlRaw("SELECT `Ts`, `DeviceId`, `Humidity`, `Location`, `Status`, `Temperature` FROM `sensor_readings`")
        .AsNoTracking()
        .Where(x => x.DeviceId == "meter-001")
        .OrderBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"FromSqlRaw + 链式 Where：行数 = {rawRows.Count}");

    IQueryable<SensorReading> dynamicQuery = db.Readings.AsNoTracking();
    var filterByDevice = true;
    var filterByStatus = true;
    var filterByTemperature = true;
    if (filterByDevice)
    {
        dynamicQuery = dynamicQuery.Where(x => x.DeviceId == "meter-001");
    }

    if (filterByStatus)
    {
        dynamicQuery = dynamicQuery.Where(x => x.Status == SensorStatus.Online);
    }

    if (filterByTemperature)
    {
        dynamicQuery = dynamicQuery.Where(x => x.Temperature >= 22);
    }

    var dynamicRows = await dynamicQuery
        .OrderBy(x => x.Ts)
        .ToListAsync();
    Console.WriteLine($"链式 Where 多条件动态拼接：命中行数 = {dynamicRows.Count}");
}

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
    [MaxLength(64)]
    [TaosTag] public string DeviceId { get; set; } = string.Empty;
    [MaxLength(64)]
    [TaosTag] public string Location { get; set; } = string.Empty;
    public SensorStatus Status { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
}

[Table("device_logs")]
public sealed class DeviceLog
{
    [TaosTimestamp] [Key] public DateTime Ts { get; set; }
    [MaxLength(64)]
    public string DeviceId { get; set; } = string.Empty;
    public DeviceLogLevel Level { get; set; }
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;
}

public enum SensorStatus
{
    Offline = 0,
    Online = 1
}

public enum DeviceLogLevel
{
    Info = 1,
    Warn = 2
}

public sealed class SensorReadingDto
{
    public DateTime Time { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorStatus Status { get; set; }
    public double Temperature { get; set; }
}

public sealed class DeviceAggregateDto
{
    public string DeviceId { get; set; } = string.Empty;
    public int Count { get; set; }
    public double MaxTemperature { get; set; }
    public double MinTemperature { get; set; }
    public double TotalHumidity { get; set; }
}
