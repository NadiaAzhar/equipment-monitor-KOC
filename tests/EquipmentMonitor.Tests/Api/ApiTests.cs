using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EquipmentMonitor.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Directory.CreateTempSubdirectory("em-api-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Monitor:DatabasePath", Path.Combine(_dir, "api.db"));
        builder.UseSetting("Monitor:DataDirectory", Import.RealDataTests.DataDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(HttpStatusCode Status, JsonElement Body)> Get(string path)
    {
        var res = await _client.GetAsync(path);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return (res.StatusCode, body);
    }

    private static void AssertPage(JsonElement body)
    {
        foreach (var f in new[] { "items", "total", "limit", "offset" }) Assert.True(body.TryGetProperty(f, out _), $"missing {f}");
    }

    private static void AssertError(JsonElement body, string code)
    {
        var error = body.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
        Assert.Equal(JsonValueKind.Array, error.GetProperty("details").ValueKind);
    }

    [Fact]
    public async Task Sites_ReturnsThreeSites()
    {
        var (status, body) = await Get("/api/sites");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertPage(body);
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(12, body.GetProperty("items").EnumerateArray().Sum(s => s.GetProperty("equipment_count").GetInt32()));
    }

    [Fact]
    public async Task Equipment_AllSites_ReturnsTwelve_SortedBySeverity()
    {
        var (status, body) = await Get("/api/equipment");
        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(12, items.Count);
        var ranks = items.Select(e => e.GetProperty("status").GetString() switch { "ALARM" => 0, "WARNING" => 1, _ => 2 }).ToList();
        Assert.Equal(ranks.Order(), ranks);
        var first = items[0];
        foreach (var f in new[] { "equipment_id", "site_id", "site_name", "criticality", "status", "alert_counts", "bad_reading_count" })
            Assert.True(first.TryGetProperty(f, out _), $"missing {f}");
    }

    [Fact]
    public async Task Equipment_FilteredBySite_ReturnsOnlyThatSite()
    {
        var (_, body) = await Get("/api/equipment?site_id=BS-01");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, e => Assert.Equal("BS-01", e.GetProperty("site_id").GetString()));
    }

    [Fact]
    public async Task Equipment_UnknownSite_Returns400()
    {
        var (status, body) = await Get("/api/equipment?site_id=NOPE");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(body, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task EquipmentById_ReturnsStatusAndLimits()
    {
        var (status, body) = await Get("/api/equipment/P-103");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ALARM", body.GetProperty("equipment").GetProperty("status").GetString());
        Assert.Equal(3, body.GetProperty("sensors").GetArrayLength());
    }

    [Theory]
    [InlineData("/api/equipment/NOPE")]
    [InlineData("/api/equipment/NOPE/alerts")]
    public async Task UnknownEquipment_Returns404(string path)
    {
        var (status, body) = await Get(path);
        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertError(body, "EQUIPMENT_NOT_FOUND");
    }

    [Fact]
    public async Task EquipmentAlerts_HaveRuleSensorKuwaitTimeAndReadingVsLimit()
    {
        var (status, body) = await Get("/api/equipment/K-202/alerts");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertPage(body);
        var alert = body.GetProperty("items").EnumerateArray().First(a => a.GetProperty("rule_id").GetString() == "HIGH_ALARM");
        Assert.EndsWith("+03:00", alert.GetProperty("timestamp").GetString());
        Assert.True(alert.GetProperty("value").GetDouble() >= alert.GetProperty("limit").GetDouble());
        Assert.False(string.IsNullOrEmpty(alert.GetProperty("sensor").GetString()));
        Assert.StartsWith("HIGH_ALARM:", alert.GetProperty("message").GetString());
    }

    [Fact]
    public async Task EquipmentAlerts_PsiSensor_ShowsConvertedAndOriginal()
    {
        var (_, body) = await Get("/api/equipment/K-203/alerts?sensor=PRESSURE&rule=HIGH_WARNING&limit=1");
        var a = body.GetProperty("items")[0];
        Assert.Equal("bar", a.GetProperty("unit").GetString());
        Assert.Equal("psi", a.GetProperty("original_unit").GetString());
    }

    [Fact]
    public async Task EquipmentAlerts_Paging_ReturnsRequestedSlice()
    {
        var (_, all) = await Get("/api/equipment/P-103/alerts?limit=500");
        var (_, page) = await Get("/api/equipment/P-103/alerts?limit=5&offset=5");
        Assert.Equal(all.GetProperty("total").GetInt32(), page.GetProperty("total").GetInt32());
        Assert.Equal(5, page.GetProperty("limit").GetInt32());
        Assert.Equal(5, page.GetProperty("offset").GetInt32());
        var expected = all.GetProperty("items").EnumerateArray().Skip(5).Take(5).Select(a => a.GetProperty("alert_id").GetInt64());
        Assert.Equal(expected, page.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("alert_id").GetInt64()));
    }

    [Fact]
    public async Task Alerts_FilterByRuleAndSite()
    {
        var (status, body) = await Get("/api/alerts?site_id=GC-01&rule=HIGH_ALARM");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.All(body.GetProperty("items").EnumerateArray(), a =>
        {
            Assert.Equal("HIGH_ALARM", a.GetProperty("rule_id").GetString());
            Assert.Equal("GC-01", a.GetProperty("site_id").GetString());
        });
        Assert.True(body.GetProperty("total").GetInt32() > 0);
    }

    [Theory]
    [InlineData("/api/alerts?rule=LOW")]
    [InlineData("/api/alerts?sensor=FLOW")]
    [InlineData("/api/alerts?limit=0")]
    [InlineData("/api/alerts?limit=abc")]
    [InlineData("/api/alerts?offset=-1")]
    [InlineData("/api/equipment/P-101/alerts?limit=501")]
    public async Task InvalidQuery_Returns400WithDetails(string path)
    {
        var (status, body) = await Get(path);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(body, "VALIDATION_FAILED");
        Assert.True(body.GetProperty("error").GetProperty("details").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Readings_PsiSensor_AreConvertedToTheLimitUnit()
    {
        var (status, body) = await Get("/api/equipment/K-203/readings?sensor=PRESSURE&limit=5000");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertPage(body);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, p => Assert.Equal("bar", p.GetProperty("unit").GetString()));
        var (_, detail) = await Get("/api/equipment/K-203");
        var alarm = detail.GetProperty("sensors").EnumerateArray().First(s => s.GetProperty("sensor").GetString() == "PRESSURE").GetProperty("alarm_high").GetDouble();
        Assert.True(items.Max(p => p.GetProperty("max").GetDouble()) < alarm * 3, "psi values would be about 14.5 times larger if unconverted");
    }

    [Fact]
    public async Task Readings_BadReadings_AreCountedButNeverPlotted()
    {
        var (_, equipment) = await Get("/api/equipment");
        var id = equipment.GetProperty("items").EnumerateArray().First(e => e.GetProperty("bad_reading_count").GetInt32() > 0).GetProperty("equipment_id").GetString();
        var (_, body) = await Get($"/api/equipment/{id}/readings?limit=5000");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, p => p.GetProperty("bad_count").GetInt32() > 0);
        Assert.DoesNotContain(items, p => p.GetProperty("max").ValueKind == JsonValueKind.Number && p.GetProperty("max").GetDouble() >= 999);
    }

    [Fact]
    public async Task Readings_Buckets_AreOrderedUniqueKuwaitTimeAndGapsAreNotFilled()
    {
        var totals = new List<int>();
        foreach (var sensor in new[] { "VIBRATION", "TEMPERATURE", "PRESSURE" })
        {
            var (_, body) = await Get($"/api/equipment/P-101/readings?sensor={sensor}&limit=5000");
            var times = body.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("timestamp").GetString()!).ToList();
            Assert.All(times, t => Assert.EndsWith("+03:00", t));
            Assert.Equal(times.Distinct().Count(), times.Count);
            Assert.Equal(times.Order(StringComparer.Ordinal), times);
            totals.Add(body.GetProperty("total").GetInt32());
        }
        Assert.All(totals, t => Assert.InRange(t, 1, 1440));
    }

    [Fact]
    public async Task Readings_BucketMinutes_AggregatesWithMinAndMax()
    {
        var (_, body) = await Get("/api/equipment/P-101/readings?sensor=VIBRATION&bucket_minutes=60&limit=100");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.InRange(items.Count, 1, 25);
        Assert.All(items.Where(p => p.GetProperty("good_count").GetInt32() > 0), p =>
        {
            Assert.True(p.GetProperty("min").GetDouble() <= p.GetProperty("value").GetDouble());
            Assert.True(p.GetProperty("value").GetDouble() <= p.GetProperty("max").GetDouble());
        });
    }

    [Fact]
    public async Task Readings_TimeRange_LimitsTheBuckets()
    {
        var (_, body) = await Get("/api/equipment/P-101/readings?sensor=VIBRATION&from=2026-10-14T06:00:00%2B03:00&to=2026-10-14T07:00:00%2B03:00");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.InRange(items.Count, 1, 60);
        Assert.All(items, p => Assert.StartsWith("2026-10-14T06:", p.GetProperty("timestamp").GetString()));
    }

    [Fact]
    public async Task Readings_UnknownEquipment_Returns404()
    {
        var (status, body) = await Get("/api/equipment/NOPE/readings");
        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertError(body, "EQUIPMENT_NOT_FOUND");
    }

    [Theory]
    [InlineData("sensor=FLOW")]
    [InlineData("bucket_minutes=0")]
    [InlineData("bucket_minutes=121")]
    [InlineData("limit=5001")]
    [InlineData("from=yesterday")]
    [InlineData("from=2026-10-14T08:00:00%2B03:00&to=2026-10-14T07:00:00%2B03:00")]
    public async Task Readings_InvalidQuery_Returns400WithDetails(string query)
    {
        var (status, body) = await Get($"/api/equipment/P-101/readings?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(body, "VALIDATION_FAILED");
        Assert.True(body.GetProperty("error").GetProperty("details").GetArrayLength() > 0);
    }

    [Fact]
    public async Task AlertsSummary_HourlyCountsAddUpToAlertTotals()
    {
        var (status, summary) = await Get("/api/alerts/summary");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertPage(summary);
        var items = summary.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, i => Assert.EndsWith(":00:00+03:00", i.GetProperty("hour").GetString()));
        var (_, all) = await Get("/api/alerts?limit=1");
        Assert.Equal(all.GetProperty("total").GetInt32(), items.Sum(i => i.GetProperty("alarm_count").GetInt32() + i.GetProperty("warning_count").GetInt32()));
    }

    [Fact]
    public async Task AlertsSummary_FilteredBySite_AndUnknownSite()
    {
        var (_, body) = await Get("/api/alerts/summary?site_id=GC-01");
        Assert.All(body.GetProperty("items").EnumerateArray(), i => Assert.Equal("GC-01", i.GetProperty("site_id").GetString()));
        var (status, error) = await Get("/api/alerts/summary?site_id=NOPE");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertError(error, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Dashboard_IsServed()
    {
        var res = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Equipment Monitor", await res.Content.ReadAsStringAsync());
    }
}
