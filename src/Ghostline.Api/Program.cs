using Ghostline.Api.Data;
using Ghostline.Api.Models;
using Ghostline.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Database & Services Registration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IReplayVerificationService, ReplayVerificationService>();

// 2. OpenAPI / Swagger Setup
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 3. API Endpoints

// Submits a gameplay run with its binary replay payload for authoritative server verification.
app.MapPost("/api/runs", async (
    SubmitRunRequest request,
    AppDbContext db,
    IReplayVerificationService verifier) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerName))
        return Results.BadRequest(new { error = "PlayerName is required." });

    if (request.ReplayBase64 == null || request.ReplayBase64.Length == 0)
        return Results.BadRequest(new { error = "Replay data is required." });

    byte[] replayBytes;
    try
    {
        replayBytes = Convert.FromBase64String(request.ReplayBase64);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "Invalid Base64 format for ReplayBase64." });
    }

    // Run authoritative headless re-simulation
    var result = verifier.Verify(replayBytes, request.ClaimedScore);

    var record = new RunRecord
    {
        PlayerName = request.PlayerName.Trim(),
        ClaimedScore = request.ClaimedScore,
        VerifiedScore = result.AuthoritativeScore,
        TotalTicks = result.AuthoritativeTicks,
        Seed = request.Seed,
        IsVerified = result.IsValid,
        VerificationError = result.ErrorMessage,
        ReplayData = replayBytes,
        CreatedAtUtc = DateTime.UtcNow
    };

    db.Runs.Add(record);
    await db.SaveChangesAsync();

    return Results.Created($"/api/runs/{record.Id}", new
    {
        record.Id,
        record.PlayerName,
        record.ClaimedScore,
        record.VerifiedScore,
        record.TotalTicks,
        record.IsVerified,
        record.VerificationError,
        record.CreatedAtUtc
    });
})
.WithName("SubmitRun")
.WithOpenApi();

// Retrieves the top verified runs for the leaderboard.
app.MapGet("/api/leaderboard", async (AppDbContext db, int limit = 20) =>
{
    int queryLimit = Math.Clamp(limit, 1, 100);

    var topRuns = await db.Runs
        .AsNoTracking()
        .Where(r => r.IsVerified)
        .OrderByDescending(r => r.VerifiedScore)
        .ThenBy(r => r.CreatedAtUtc)
        .Take(queryLimit)
        .Select(r => new
        {
            r.Id,
            r.PlayerName,
            Score = r.VerifiedScore,
            r.TotalTicks,
            r.CreatedAtUtc
        })
        .ToListAsync();

    return Results.Ok(topRuns);
})
.WithName("GetLeaderboard")
.WithOpenApi();

/// <summary>
/// Retrieves raw binary replay bytes for match playback.
/// </summary>
app.MapGet("/api/runs/{id:guid}/replay", async (Guid id, AppDbContext db) =>
{
    var run = await db.Runs
        .AsNoTracking()
        .Where(r => r.Id == id)
        .Select(r => new { r.ReplayData })
        .FirstOrDefaultAsync();

    if (run == null)
        return Results.NotFound();

    return Results.File(run.ReplayData, "application/octet-stream", $"{id}.replay");
})
.WithName("DownloadReplay")
.WithOpenApi();

app.Run();

// DTO for run submission
public record SubmitRunRequest(
    string PlayerName,
    int ClaimedScore,
    uint Seed,
    string ReplayBase64
);