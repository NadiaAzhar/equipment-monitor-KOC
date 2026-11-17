using System.Globalization;
using EquipmentMonitor.Core.Rules;
using Microsoft.Data.Sqlite;

namespace EquipmentMonitor.Core.Data;

/// <summary>Rebuilds the SQLite database from the CSV files, then evaluates the rules and stores the alerts. Safe to run again.</summary>
public static class MonitorDatabase
{
    public const string EquipmentFile = "equipment.csv";
    public const string LimitsFile = "sensor_limits.csv";
    public const string ReadingsFile = "sensor_readings.csv";
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private const string Schema = """
        DROP TABLE IF EXISTS alerts;
        DROP TABLE IF EXISTS readings;
        DROP TABLE IF EXISTS sensor_limits;
        DROP TABLE IF EXISTS equipment;
        DROP TABLE IF EXISTS import_rejects;
        CREATE TABLE equipment (
            equipment_id TEXT PRIMARY KEY, equipment_type TEXT NOT NULL, description TEXT NOT NULL,
            site_id TEXT NOT NULL, site_name TEXT NOT NULL, criticality TEXT NOT NULL, source_row INTEGER NOT NULL);
        CREATE TABLE sensor_limits (
            equipment_id TEXT NOT NULL REFERENCES equipment(equipment_id), sensor TEXT NOT NULL, measures TEXT NOT NULL,
            unit TEXT NOT NULL, warning_high REAL NOT NULL, alarm_high REAL NOT NULL, rolling_avg_high REAL NOT NULL,
            source_row INTEGER NOT NULL, PRIMARY KEY (equipment_id, sensor));
        CREATE TABLE readings (
            id INTEGER PRIMARY KEY, timestamp TEXT NOT NULL, equipment_id TEXT NOT NULL, sensor TEXT NOT NULL,
            value REAL NOT NULL, unit TEXT NOT NULL, quality TEXT NOT NULL CHECK (quality IN ('GOOD','BAD')),
            source_row INTEGER NOT NULL, UNIQUE (equipment_id, sensor, timestamp),
            FOREIGN KEY (equipment_id, sensor) REFERENCES sensor_limits(equipment_id, sensor));
        CREATE TABLE import_rejects (
            id INTEGER PRIMARY KEY, file TEXT NOT NULL, source_row INTEGER NOT NULL, raw TEXT NOT NULL, reason TEXT NOT NULL);
        CREATE TABLE alerts (
            id INTEGER PRIMARY KEY, rule_id TEXT NOT NULL, severity TEXT NOT NULL, equipment_id TEXT NOT NULL,
            sensor TEXT NOT NULL, timestamp TEXT NOT NULL, value REAL NOT NULL, unit TEXT NOT NULL, limit_value REAL NOT NULL,
            original_value REAL NOT NULL, original_unit TEXT NOT NULL, window_good_count INTEGER, message TEXT NOT NULL,
            source_row INTEGER NOT NULL);
        CREATE INDEX ix_alerts_equipment ON alerts(equipment_id, timestamp);
        CREATE INDEX ix_alerts_rule ON alerts(rule_id);
        """;

    public static ImportReport Rebuild(string databasePath, string dataDirectory, RuleOptions rules)
    {
        var report = new ImportReport();
        using var conn = Open(databasePath);
        Exec(conn, "PRAGMA foreign_keys = ON;");
        Exec(conn, Schema);

        Exec(conn, "BEGIN;");
        var equipmentIds = LoadEquipment(conn, Path.Combine(dataDirectory, EquipmentFile), report);
        var limits = LoadLimits(conn, Path.Combine(dataDirectory, LimitsFile), equipmentIds, report);
        var readings = LoadReadings(conn, Path.Combine(dataDirectory, ReadingsFile), limits, new UnitConverter(rules.UnitConversions), report);

        var alerts = new RuleEngine(rules).Evaluate(readings, limits.Values);
        InsertAlerts(conn, alerts);
        report.Alerts = alerts.Count;
        Exec(conn, "COMMIT;");
        return report;
    }

    public static SqliteConnection Open(string databasePath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Insert(SqliteConnection conn, string table, params string[] columns)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", columns)}) VALUES ({string.Join(",", columns.Select(c => "$" + c))})";
        foreach (var c in columns) cmd.Parameters.Add(new SqliteParameter("$" + c, null));
        return cmd;
    }

