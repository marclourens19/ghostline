using Godot;
using Ghostline.Simulation;
using System.Collections.Generic;

namespace Ghostline.Client;

public partial class GameWorld : Node2D
{
    // Node references
    private Sprite2D _playerVisual = null!;

    // Authoritative headless simulation instance
    private SimulationWorld _simulation = null!;

    // Fixed timestep accumulator to decouple Godot frame rate from simulation ticks
    private double _timeAccumulator;
    private const double SecondsPerTick = 1.0 / SimulationWorld.TicksPerSecond;

    // In-memory replay buffer: captures authoritative run inputs for server verification
    private readonly List<PlayerInput> _recordedInputs = new();
    private bool _showSkillQEffect;

    public override void _Ready()
    {
        _playerVisual = GetNode<Sprite2D>("PlayerVisual");

        // Spawn player near center of default arena (500, 400) screen pixels with a deterministic seed
        var spawnPosition = SimVector2.FromScreenPixels(500f, 400f);
        _simulation = new SimulationWorld(spawnPosition, seed: 1337);

        // Position the visual immediately at spawn
        UpdateVisuals();
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
                         $"Recorded Inputs: {_recordedInputs.Count}";

        DrawString(ThemeDB.FallbackFont, new Vector2(minX + 15, minY + 30), hudText, HorizontalAlignment.Left, -1, 15, Colors.White);
    }

    public override void _Process(double delta)
    {
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
}