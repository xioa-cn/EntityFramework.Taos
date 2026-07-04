using EntityFramework.Taos.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var connectionString = Environment.GetEnvironmentVariable("TAOS_CONNECTION")
                       ?? "host=localhost;port=6041;username=root;password=taosdata;db=efcore_taos_demo;protocol=WebSocket";

Console.WriteLine($"TDengine connection: {connectionString}");

await using var db = new SensorDbContext(connectionString);

await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

db.Readings.AddRange(
    new SensorReading
    {
        Ts = DateTime.UtcNow.AddSeconds(-20),
        DeviceId = "meter-001",
        Location = "workshop-a",
        Temperature = 22.8,
        Humidity = 48.2
    },
    new SensorReading
    {
        Ts = DateTime.UtcNow.AddSeconds(-10),
        DeviceId = "meter-001",
        Location = "workshop-a",
        Temperature = 23.1,
        Humidity = 47.9
    },
    new SensorReading
    {
        Ts = DateTime.UtcNow,
        DeviceId = "meter-002",
        Location = "workshop-b",
        Temperature = 21.9,
        Humidity = 52.4
    });

await db.SaveChangesAsync();

var latest = await db.Readings
    .AsNoTracking()
    .Where(x => x.DeviceId == "meter-001")
    .OrderByDescending(x => x.Ts)
    .Take(1)
    .ToListAsync();

Console.WriteLine("Latest readings for meter-001:");
foreach (var item in latest)
{
    Console.WriteLine($"{item.Ts:O} {item.DeviceId} {item.Location} temp={item.Temperature:F1} humidity={item.Humidity:F1}");
}

public sealed class SensorDbContext(string connectionString) : DbContext
{
    public DbSet<SensorReading> Readings => Set<SensorReading>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseTaos(connectionString)
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensorReading>(builder =>
        {
            builder.ToStable("sensor_readings");
            builder.HasKey(x => x.Ts);

            builder.Property(x => x.Ts)
                .IsTaosTimestamp()
                .ValueGeneratedNever()
                .HasColumnName("ts");

            builder.Property(x => x.DeviceId)
                .IsTaosTag()
                .HasColumnName("device_id")
                .HasColumnType("nchar(64)");

            builder.Property(x => x.Location)
                .IsTaosTag()
                .HasColumnName("location")
                .HasColumnType("nchar(64)");

            builder.Property(x => x.Temperature)
                .HasColumnName("temperature")
                .HasColumnType("double");

            builder.Property(x => x.Humidity)
                .HasColumnName("humidity")
                .HasColumnType("double");
        });
    }
}

public sealed class SensorReading
{
    public DateTime Ts { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Humidity { get; set; }
}
