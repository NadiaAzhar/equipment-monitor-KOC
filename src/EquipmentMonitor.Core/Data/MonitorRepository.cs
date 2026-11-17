using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EquipmentMonitor.Core.Data;

public sealed record SiteSummary(string SiteId, string SiteName, int EquipmentCount);

public sealed record EquipmentSummary(
    string EquipmentId, string EquipmentType, string Description, string SiteId, string SiteName, string Criticality,
    string Status, IReadOnlyDictionary<string, int> AlertCounts, int BadReadingCount, DateTimeOffset? LastAlertAt);

public sealed record SensorDetail(
    string Sensor, string Measures, string Unit, double WarningHigh, double AlarmHigh, double RollingAvgHigh,
    IReadOnlyDictionary<string, int> AlertCounts, int BadReadingCount);

public sealed record AlertRow(
    long AlertId, string RuleId, string Severity, string EquipmentId, string SiteId, string Sensor, DateTimeOffset Timestamp,
    double Value, string Unit, double Limit, double OriginalValue, string OriginalUnit, int? WindowGoodCount, string Message);

public sealed record AlertQuery(string? SiteId = null, string? EquipmentId = null, string? RuleId = null, string? Sensor = null,
    int Limit = 50, int Offset = 0);

/// <summary>One time bucket of a sensor's readings. Value, Min and Max come from GOOD readings only, in the sensor's limit unit.</summary>
public sealed record ReadingPoint(
    DateTimeOffset Timestamp, string Sensor, string Unit, double? Value, double? Min, double? Max, int GoodCount, int BadCount);

public sealed record ReadingQuery(string EquipmentId, string? Sensor = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    int BucketMinutes = 1, int Limit = 1500, int Offset = 0);

/// <summary>Alerts per equipment per local hour, split by severity. Hours with no alerts are omitted.</summary>
public sealed record AlertHourCount(string EquipmentId, string SiteId, DateTimeOffset Hour, int AlarmCount, int WarningCount);

