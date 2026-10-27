using Ghostline.Api.Data;
using Ghostline.Api.Models;
using Ghostline.Api.Services;
using Ghostline.Simulation;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System;

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

// Automatically apply pending EF Core migrations on application startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Always enable developer exception page and Swagger in local dev
// PRIORITY 2: Enable detailed error pages only in development.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 3. API Endpoints

// Diagnostic test endpoint to verify raw PostgreSQL connection and table state
app.MapGet("/api/health/db", async (AppDbContext db, IWebHostEnvironment env) =>
{
    try
    {
        bool canConnect = await db.Database.CanConnectAsync();
        int count = await db.Runs.CountAsync();
        return Results.Ok(new { status = "Healthy", canConnect, runCount = count });
    }
    catch (Exception ex)
    {
        // PRIORITY 2: Return generic production errors with an appropriate request identifier.
        return Results.Problem(
            detail: env.IsDevelopment() ? ex.ToString() : "A database connection error occurred.",
            title: "Database Connection Failed",
            statusCode: 500
        );
    }
});

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

    // PRIORITY 1 & 2: Reject malformed data and derive seed from the replay itself.
    ReplayFile replay;
    try
    {
        replay = ReplayFile.Deserialize(replayBytes);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = $"Invalid replay payload: {ex.Message}" });
    }

    // PRIORITY 2: Reject mismatched seeds.
    if (request.Seed != replay.Header.Seed)
    {
        return Results.BadRequest(new { error = "Request seed does not match the seed embedded in the replay payload." });
    }

    var result = verifier.Verify(replayBytes, request.ClaimedScore);

    var record = new RunRecord
    {
        PlayerName = request.PlayerName.Trim(),
        ClaimedScore = request.ClaimedScore,
        VerifiedScore = result.AuthoritativeScore,
        TotalTicks = result.AuthoritativeTicks,
        Seed = replay.Header.Seed,
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
});

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
});

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
});

app.Run();

public record SubmitRunRequest(
    string PlayerName,
    int ClaimedScore,
    uint Seed,
    string ReplayBase64
);