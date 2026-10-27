using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Ghostline.Simulation;
using Xunit;

namespace Ghostline.Simulation.Tests;

public class ApiIntegrationTests : IClassFixture<GhostlineWebApplicationFactory>
{
    private readonly GhostlineWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(GhostlineWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthDb_ReturnsOk()
    {
        // ACT
        var response = await _client.GetAsync("/api/health/db");

        // ASSERT
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SubmitRun_WithValidReplay_ReturnsCreatedAndAppearsOnLeaderboard()
    {
        // ARRANGE: Generate an authoritative match
        uint seed = 42;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var sim = new SimulationWorld(spawnPoint, seed: seed);
        var inputs = new List<PlayerInput>();

        for (int i = 0; i < 60; i++)
        {
            var input = new PlayerInput(i, 0, 0, false, false, false, false, false, 0, 0);
            inputs.Add(input);
            sim.Step(input);
        }

        var header = new ReplayHeader(ReplayFile.CurrentVersion, seed, spawnPoint, inputs.Count);
        var replay = new ReplayFile(header, inputs);
        string base64 = Convert.ToBase64String(replay.Serialize());

        var request = new
        {
            playerName = "AcePilot",
            claimedScore = sim.Score,
            seed = seed,
            replayBase64 = base64
        };

        // ACT
        var response = await _client.PostAsJsonAsync("/api/runs", request);

        // ASSERT
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(jsonDoc.RootElement.GetProperty("isVerified").GetBoolean());
        Assert.Equal(sim.Score, jsonDoc.RootElement.GetProperty("verifiedScore").GetInt32());

        // Verify it appears on the leaderboard
        var leaderboardResponse = await _client.GetAsync("/api/leaderboard");
        Assert.Equal(HttpStatusCode.OK, leaderboardResponse.StatusCode);
        string leaderboardJson = await leaderboardResponse.Content.ReadAsStringAsync();
        Assert.Contains("AcePilot", leaderboardJson);
    }

    [Fact]
    public async Task SubmitRun_WithInvalidPlayerName_ReturnsBadRequest()
    {
        // ARRANGE
        var request = new
        {
            playerName = "", // Invalid empty name
            claimedScore = 100,
            seed = (uint)1337,
            replayBase64 = "validplaceholder"
        };

        // ACT
        var response = await _client.PostAsJsonAsync("/api/runs", request);

        // ASSERT
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PlayerName is required", body);
    }

    [Fact]
    public async Task SubmitRun_WithInvalidBase64_ReturnsBadRequest()
    {
        // ARRANGE
        var request = new
        {
            playerName = "Player1",
            claimedScore = 0,
            seed = (uint)1337,
            replayBase64 = "This is not valid base64!@#$"
        };

        // ACT
        var response = await _client.PostAsJsonAsync("/api/runs", request);

        // ASSERT
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid Base64 format", body);
    }

    [Fact]
    public async Task SubmitRun_WithMismatchedSeed_ReturnsBadRequest()
    {
        // ARRANGE: Replay has seed 1234, but request claims seed 9999
        var spawnPoint = new SimVector2(500_000, 400_000);
        var replay = new ReplayFile(new ReplayHeader(1, Seed: 1234, spawnPoint, 0), Array.Empty<PlayerInput>());
        string base64 = Convert.ToBase64String(replay.Serialize());

        var request = new
        {
            playerName = "Player1",
            claimedScore = 0,
            seed = (uint)9999, // Mismatch
            replayBase64 = base64
        };

        // ACT
        var response = await _client.PostAsJsonAsync("/api/runs", request);

        // ASSERT
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("seed does not match", body);
    }

    [Fact]
    public async Task SubmitRun_WithDuplicateReplayPayload_ReturnsConflict()
    {
        // ARRANGE: Construct a valid replay
        uint seed = 8888;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var replay = new ReplayFile(new ReplayHeader(1, seed, spawnPoint, 0), Array.Empty<PlayerInput>());
        string base64 = Convert.ToBase64String(replay.Serialize());

        var request = new
        {
            playerName = "Repeater",
            claimedScore = 0,
            seed = seed,
            replayBase64 = base64
        };

        // ACT: Submit first time (201 Created)
        var firstResponse = await _client.PostAsJsonAsync("/api/runs", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // ACT: Submit identical payload second time
        var secondResponse = await _client.PostAsJsonAsync("/api/runs", request);

        // ASSERT: Must be rejected as duplicate
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        string body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate submission", body);
    }

    [Fact]
    public async Task SubmitRun_ExceedingRateLimit_ReturnsTooManyRequests()
    {
        // ARRANGE: Use a dedicated client with an isolated IP so other tests are not throttled
        var throttledClient = _factory.CreateClient();
        throttledClient.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.1.99");

        var spawnPoint = new SimVector2(500_000, 400_000);
        bool hitRateLimit = false;

        for (int i = 0; i < 25; i++)
        {
            uint seed = (uint)(20000 + i);
            var replay = new ReplayFile(new ReplayHeader(1, seed, spawnPoint, 0), Array.Empty<PlayerInput>());
            string base64 = Convert.ToBase64String(replay.Serialize());

            var request = new
            {
                playerName = $"User{i}",
                claimedScore = 0,
                seed = seed,
                replayBase64 = base64
            };

            var response = await throttledClient.PostAsJsonAsync("/api/runs", request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                hitRateLimit = true;
                break;
            }
        }

        // ASSERT: Must eventually reject with 429 Too Many Requests
        Assert.True(hitRateLimit, "Expected the rate limiter to trigger 429 Too Many Requests after exceeding window limit.");
    }

    [Fact]
    public async Task GetLeaderboard_WithSeedFilter_ReturnsOnlyMatchingRuns()
    {
        // ARRANGE: Submit verified runs with two distinct seeds
        uint seedTarget = 5555;
        uint seedOther = 7777;
        var spawnPoint = new SimVector2(500_000, 400_000);

        // Supply at least 1 input frame so the replay passes authoritative verification
        var inputs = new List<PlayerInput>
        {
            new(Tick: 0, MoveX: 0, MoveY: 0, Dash: false, Attack: false, SkillQ: false, SkillE: false, SkillR: false, AimTargetX: 0, AimTargetY: 0)
        };

        var targetReplay = new ReplayFile(new ReplayHeader(ReplayFile.CurrentVersion, seedTarget, spawnPoint, inputs.Count), inputs);
        var otherReplay = new ReplayFile(new ReplayHeader(ReplayFile.CurrentVersion, seedOther, spawnPoint, inputs.Count), inputs);

        var post1 = await _client.PostAsJsonAsync("/api/runs", new
        {
            playerName = "TargetSeedUser",
            claimedScore = 0,
            seed = seedTarget,
            replayBase64 = Convert.ToBase64String(targetReplay.Serialize())
        });
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);
        using var doc1 = JsonDocument.Parse(await post1.Content.ReadAsStringAsync());
        Assert.True(doc1.RootElement.GetProperty("isVerified").GetBoolean());

        var post2 = await _client.PostAsJsonAsync("/api/runs", new
        {
            playerName = "OtherSeedUser",
            claimedScore = 0,
            seed = seedOther,
            replayBase64 = Convert.ToBase64String(otherReplay.Serialize())
        });
        Assert.Equal(HttpStatusCode.Created, post2.StatusCode);
        using var doc2 = JsonDocument.Parse(await post2.Content.ReadAsStringAsync());
        Assert.True(doc2.RootElement.GetProperty("isVerified").GetBoolean());

        // ACT: Query specifically for seedTarget
        var response = await _client.GetAsync($"/api/leaderboard?seed={seedTarget}");

        // ASSERT: Only the run matching seedTarget should appear
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("TargetSeedUser", json);
        Assert.DoesNotContain("OtherSeedUser", json);
    }

    [Fact]
    public async Task GetLeaderboard_WithPagination_HonorsOffsetAndLimit()
    {
        // ACT: Query page 1 with limit 1, then page 2 with limit 1
        var page1 = await _client.GetFromJsonAsync<List<JsonElement>>("/api/leaderboard?page=1&limit=1");
        var page2 = await _client.GetFromJsonAsync<List<JsonElement>>("/api/leaderboard?page=2&limit=1");

        // ASSERT
        Assert.NotNull(page1);
        Assert.NotNull(page2);
        if (page1.Count > 0 && page2.Count > 0)
        {
            Assert.NotEqual(page1[0].GetProperty("id").GetGuid(), page2[0].GetProperty("id").GetGuid());
        }
    }
}