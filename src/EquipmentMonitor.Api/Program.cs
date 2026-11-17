using System.Text.Json;
using EquipmentMonitor.Core;
using EquipmentMonitor.Core.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

var options = builder.Configuration.GetSection(MonitorOptions.Section).Get<MonitorOptions>() ?? new MonitorOptions();
var root = builder.Environment.ContentRootPath;
var dataDir = Path.GetFullPath(Path.Combine(root, options.DataDirectory));
var dbPath = Path.GetFullPath(Path.Combine(root, options.DatabasePath));

var report = MonitorDatabase.Rebuild(dbPath, dataDir, options.Rules);
builder.Services.AddSingleton(new MonitorRepository(dbPath, options.LocalUtcOffset));
builder.Services.AddSingleton(report);

var app = builder.Build();
app.Logger.LogInformation("Database rebuilt at {Path}: {Report}", dbPath, report);

app.UseExceptionHandler(error => error.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(ApiError.Body("INTERNAL_ERROR", "Something went wrong. Try again or contact support."));
}));
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/sites", (MonitorRepository repo) =>
{
    var sites = repo.Sites();
    return Results.Ok(Page.Of(sites, sites.Count, sites.Count, 0));
});

api.MapGet("/equipment", (HttpRequest req, MonitorRepository repo) =>
{
    var problems = new List<ApiError.Detail>();
    var siteId = Query.Site(req, repo, problems);
    if (problems.Count > 0) return ApiError.BadRequest(problems);
    var items = repo.Equipment(siteId);
    return Results.Ok(Page.Of(items, items.Count, items.Count, 0));
});

api.MapGet("/equipment/{equipment_id}", (string equipment_id, MonitorRepository repo) =>
{
    var e = repo.EquipmentById(equipment_id);
    if (e is null) return ApiError.NotFound(equipment_id);
    return Results.Ok(new EquipmentDetail(e, repo.SensorsFor(equipment_id)));
});

api.MapGet("/equipment/{equipment_id}/alerts", (string equipment_id, HttpRequest req, MonitorRepository repo) =>
{
    if (repo.EquipmentById(equipment_id) is null) return ApiError.NotFound(equipment_id);
    var problems = new List<ApiError.Detail>();
    var q = Query.Alerts(req, repo, problems) with { EquipmentId = equipment_id };
    if (problems.Count > 0) return ApiError.BadRequest(problems);
    var (items, total) = repo.Alerts(q);
    return Results.Ok(Page.Of(items, total, q.Limit, q.Offset));
});

api.MapGet("/alerts", (HttpRequest req, MonitorRepository repo) =>
{
    var problems = new List<ApiError.Detail>();
    var q = Query.Alerts(req, repo, problems) with { SiteId = Query.Site(req, repo, problems) };
    if (problems.Count > 0) return ApiError.BadRequest(problems);
    var (items, total) = repo.Alerts(q);
    return Results.Ok(Page.Of(items, total, q.Limit, q.Offset));
});

api.MapGet("/import-report", (ImportReport r) => Results.Ok(r));

api.MapFallback(() => Results.Json(ApiError.Body("NOT_FOUND", "No such API path."), statusCode: 404));

app.Run();

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Limit, int Offset);

public static class Page
{
    public static Page<T> Of<T>(IReadOnlyList<T> items, int total, int limit, int offset) => new(items, total, limit, offset);
}

public sealed record EquipmentDetail(EquipmentSummary Equipment, IReadOnlyList<SensorDetail> Sensors);

public static class ApiError
{
    public sealed record Detail(string Field, string Problem, string? Value = null);
    public sealed record Error(string Code, string Message, IReadOnlyList<Detail> Details);
    public sealed record Envelope(Error Error);

    public static Envelope Body(string code, string message, IReadOnlyList<Detail>? details = null) =>
        new(new Error(code, message, details ?? []));

    public static IResult BadRequest(IReadOnlyList<Detail> details) =>
        Results.Json(Body("VALIDATION_FAILED", $"The request has {details.Count} invalid field{(details.Count == 1 ? "" : "s")}.", details), statusCode: 400);

    public static IResult NotFound(string equipmentId) =>
        Results.Json(Body("EQUIPMENT_NOT_FOUND", $"No equipment with id '{equipmentId}'.",
            [new Detail("equipment_id", "Unknown equipment.", equipmentId)]), statusCode: 404);
}

internal static class Query
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;

    private static string? Get(HttpRequest req, string name) =>
        req.Query.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString().Trim() : null;

    public static string? Site(HttpRequest req, MonitorRepository repo, List<ApiError.Detail> problems)
    {
        var site = Get(req, "site_id");
        if (site is not null && !repo.SiteExists(site)) problems.Add(new("site_id", "Unknown site.", site));
        return site;
    }

    public static AlertQuery Alerts(HttpRequest req, MonitorRepository repo, List<ApiError.Detail> problems)
    {
        var rule = Get(req, "rule");
        if (rule is not null && !RuleIds.All.Contains(rule))
            problems.Add(new("rule", $"Must be one of {string.Join(", ", RuleIds.All)}.", rule));
        var sensor = Get(req, "sensor");
        if (sensor is not null && !repo.Sensors().Contains(sensor))
            problems.Add(new("sensor", $"Must be one of {string.Join(", ", repo.Sensors())}.", sensor));
        var limit = Int(req, "limit", DefaultLimit, 1, MaxLimit, problems);
        var offset = Int(req, "offset", 0, 0, int.MaxValue, problems);
        return new AlertQuery(RuleId: rule, Sensor: sensor, Limit: limit, Offset: offset);
    }

    private static int Int(HttpRequest req, string name, int fallback, int min, int max, List<ApiError.Detail> problems)
    {
        var raw = Get(req, name);
        if (raw is null) return fallback;
        if (int.TryParse(raw, out var v) && v >= min && v <= max) return v;
        problems.Add(new(name, max == int.MaxValue ? $"Must be a whole number, {min} or more." : $"Must be a whole number from {min} to {max}.", raw));
        return fallback;
    }
}

public partial class Program;
