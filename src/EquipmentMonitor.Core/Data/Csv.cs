using System.Text;

namespace EquipmentMonitor.Core.Data;

public static class Csv
{
    /// <summary>Reads a CSV with a header row. Yields (1-based data row number, header-to-value map, raw line).</summary>
    public static IEnumerable<(long Row, IReadOnlyDictionary<string, string> Fields, string Raw)> Read(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("The file is empty.");
        var header = Split(headerLine.TrimStart('\uFEFF')).Select(h => h.Trim()).ToArray();
        long row = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            row++;
            if (line.Length == 0) continue;
            var values = Split(line);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Length; i++) map[header[i]] = i < values.Count ? values[i].Trim() : "";
            yield return (row, map, line);
        }
    }

    public static List<string> Split(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
