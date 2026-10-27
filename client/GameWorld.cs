using Godot;
using Ghostline.Simulation;

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

    public override void _Ready()
    {
        _playerVisual = GetNode<Sprite2D>("PlayerVisual");

        // Spawn player at (300, 300) screen pixels
        var spawnPosition = SimVector2.FromScreenPixels(300f, 300f);
        _simulation = new SimulationWorld(spawnPosition);

        // Position the visual immediately at spawn
        UpdateVisuals();
    }

    public override void _Process(double delta)
    {
        // Accumulate elapsed real time
        _timeAccumulator += delta;

        /* 
         * Run fixed simulation steps whenever enough time has passed.
         * If Godot runs at 144 FPS, it might step 0 times on some frames and 1 time on others.
         * If Godot drops to 30 FPS, it will step 2 times in one frame to catch up.
         */
        while (_timeAccumulator >= SecondsPerTick)
        {
            PlayerInput input = PollInput(_simulation.CurrentTick);
            _simulation.Step(input);
            _timeAccumulator -= SecondsPerTick;
        }

        // Render authoritative simulation state to screen
        UpdateVisuals();
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

        return new PlayerInput(
            Tick: currentTick,
            MoveX: moveX,
            MoveY: moveY,
            Dash: dash,
            Attack: false
        );
    }

    private void UpdateVisuals()
    {
        var (screenX, screenY) = _simulation.Player.Position.ToScreenPixels();
        _playerVisual.Position = new Vector2(screenX, screenY);
    }
}