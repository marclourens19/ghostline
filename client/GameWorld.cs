using Godot;
using Ghostline.Simulation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ghostline.Client;

public record LeaderboardItem(Guid Id, string PlayerName, int Score, int TotalTicks, DateTime CreatedAtUtc);

public partial class GameWorld : Node2D
{
    // Node references
    private Sprite2D _playerVisual = null!;
    private HttpRequest _http = null!;
    private HttpRequest _leaderboardHttp = null!;

    // Authoritative headless simulation instance
    private SimulationWorld _simulation = null!;

    // Fixed timestep accumulator to decouple Godot frame rate from simulation ticks
    private double _timeAccumulator;
    private const double SecondsPerTick = 1.0 / SimulationWorld.TicksPerSecond;

    // In-memory replay buffer: captures authoritative run inputs for server verification
    private readonly List<PlayerInput> _recordedInputs = new();
    private bool _showSkillQEffect;
    private string _submissionStatus = "Press F6: Export Replay | Press F7: Submit to Leaderboard | Press Tab / L: Toggle Leaderboard";
    private bool _isSubmitting;

    // Leaderboard state
    private bool _showLeaderboard;
    private readonly List<LeaderboardItem> _leaderboardEntries = new();
    private bool _isLoadingLeaderboard;

    // Default backend endpoint (adjust port if launchSettings.json specifies another, e.g., 5194)
    private const string ApiEndpoint = "http://localhost:5000/api/runs";
    private const string ApiLeaderboardEndpoint = "http://localhost:5000/api/leaderboard?limit=10";

