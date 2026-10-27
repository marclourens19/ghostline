namespace Ghostline.Simulation;

/*
 * Authoritative simulation container.
 * Executes discrete game ticks using deterministic integer arithmetic.
 * Both the Godot client and the backend API run this identical class.
 */
public class SimulationWorld
{
    // Simulation configuration constants
    public const int TicksPerSecond = 60;
    public const int PlayerSpeedPerTick = 5_000;    // 5 visual pixels per tick (300 pixels/sec)
    public const int DashDistance = 25_000;          // 25 visual pixels instant burst
    public const int DashCooldownDuration = 60;      // 60 ticks = 1.0 second cooldown

    // Current simulation tick count (starts at 0 and increments every Step)
    public int CurrentTick { get; private set; }

    // Authoritative state of the player
    public PlayerState Player { get; private set; }

    // Initializes a fresh simulation world with a defined starting position.
    public SimulationWorld(SimVector2 playerSpawnPosition)
    {
        CurrentTick = 0;
        Player = PlayerState.CreateDefault(playerSpawnPosition);
    }

    // Advances the simulation by exactly one fixed tick using the provided player input.
    public void Step(PlayerInput input)
    {
        // If the player is dead, the world no longer accepts player movement
        if (!Player.IsAlive)
        {
            CurrentTick++;
            return;
        }

        // 1. Calculate Dash Cooldown progression
        // If cooldown is active, tick it down toward 0.
        int currentCooldown = Player.DashCooldownTicks > 0
            ? Player.DashCooldownTicks - 1
            : 0;

        // 2. Check if a dash was triggered on this tick
        bool isDashing = input.Dash && currentCooldown == 0;
        if (isDashing)
        {
            currentCooldown = DashCooldownDuration;
        }

        // 3. Calculate movement delta for this tick
        // Determine base displacement based on directional input (-1, 0, 1)
        int moveDeltaX = input.MoveX * PlayerSpeedPerTick;
        int moveDeltaY = input.MoveY * PlayerSpeedPerTick;

        // If dashing, add burst distance in the direction the player is moving
        if (isDashing)
        {
            moveDeltaX += input.MoveX * DashDistance;
            moveDeltaY += input.MoveY * DashDistance;
        }

        // 4. Compute the new player position
        SimVector2 newPosition = Player.Position + new SimVector2(moveDeltaX, moveDeltaY);

        // 5. Update the player state with an immutable snapshot update
        // Used the 'with' expression on the record struct to produce a clean new state
        Player = Player with
        {
            Position = newPosition,
            DashCooldownTicks = currentCooldown
        };

        // 6. Advance the clock by 1 tick
        CurrentTick++;
    }
}