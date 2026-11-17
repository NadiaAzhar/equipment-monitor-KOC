using EquipmentMonitor.Core;
using EquipmentMonitor.Core.Data;

namespace EquipmentMonitor.Tests.Import;

/// <summary>Golden check on the hackathon data set: catches accidental changes to import or rule behaviour.</summary>
public sealed class RealDataTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("em-real-").FullName;

    public static string DataDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", MonitorDatabase.ReadingsFile))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("data/ not found"), "data");
        }
    }

    [Fact]
    public void RealData_ImportCountsAndStatuses_AreStable()
    {
        var db = Path.Combine(_dir, "real.db");
        var report = MonitorDatabase.Rebuild(db, DataDirectory, TestData.Options());

        var readings = report.Files[MonitorDatabase.ReadingsFile];
        Assert.Equal(51866, readings.Read);
        Assert.Equal(120, report.RejectReasons[RejectReasons.Duplicate]);
        Assert.Equal(51746, readings.Loaded);
        Assert.Equal(17, report.BadReadings);
        Assert.Equal(456, report.Alerts);

        var status = new MonitorRepository(db, TimeSpan.FromHours(3)).Equipment().ToDictionary(e => e.EquipmentId, e => e.Status);
        Assert.Equal("ALARM", status["P-103"]);
        Assert.Equal("ALARM", status["K-202"]);
        Assert.Equal("WARNING", status["K-203"]);
        Assert.Equal("NORMAL", status["P-102"]);
        Assert.Equal(2, status.Values.Count(s => s == "ALARM"));
        Assert.Equal(4, status.Values.Count(s => s == "WARNING"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }
}