    public override void _Ready()
    {
        _playerVisual = GetNode<Sprite2D>("PlayerVisual");

        // Set up HTTP Request node for backend communication
        _http = new HttpRequest();
        AddChild(_http);
        _http.RequestCompleted += OnHttpRequestCompleted;

        // Set up HTTP Request node for fetching leaderboard
        _leaderboardHttp = new HttpRequest();
        AddChild(_leaderboardHttp);
        _leaderboardHttp.RequestCompleted += OnLeaderboardCompleted;

        // Spawn player near center of default arena (500, 400) screen pixels with a deterministic seed
        var spawnPosition = SimVector2.FromScreenPixels(500f, 400f);
        _simulation = new SimulationWorld(spawnPosition, seed: 1337);

        // Position the visual immediately at spawn
        UpdateVisuals();

        // Initial leaderboard fetch
        FetchLeaderboard();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Toggle Leaderboard cleanly on single key down (ignoring repeat/echo frames)
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Tab || keyEvent.Keycode == Key.L)
            {
                _showLeaderboard = !_showLeaderboard;
                if (_showLeaderboard)
                {
                    FetchLeaderboard();
                }
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        // 1. Draw the arena boundary defined by the simulation
        var (minX, minY) = new SimVector2(_simulation.Arena.MinX, _simulation.Arena.MinY).ToScreenPixels();
        var (maxX, maxY) = new SimVector2(_simulation.Arena.MaxX, _simulation.Arena.MaxY).ToScreenPixels();

        var rect = new Rect2(minX, minY, maxX - minX, maxY - minY);
        DrawRect(rect, Colors.White, filled: false, width: 2.0f);

        var (px, py) = _simulation.Player.Position.ToScreenPixels();
        var playerCenter = new Vector2(px, py);

        // 2. Draw Skill R: Continuous Laser Beam
        if (_simulation.Player.LaserActiveTicks > 0)
        {
            float dirX = _simulation.Player.LaserAimDirection.X / 1000f;
            float dirY = _simulation.Player.LaserAimDirection.Y / 1000f;
            Vector2 beamEnd = playerCenter + new Vector2(dirX, dirY) * (SimulationWorld.LaserLength / 1000f);

            // Wide glow beam and bright inner beam core
            DrawLine(playerCenter, beamEnd, new Color(0.2f, 0.8f, 1.0f, 0.4f), 18f);
            DrawLine(playerCenter, beamEnd, Colors.White, 6f);
        }

        // 3. Draw Skill Q: Wide shockwave ring effect
        if (_showSkillQEffect)
        {
            float qRadius = SimulationWorld.SkillQRadius / 1000f;
            DrawArc(playerCenter, qRadius, 0, Mathf.Tau, 36, Colors.Gold, 3.5f);
            _showSkillQEffect = false;
        }

        // 4. Draw Skill E: Shield bubble around player
        if (_simulation.Player.Shield > 0)
        {
            float shieldRadius = (SimulationWorld.PlayerRadius / 1000f) + 6f;
            DrawArc(playerCenter, shieldRadius, 0, Mathf.Tau, 32, Colors.DeepSkyBlue, 3.0f);
        }

        // 5. Draw Bullets (Normal Attack)
        foreach (var proj in _simulation.Projectiles)
        {
            if (!proj.IsAlive) continue;
            var (bx, by) = proj.Position.ToScreenPixels();
            DrawCircle(new Vector2(bx, by), SimulationWorld.BulletRadius / 1000f, Colors.Yellow);
        }

        // 6. Draw enemies
        foreach (var enemy in _simulation.Enemies)
        {
            if (!enemy.IsAlive) continue;

            var (ex, ey) = enemy.Position.ToScreenPixels();
            float radius = SimulationWorld.EnemyRadius / 1000f;
            DrawCircle(new Vector2(ex, ey), radius, Colors.Crimson);
        }

        // 7. Debug HUD Overlay & Cooldown Displays
        float qCd = _simulation.Player.QCooldownTicks / (float)SimulationWorld.TicksPerSecond;
        float eCd = _simulation.Player.ShieldCooldownTicks / (float)SimulationWorld.TicksPerSecond;
        float rCd = _simulation.Player.RCooldownTicks / (float)SimulationWorld.TicksPerSecond;

        string hudText = $"Health: {_simulation.Player.Health}/{_simulation.Player.MaxHealth} | Shield: {_simulation.Player.Shield}\n" +
                         $"Score: {_simulation.Score} | Tick: {_simulation.CurrentTick}\n" +
                         $"Attack (LMB): Fire Bullet\n" +
                         $"[Q] Shockwave: {(qCd > 0 ? $"{qCd:F1}s" : "READY")}\n" +
                         $"[E] Shield (15 HP): {(eCd > 0 ? $"{eCd:F1}s" : "READY")}\n" +
                         $"[R] Laser Beam (5s): {(rCd > 0 ? $"{rCd:F1}s" : "READY")}\n" +
                         $"Recorded Inputs: {_recordedInputs.Count}\n" +
                         $"{_submissionStatus}";

        DrawString(ThemeDB.FallbackFont, new Vector2(minX + 15, minY + 30), hudText, HorizontalAlignment.Left, -1, 14, Colors.White);

        // 8. Render Leaderboard Overlay if active
        if (_showLeaderboard)
        {
            DrawLeaderboardOverlay(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private void DrawLeaderboardOverlay(float arenaX, float arenaY, float arenaWidth, float arenaHeight)
    {
        // Darkened background panel centered in arena
        float panelWidth = 480f;
        float panelHeight = 360f;
        float panelX = arenaX + (arenaWidth - panelWidth) * 0.5f;
        float panelY = arenaY + (arenaHeight - panelHeight) * 0.5f;

        var panelRect = new Rect2(panelX, panelY, panelWidth, panelHeight);
        DrawRect(panelRect, new Color(0.05f, 0.05f, 0.1f, 0.88f), filled: true);
        DrawRect(panelRect, Colors.Cyan, filled: false, width: 2.0f);

        // Header
        string title = "=== AUTHORITATIVE LEADERBOARD ===";
        DrawString(ThemeDB.FallbackFont, new Vector2(panelX + 20, panelY + 35), title, HorizontalAlignment.Left, -1, 16, Colors.Gold);

        if (_isLoadingLeaderboard)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(panelX + 20, panelY + 80), "Loading top runs...", HorizontalAlignment.Left, -1, 14, Colors.Silver);
            return;
        }

        if (_leaderboardEntries.Count == 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(panelX + 20, panelY + 80), "No verified runs logged yet. Press F7 to submit your run!", HorizontalAlignment.Left, -1, 13, Colors.Silver);
            return;
        }

        // Column Titles
        string columns = "RANK   PLAYER          SCORE       SURVIVED";
        DrawString(ThemeDB.FallbackFont, new Vector2(panelX + 20, panelY + 70), columns, HorizontalAlignment.Left, -1, 13, Colors.Cyan);

        // Entry rows
        float yOffset = panelY + 100;
        for (int i = 0; i < _leaderboardEntries.Count && i < 10; i++)
        {
            var entry = _leaderboardEntries[i];
            float seconds = entry.TotalTicks / (float)SimulationWorld.TicksPerSecond;
            string row = $"#{i + 1,-5} {entry.PlayerName,-15} {entry.Score,-11} {seconds:F1}s";

            Color rowColor = i == 0 ? Colors.Yellow : (i < 3 ? Colors.LightSkyBlue : Colors.White);
            DrawString(ThemeDB.FallbackFont, new Vector2(panelX + 20, yOffset), row, HorizontalAlignment.Left, -1, 13, rowColor);
            yOffset += 24;
        }
    }