    private static void Run(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    private static void RejectRow(SqliteCommand rejects, ImportReport report, string file, long row, string raw, string reason)
    {
        Run(rejects, file, row, raw, reason);
        report.Reject(file, reason);
    }

    private static bool TryNumber(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool Missing(IReadOnlyDictionary<string, string> f, params string[] names) =>
        names.Any(n => string.IsNullOrWhiteSpace(f.GetValueOrDefault(n)));

    private static HashSet<string> LoadEquipment(SqliteConnection conn, string path, ImportReport report)
    {
        using var cmd = Insert(conn, "equipment", "equipment_id", "equipment_type", "description", "site_id", "site_name", "criticality", "source_row");
        using var rejects = Insert(conn, "import_rejects", "file", "source_row", "raw", "reason");
        var ids = new HashSet<string>();
        using var reader = new StreamReader(path);
        foreach (var (row, f, raw) in Csv.Read(reader))
        {
            report.For(EquipmentFile).Read++;
            if (Missing(f, "equipment_id", "site_id")) { RejectRow(rejects, report, EquipmentFile, row, raw, RejectReasons.MissingField); continue; }
            if (!ids.Add(f["equipment_id"])) { RejectRow(rejects, report, EquipmentFile, row, raw, RejectReasons.Duplicate); continue; }
            Run(cmd, f["equipment_id"], f["equipment_type"], f["description"], f["site_id"], f["site_name"], f["criticality"], row);
            report.For(EquipmentFile).Loaded++;
        }
        return ids;
    }

    private static Dictionary<(string, string), SensorLimit> LoadLimits(SqliteConnection conn, string path, HashSet<string> equipmentIds, ImportReport report)
    {
        using var cmd = Insert(conn, "sensor_limits", "equipment_id", "sensor", "measures", "unit", "warning_high", "alarm_high", "rolling_avg_high", "source_row");
        using var rejects = Insert(conn, "import_rejects", "file", "source_row", "raw", "reason");
        var limits = new Dictionary<(string, string), SensorLimit>();
        using var reader = new StreamReader(path);
        foreach (var (row, f, raw) in Csv.Read(reader))
        {
            report.For(LimitsFile).Read++;
            if (Missing(f, "equipment_id", "sensor", "unit")) { RejectRow(rejects, report, LimitsFile, row, raw, RejectReasons.MissingField); continue; }
            if (!equipmentIds.Contains(f["equipment_id"])) { RejectRow(rejects, report, LimitsFile, row, raw, RejectReasons.UnknownEquipment); continue; }
            if (!TryNumber(f["warning_high"], out var warn) || !TryNumber(f["alarm_high"], out var alarm) || !TryNumber(f["rolling_avg_high"], out var avg))
            { RejectRow(rejects, report, LimitsFile, row, raw, RejectReasons.InvalidValue); continue; }
            var key = (f["equipment_id"], f["sensor"]);
            if (limits.ContainsKey(key)) { RejectRow(rejects, report, LimitsFile, row, raw, RejectReasons.Duplicate); continue; }
            limits[key] = new SensorLimit(f["equipment_id"], f["sensor"], f["measures"], f["unit"], warn, alarm, avg);
            Run(cmd, f["equipment_id"], f["sensor"], f["measures"], f["unit"], warn, alarm, avg, row);
            report.For(LimitsFile).Loaded++;
        }
        return limits;
    }

    private static List<Reading> LoadReadings(SqliteConnection conn, string path, Dictionary<(string, string), SensorLimit> limits,
        UnitConverter converter, ImportReport report)
    {
        using var cmd = Insert(conn, "readings", "timestamp", "equipment_id", "sensor", "value", "unit", "quality", "source_row");
        using var rejects = Insert(conn, "import_rejects", "file", "source_row", "raw", "reason");
        var seen = new Dictionary<(string, string, DateTimeOffset), Reading>();
        var readings = new List<Reading>();
        using var reader = new StreamReader(path);
        foreach (var (row, f, raw) in Csv.Read(reader))
        {
            report.For(ReadingsFile).Read++;
            string? reason = null;
            DateTimeOffset ts = default;
            double value = 0;
            SensorLimit? limit = null;

            if (Missing(f, "timestamp", "equipment_id", "sensor", "value", "unit", "quality")) reason = RejectReasons.MissingField;
            else if (!DateTimeOffset.TryParseExact(f["timestamp"], TimestampFormat, CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out ts)) reason = RejectReasons.InvalidTimestamp;
            else if (!limits.TryGetValue((f["equipment_id"], f["sensor"]), out limit)) reason = RejectReasons.UnknownSensor;
            else if (!TryNumber(f["value"], out value)) reason = RejectReasons.InvalidValue;
            else if (f["quality"] is not (Reading.Quality_Good or Reading.Quality_Bad)) reason = RejectReasons.InvalidQuality;
            else if (!converter.CanConvert(f["unit"], limit.Unit)) reason = RejectReasons.UnsupportedUnit;

            if (reason is null)
            {
                var reading = new Reading(ts, f["equipment_id"], f["sensor"], value, f["unit"], f["quality"], row);
                if (seen.TryGetValue((reading.EquipmentId, reading.Sensor, ts), out var first))
                    reason = first.Value == reading.Value && first.Unit == reading.Unit && first.Quality == reading.Quality
                        ? RejectReasons.Duplicate : RejectReasons.ConflictingDuplicate;
                else
                {
                    seen[(reading.EquipmentId, reading.Sensor, ts)] = reading;
                    readings.Add(reading);
                    Run(cmd, ts.ToString(TimestampFormat, CultureInfo.InvariantCulture), reading.EquipmentId, reading.Sensor,
                        value, reading.Unit, reading.Quality, row);
                    report.For(ReadingsFile).Loaded++;
                    if (!reading.IsGood) report.BadReadings++;
                    continue;
                }
            }
            RejectRow(rejects, report, ReadingsFile, row, raw, reason);
        }
        return readings;
    }

    private static void InsertAlerts(SqliteConnection conn, IEnumerable<Alert> alerts)
    {
        using var cmd = Insert(conn, "alerts", "rule_id", "severity", "equipment_id", "sensor", "timestamp", "value", "unit",
            "limit_value", "original_value", "original_unit", "window_good_count", "message", "source_row");
        foreach (var a in alerts)
            Run(cmd, a.RuleId, a.Severity, a.EquipmentId, a.Sensor, a.Timestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
                a.Value, a.Unit, a.Limit, a.OriginalValue, a.OriginalUnit, a.WindowGoodCount, a.Message, a.SourceRow);
    }
}
