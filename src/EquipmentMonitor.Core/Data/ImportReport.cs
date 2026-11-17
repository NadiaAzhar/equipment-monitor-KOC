namespace EquipmentMonitor.Core.Data;

public sealed class ImportReport
{
    public Dictionary<string, FileCounts> Files { get; } = new();
    public Dictionary<string, int> RejectReasons { get; } = new();
    public int BadReadings { get; set; }
    public int Alerts { get; set; }

    public FileCounts For(string file) => Files.TryGetValue(file, out var c) ? c : Files[file] = new FileCounts();

    public void Reject(string file, string reason)
    {
        For(file).Rejected++;
        RejectReasons[reason] = RejectReasons.GetValueOrDefault(reason) + 1;
    }

    public override string ToString() =>
        string.Join("; ", Files.Select(f => $"{f.Key}: read {f.Value.Read}, loaded {f.Value.Loaded}, rejected {f.Value.Rejected}")) +
        $"; reject reasons: {(RejectReasons.Count == 0 ? "none" : string.Join(", ", RejectReasons.Select(r => $"{r.Key}={r.Value}")))}" +
        $"; BAD readings kept: {BadReadings}; alerts: {Alerts}";
}

public sealed class FileCounts
{
    public int Read { get; set; }
    public int Loaded { get; set; }
    public int Rejected { get; set; }
}

public static class RejectReasons
{
    public const string Duplicate = "DUPLICATE";
    public const string ConflictingDuplicate = "CONFLICTING_DUPLICATE";
    public const string UnknownSensor = "UNKNOWN_SENSOR";
    public const string UnknownEquipment = "UNKNOWN_EQUIPMENT";
    public const string UnsupportedUnit = "UNSUPPORTED_UNIT";
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";
    public const string InvalidValue = "INVALID_VALUE";
    public const string InvalidQuality = "INVALID_QUALITY";
    public const string MissingField = "MISSING_FIELD";
}
