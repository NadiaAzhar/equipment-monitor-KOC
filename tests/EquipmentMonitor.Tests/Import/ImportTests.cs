using EquipmentMonitor.Core;
using EquipmentMonitor.Core.Data;
using Microsoft.Data.Sqlite;

namespace EquipmentMonitor.Tests.Import;

public sealed class ImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("em-import-").FullName;
    private string Db => Path.Combine(_dir, "test.db");

    private const string Equipment = """
        equipment_id,equipment_type,description,site_id,site_name,criticality
        P-101,PUMP,Crude transfer pump,GC-01,Gathering Centre 01,A
        K-203,COMPRESSOR,Gas compressor,BS-01,Booster Station 01,A
        """;

    private const string Limits = """
        equipment_id,sensor,measures,unit,warning_high,alarm_high,rolling_avg_high
        P-101,VIBRATION,Casing vibration,mm/s,7.1,11.2,6.0
        K-203,PRESSURE,Discharge pressure,bar,70,75,68
        """;

    private ImportReport Load(string readings)
    {
        File.WriteAllText(Path.Combine(_dir, MonitorDatabase.EquipmentFile), Equipment);
        File.WriteAllText(Path.Combine(_dir, MonitorDatabase.LimitsFile), Limits);
        File.WriteAllText(Path.Combine(_dir, MonitorDatabase.ReadingsFile), "timestamp,equipment_id,sensor,value,unit,quality\n" + readings);
        return MonitorDatabase.Rebuild(Db, _dir, TestData.Options());
    }

    private long Scalar(string sql)
    {
        using var conn = MonitorDatabase.Open(Db);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void ExactDuplicate_IsRejectedWithReason_AndCountsReconcile()
    {
        var report = Load("""
            2026-10-14T00:00:00Z,P-101,VIBRATION,3.1,mm/s,GOOD
            2026-10-14T00:00:00Z,P-101,VIBRATION,3.1,mm/s,GOOD
            2026-10-14T00:01:00Z,P-101,VIBRATION,3.2,mm/s,GOOD
            """);
        var c = report.Files[MonitorDatabase.ReadingsFile];
        Assert.Equal((3, 2, 1), (c.Read, c.Loaded, c.Rejected));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM import_rejects WHERE reason = 'DUPLICATE' AND source_row = 2"));
    }

    [Fact]
    public void ConflictingDuplicate_KeepsFirst_RejectsSecond()
    {
        Load("""
            2026-10-14T00:00:00Z,P-101,VIBRATION,3.1,mm/s,GOOD
            2026-10-14T00:00:00Z,P-101,VIBRATION,9.9,mm/s,GOOD
            """);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM import_rejects WHERE reason = 'CONFLICTING_DUPLICATE'"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM alerts"));
    }

    [Fact]
    public void BadReading_IsKept_ButRaisesNoAlert()
    {
        var report = Load("2026-10-14T00:00:00Z,P-101,VIBRATION,999.9,mm/s,BAD\n");
        Assert.Equal(1, report.BadReadings);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM readings WHERE quality = 'BAD'"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM alerts"));
    }

    [Theory]
    [InlineData("2026-10-14T00:00:00Z,X-999,VIBRATION,3.1,mm/s,GOOD", "UNKNOWN_SENSOR")]
    [InlineData("2026-10-14T00:00:00Z,P-101,VIBRATION,abc,mm/s,GOOD", "INVALID_VALUE")]
    [InlineData("2026-10-14T00:00:00Z,P-101,VIBRATION,3.1,in/s,GOOD", "UNSUPPORTED_UNIT")]
    [InlineData("14/10/2026 00:00,P-101,VIBRATION,3.1,mm/s,GOOD", "INVALID_TIMESTAMP")]
    [InlineData("2026-10-14T00:00:00Z,P-101,VIBRATION,3.1,mm/s,MAYBE", "INVALID_QUALITY")]
    [InlineData("2026-10-14T00:00:00Z,P-101,VIBRATION,,mm/s,GOOD", "MISSING_FIELD")]
    public void InvalidRow_IsRejectedWithReason_NeverDroppedSilently(string row, string reason)
    {
        var report = Load(row + "\n");
        Assert.Equal(1, report.Files[MonitorDatabase.ReadingsFile].Rejected);
        Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM import_rejects WHERE reason = '{reason}'"));
    }

    [Fact]
    public void PsiReading_IsStoredAsReported_AlertCarriesConvertedValue()
    {
        Load("2026-10-14T00:00:00Z,K-203,PRESSURE,1100,psi,GOOD\n"); // 75.84 bar
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM readings WHERE unit = 'psi'"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM alerts WHERE rule_id = 'HIGH_ALARM' AND unit = 'bar' AND original_unit = 'psi'"));
    }

    [Fact]
    public void Rebuild_IsRepeatable()
    {
        const string rows = "2026-10-14T00:00:00Z,P-101,VIBRATION,8.0,mm/s,GOOD\n";
        Load(rows);
        Load(rows);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM readings"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM alerts"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }
}
