using LastTrain.Api.Data;
using LastTrain.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "lastrain.db";
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));builder.Services.AddScoped<RoutingService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SeedData.Apply(db);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

// Station names + which line(s) they're on, with each line's station code
// and colour — e.g. MacPherson -> [{code: "CC10", colourHex: "#F9A000"}, {code: "DT26", colourHex: "#005EC4"}].
// Used by the Flutter app to show "MacPherson (CC10)(DT26)" in the dropdown
// and results, same codes/colours as the official line legend.
app.MapGet("/api/stations", async (AppDbContext db) =>
{
    var rows = await db.StationLines
        .Include(sl => sl.Station)
        .Include(sl => sl.Line)
        .ToListAsync();

    var grouped = rows
        .GroupBy(sl => sl.Station.Name)
        .OrderBy(g => g.Key)
        .Select(g => new
        {
            name = g.Key,
            codes = g.Select(sl => new { code = sl.StationCode, colourHex = sl.Line.ColourHex }).ToList()
        });

    return Results.Ok(grouped);
});

// Today's date/day-of-week/PH status, for display only. Last-train times
// are the same every day (confirmed against sgtrains.com), so this does NOT
// feed into /api/plan's calculation — it's shown so you know what day it
// thinks it is, and it'll matter if a first-train feature gets added later.
app.MapGet("/api/daycontext", async (AppDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    var isPublicHoliday = await db.PublicHolidays.AnyAsync(h => h.Date == today);
    var dow = DateTime.Now.DayOfWeek;

    string dayType = isPublicHoliday || dow == DayOfWeek.Sunday
        ? "Sunday & PH"
        : dow == DayOfWeek.Saturday
            ? "Saturday"
            : "Weekday (Mon-Fri)";

    return Results.Ok(new
    {
        date = today.ToString("yyyy-MM-dd"),
        dayOfWeek = dow.ToString(),
        isPublicHoliday,
        dayType
    });
});

// The main endpoint: "I'm at {from}, I need to get to {to} — what's my
// last-possible departure, and what are the connections along the way?"
app.MapGet("/api/plan", async (string from, string to, RoutingService routing) =>
{
    var result = await routing.PlanLastTrainAsync(from, to);
    return Results.Ok(result);
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