/// <summary>Read-only queries over the database. Times are returned in the configured local offset.</summary>
public sealed class MonitorRepository(string databasePath, TimeSpan localOffset, UnitConverter? converter = null)
{
    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        return conn;
    }

    private DateTimeOffset Local(string utc) =>
        DateTimeOffset.ParseExact(utc, MonitorDatabase.TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToOffset(localOffset);

    private static List<T> Query<T>(SqliteConnection conn, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public IReadOnlyList<SiteSummary> Sites()
    {
        using var conn = Open();
        return Query(conn, "SELECT site_id, site_name, COUNT(*) FROM equipment GROUP BY site_id, site_name ORDER BY site_id",
            r => new SiteSummary(r.GetString(0), r.GetString(1), r.GetInt32(2)));
    }

    public bool SiteExists(string siteId) => Sites().Any(s => s.SiteId == siteId);

    public IReadOnlyList<string> Sensors()
    {
        using var conn = Open();
        return Query(conn, "SELECT DISTINCT sensor FROM sensor_limits ORDER BY sensor", r => r.GetString(0));
    }

    public IReadOnlyList<EquipmentSummary> Equipment(string? siteId = null)
    {
        using var conn = Open();
        var counts = Query(conn, "SELECT equipment_id, rule_id, COUNT(*), MAX(timestamp) FROM alerts GROUP BY equipment_id, rule_id",
            r => (Id: r.GetString(0), Rule: r.GetString(1), Count: r.GetInt32(2), Last: r.GetString(3)));
        var bad = Query(conn, "SELECT equipment_id, COUNT(*) FROM readings WHERE quality = 'BAD' GROUP BY equipment_id",
            r => (Id: r.GetString(0), Count: r.GetInt32(1))).ToDictionary(x => x.Id, x => x.Count);

        var rows = Query(conn,
            "SELECT equipment_id, equipment_type, description, site_id, site_name, criticality FROM equipment WHERE $site IS NULL OR site_id = $site",
            r => (Id: r.GetString(0), Type: r.GetString(1), Desc: r.GetString(2), Site: r.GetString(3), SiteName: r.GetString(4), Crit: r.GetString(5)),
            ("$site", siteId));

        return rows.Select(e =>
            {
                var mine = counts.Where(c => c.Id == e.Id).ToList();
                var byRule = RuleIds.All.ToDictionary(id => id, id => mine.Where(c => c.Rule == id).Sum(c => c.Count));
                var last = mine.Count == 0 ? (DateTimeOffset?)null : Local(mine.Max(c => c.Last)!);
                return new EquipmentSummary(e.Id, e.Type, e.Desc, e.Site, e.SiteName, e.Crit,
                    EquipmentStatus.FromRuleIds(mine.Select(c => c.Rule)), byRule, bad.GetValueOrDefault(e.Id), last);
            })
            .OrderBy(e => EquipmentStatus.Rank(e.Status)).ThenBy(e => e.Criticality).ThenBy(e => e.EquipmentId)
            .ToList();
    }

    public EquipmentSummary? EquipmentById(string equipmentId) =>
        Equipment().FirstOrDefault(e => e.EquipmentId == equipmentId);

    public IReadOnlyList<SensorDetail> SensorsFor(string equipmentId)
    {
        using var conn = Open();
        var counts = Query(conn, "SELECT sensor, rule_id, COUNT(*) FROM alerts WHERE equipment_id = $id GROUP BY sensor, rule_id",
            r => (Sensor: r.GetString(0), Rule: r.GetString(1), Count: r.GetInt32(2)), ("$id", equipmentId));
        var bad = Query(conn, "SELECT sensor, COUNT(*) FROM readings WHERE equipment_id = $id AND quality = 'BAD' GROUP BY sensor",
            r => (Sensor: r.GetString(0), Count: r.GetInt32(1)), ("$id", equipmentId)).ToDictionary(x => x.Sensor, x => x.Count);
        return Query(conn,
            "SELECT sensor, measures, unit, warning_high, alarm_high, rolling_avg_high FROM sensor_limits WHERE equipment_id = $id ORDER BY sensor",
            r => (Sensor: r.GetString(0), Measures: r.GetString(1), Unit: r.GetString(2), W: r.GetDouble(3), A: r.GetDouble(4), R: r.GetDouble(5)),
            ("$id", equipmentId))
            .Select(s => new SensorDetail(s.Sensor, s.Measures, s.Unit, s.W, s.A, s.R,
                RuleIds.All.ToDictionary(id => id, id => counts.Where(c => c.Sensor == s.Sensor && c.Rule == id).Sum(c => c.Count)),
                bad.GetValueOrDefault(s.Sensor)))
            .ToList();
    }

    public (IReadOnlyList<ReadingPoint> Items, int Total) Readings(ReadingQuery q)
    {
        using var conn = Open();
        var units = Query(conn, "SELECT sensor, unit FROM sensor_limits WHERE equipment_id = $id AND ($sensor IS NULL OR sensor = $sensor)",
            r => (Sensor: r.GetString(0), Unit: r.GetString(1)), ("$id", q.EquipmentId), ("$sensor", q.Sensor)).ToDictionary(x => x.Sensor, x => x.Unit);
        var format = (DateTimeOffset? t) => t?.ToUniversalTime().ToString(MonitorDatabase.TimestampFormat, CultureInfo.InvariantCulture);
        var rows = Query(conn,
            "SELECT sensor, timestamp, value, unit, quality FROM readings WHERE equipment_id = $id AND ($sensor IS NULL OR sensor = $sensor) " +
            "AND ($from IS NULL OR timestamp >= $from) AND ($to IS NULL OR timestamp < $to) ORDER BY sensor, timestamp",
            r => (Sensor: r.GetString(0), At: Local(r.GetString(1)), Value: r.GetDouble(2), Unit: r.GetString(3), Good: r.GetString(4) == "GOOD"),
            ("$id", q.EquipmentId), ("$sensor", q.Sensor), ("$from", format(q.From)), ("$to", format(q.To)));

        var bucket = TimeSpan.FromMinutes(q.BucketMinutes);
        var points = rows
            .GroupBy(r => (r.Sensor, Start: r.At.UtcTicks - r.At.UtcTicks % bucket.Ticks))
            .OrderBy(g => g.Key.Sensor, StringComparer.Ordinal).ThenBy(g => g.Key.Start)
            .Select(g =>
            {
                var unit = units[g.Key.Sensor];
                var good = g.Where(r => r.Good)
                    .Select(r => converter is not null && converter.TryConvert(r.Value, r.Unit, unit, out var v) ? v : r.Value).ToList();
                return new ReadingPoint(new DateTimeOffset(g.Key.Start, TimeSpan.Zero).ToOffset(localOffset), g.Key.Sensor, unit,
                    good.Count > 0 ? good.Average() : null, good.Count > 0 ? good.Min() : null, good.Count > 0 ? good.Max() : null,
                    good.Count, g.Count() - good.Count);
            })
            .ToList();
        return (points.Skip(q.Offset).Take(q.Limit).ToList(), points.Count);
    }

    public IReadOnlyList<AlertHourCount> AlertsByHour(string? siteId = null)
    {
        using var conn = Open();
        return Query(conn,
            "SELECT a.equipment_id, e.site_id, substr(a.timestamp, 1, 13) AS hour, " +
            "SUM(a.severity = 'ALARM'), SUM(a.severity <> 'ALARM') " +
            "FROM alerts a JOIN equipment e ON e.equipment_id = a.equipment_id WHERE $site IS NULL OR e.site_id = $site " +
            "GROUP BY a.equipment_id, e.site_id, hour ORDER BY hour, a.equipment_id",
            r => new AlertHourCount(r.GetString(0), r.GetString(1), Local(r.GetString(2) + ":00:00Z"), r.GetInt32(3), r.GetInt32(4)),
            ("$site", siteId));
    }

    public (IReadOnlyList<AlertRow> Items, int Total) Alerts(AlertQuery q)
    {
        using var conn = Open();
        const string where = """
            FROM alerts a JOIN equipment e ON e.equipment_id = a.equipment_id
            WHERE ($site IS NULL OR e.site_id = $site) AND ($eq IS NULL OR a.equipment_id = $eq)
              AND ($rule IS NULL OR a.rule_id = $rule) AND ($sensor IS NULL OR a.sensor = $sensor)
            """;
        (string, object?)[] args = [("$site", q.SiteId), ("$eq", q.EquipmentId), ("$rule", q.RuleId), ("$sensor", q.Sensor)];
        var total = Query(conn, "SELECT COUNT(*) " + where, r => r.GetInt32(0), args)[0];
        var items = Query(conn,
            "SELECT a.id, a.rule_id, a.severity, a.equipment_id, e.site_id, a.sensor, a.timestamp, a.value, a.unit, a.limit_value, " +
            "a.original_value, a.original_unit, a.window_good_count, a.message " + where +
            " ORDER BY a.timestamp DESC, CASE a.severity WHEN 'ALARM' THEN 0 ELSE 1 END, a.equipment_id, a.sensor, a.rule_id LIMIT $limit OFFSET $offset",
            r => new AlertRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), Local(r.GetString(6)),
                r.GetDouble(7), r.GetString(8), r.GetDouble(9), r.GetDouble(10), r.GetString(11), r.IsDBNull(12) ? null : r.GetInt32(12), r.GetString(13)),
            [.. args, ("$limit", q.Limit), ("$offset", q.Offset)]);
        return (items, total);
    }
}