    public override void _Process(double delta)
    {
        // Export replay file on F6 key press
        if (Input.IsKeyPressed(Key.F6))
        {
            ExportReplay("latest_run.replay");
        }

        // F7: Submit replay to backend for authoritative verification
        if (Input.IsKeyPressed(Key.F7) && !_isSubmitting)
        {
            SubmitRun();
        }

        // Accumulate elapsed real time, capped at 0.2s to prevent timestep death spirals
        _timeAccumulator = Mathf.Min((float)(_timeAccumulator + delta), 0.2f);

        /* 
         * Run fixed simulation steps whenever enough time has passed.
         * If Godot runs at 144 FPS, it might step 0 times on some frames and 1 time on others.
         * If Godot drops to 30 FPS, it will step 2 times in one frame to catch up.
         */
        while (_timeAccumulator >= SecondsPerTick)
        {
            PlayerInput input = PollInput(_simulation.CurrentTick);
            _recordedInputs.Add(input);

            if (input.SkillQ && _simulation.Player.QCooldownTicks == 0)
            {
                _showSkillQEffect = true;
            }

            _simulation.Step(input);
            _timeAccumulator -= SecondsPerTick;
        }

        // Render authoritative simulation state to screen
        UpdateVisuals();

        // Trigger _Draw() each frame to update moving entities
        QueueRedraw();
    }

    private PlayerInput PollInput(int currentTick)
    {
        sbyte moveX = 0;
        sbyte moveY = 0;

        if (Input.IsActionPressed("move_left")) moveX -= 1;
        if (Input.IsActionPressed("move_right")) moveX += 1;
        if (Input.IsActionPressed("move_up")) moveY -= 1;
        if (Input.IsActionPressed("move_down")) moveY += 1;

        bool dash = Input.IsActionJustPressed("dash");
        bool attack = Input.IsActionPressed("attack");       // Hold or click LMB to shoot
        bool skillQ = Input.IsActionJustPressed("skill_q");  // Q: Shockwave
        bool skillE = Input.IsActionJustPressed("skill_e");  // E: Shield
        bool skillR = Input.IsActionJustPressed("skill_r");  // R: Laser Beam

        // Convert mouse position to integer millipixels
        Vector2 mousePos = GetGlobalMousePosition();
        SimVector2 aimTarget = SimVector2.FromScreenPixels(mousePos.X, mousePos.Y);

        return new PlayerInput(
            Tick: currentTick,
            MoveX: moveX,
            MoveY: moveY,
            Dash: dash,
            Attack: attack,
            SkillQ: skillQ,
            SkillE: skillE,
            SkillR: skillR,
            AimTargetX: aimTarget.X,
            AimTargetY: aimTarget.Y
        );
    }

    private void UpdateVisuals()
    {
        var (screenX, screenY) = _simulation.Player.Position.ToScreenPixels();
        _playerVisual.Position = new Vector2(screenX, screenY);
    }

    // Exports the current recorded gameplay session to a binary replay file.
    // Can be called on player death, game over, or by pressing F6.
    public void ExportReplay(string filePath)
    {
        var spawnPos = SimVector2.FromScreenPixels(500f, 400f);
        var header = new ReplayHeader(
            Version: ReplayFile.CurrentVersion,
            Seed: _simulation.Seed,
            SpawnPosition: spawnPos,
            InputCount: _recordedInputs.Count
        );

        var replay = new ReplayFile(header, _recordedInputs);
        byte[] data = replay.Serialize();

        File.WriteAllBytes(filePath, data);
        _submissionStatus = $"Saved: {filePath} ({data.Length} bytes)";
        GD.Print($"Replay successfully exported to: {filePath} ({data.Length} bytes)");
    }

    // Submits the current run and binary replay payload to the backend API.
    public void SubmitRun()
    {
        if (_recordedInputs.Count == 0)
        {
            _submissionStatus = "Cannot submit: No input frames recorded.";
            return;
        }

        _isSubmitting = true;
        _submissionStatus = "Submitting run to server...";

        var spawnPos = SimVector2.FromScreenPixels(500f, 400f);
        var header = new ReplayHeader(
            Version: ReplayFile.CurrentVersion,
            Seed: _simulation.Seed,
            SpawnPosition: spawnPos,
            InputCount: _recordedInputs.Count
        );

        var replay = new ReplayFile(header, _recordedInputs);
        byte[] replayBytes = replay.Serialize();
        string base64Payload = Convert.ToBase64String(replayBytes);

        var requestBody = new
        {
            playerName = "Player1",
            claimedScore = _simulation.Score,
            seed = _simulation.Seed,
            replayBase64 = base64Payload
        };

        string json = JsonSerializer.Serialize(requestBody);
        string[] headers = new[] { "Content-Type: application/json" };

        Error err = _http.Request(ApiEndpoint, headers, HttpClient.Method.Post, json);
        if (err != Error.Ok)
        {
            _submissionStatus = $"HTTP Error: Failed to initiate request ({err})";
            _isSubmitting = false;
        }
    }

    // Fetches top runs from the backend leaderboard endpoint.
    public void FetchLeaderboard()
    {
        _isLoadingLeaderboard = true;
        _leaderboardHttp.Request(ApiLeaderboardEndpoint);
    }

    private void OnHttpRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        _isSubmitting = false;

        if (responseCode == 201)
        {
            string responseText = System.Text.Encoding.UTF8.GetString(body);
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            bool isVerified = root.GetProperty("isVerified").GetBoolean();
            int verifiedScore = root.GetProperty("verifiedScore").GetInt32();

            _submissionStatus = isVerified
                ? $"Verified by Server: Score {verifiedScore}"
                : "Rejected: Replay diverged from server simulation!";

            // Refresh leaderboard to include newly verified run
            FetchLeaderboard();
        }
        else
        {
            _submissionStatus = $"Submission Failed: HTTP {responseCode}";
            GD.PrintErr($"Run submission failed with status code {responseCode}");
        }
    }

    private void OnLeaderboardCompleted(long result, long responseCode, string[] headers, byte[] body)
    {
        _isLoadingLeaderboard = false;

        if (responseCode == 200)
        {
            string json = System.Text.Encoding.UTF8.GetString(body);
            using var doc = JsonDocument.Parse(json);

            _leaderboardEntries.Clear();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var entry = new LeaderboardItem(
                    Id: el.GetProperty("id").GetGuid(),
                    PlayerName: el.GetProperty("playerName").GetString() ?? "Unknown",
                    Score: el.GetProperty("score").GetInt32(),
                    TotalTicks: el.GetProperty("totalTicks").GetInt32(),
                    CreatedAtUtc: el.GetProperty("createdAtUtc").GetDateTime()
                );
                _leaderboardEntries.Add(entry);
            }
        }
        else
        {
            GD.PrintErr($"Failed to load leaderboard: HTTP {responseCode}");
        }
    }
}