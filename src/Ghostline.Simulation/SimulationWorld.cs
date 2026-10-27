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
    public const int PlayerRadius = 12_000;          // 12 visual pixels collision radius
    public const int EnemyRadius = 10_000;           // 10 visual pixels
    public const int EnemySpeedPerTick = 2_000;     // 2 visual pixels per tick (slower than player)
    public const int EnemySpawnCadenceTicks = 120;   // Spawn an enemy every 2 seconds

    // Current simulation tick count (starts at 0 and increments every Step)
    public int CurrentTick { get; private set; }

    // Authoritative state of the player
    public PlayerState Player { get; private set; }

    // Arena boundary configuration
    public ArenaConfig Arena { get; }
    public uint Seed { get; }

    // Private simulation state fields
    private readonly DeterministicRng _rng;
    private readonly List<EnemyState> _enemies = new();
    private int _nextEnemyId = 1;

    // Expose a read-only view of enemies for the client presentation layer and tests
    public IReadOnlyList<EnemyState> Enemies => _enemies;

    // Initializes a fresh simulation world with a defined starting position.
    public SimulationWorld(SimVector2 playerSpawnPosition, ArenaConfig? arena = null, uint seed = 1337)
    {
        CurrentTick = 0;
        Seed = seed;
        _rng = new DeterministicRng(seed);
        Arena = arena ?? ArenaConfig.CreateDefault();
        Player = PlayerState.CreateDefault(playerSpawnPosition);
    }

    // Advances the simulation by exactly one fixed tick using the provided player input.
    public void Step(PlayerInput input)
    {
        // 1. Advance the clock by 1 tick at the start of the step
        CurrentTick++;

        // If the player is dead, the world no longer accepts player movement
        if (!Player.IsAlive)
        {
            return;
        }

        // 2. Calculate Dash Cooldown progression
        // If cooldown is active, tick it down toward 0.
        int currentCooldown = Player.DashCooldownTicks > 0
            ? Player.DashCooldownTicks - 1
            : 0;

        // 3. Check if a dash was triggered on this tick
        bool isDashing = input.Dash && currentCooldown == 0;
        if (isDashing)
        {
            currentCooldown = DashCooldownDuration;
        }

        // 4. Calculate movement delta for this tick
        // Determine base displacement based on directional input (-1, 0, 1)
        int moveDeltaX = input.MoveX * PlayerSpeedPerTick;
        int moveDeltaY = input.MoveY * PlayerSpeedPerTick;

        // If dashing, add burst distance in the direction the player is moving
        if (isDashing)
        {
            moveDeltaX += input.MoveX * DashDistance;
            moveDeltaY += input.MoveY * DashDistance;
        }

        // 5. Compute the new player position
        SimVector2 rawPosition = Player.Position + new SimVector2(moveDeltaX, moveDeltaY);

        // 6. Constrain within arena boundaries (integer clamp)
        SimVector2 clampedPosition = Arena.ClampPosition(rawPosition, PlayerRadius);

        // 7. Periodic Enemy Spawning
        if (CurrentTick % EnemySpawnCadenceTicks == 0)
        {
            SpawnEnemy();
        }

        // 8. Update Enemies & Check Player Collisions
        int playerDamageTaken = 0;

        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyState enemy = _enemies[i];
            if (!enemy.IsAlive) continue;

            // Simple integer pursuit: step towards player position
            int dirX = Math.Sign(clampedPosition.X - enemy.Position.X);
            int dirY = Math.Sign(clampedPosition.Y - enemy.Position.Y);

            SimVector2 newEnemyPos = enemy.Position + new SimVector2(
                dirX * EnemySpeedPerTick,
                dirY * EnemySpeedPerTick
            );

            _enemies[i] = enemy with { Position = newEnemyPos };

            // Check collision with player (Manhattan/Axis check for contact)
            int combinedRadius = PlayerRadius + EnemyRadius;
            int distX = Math.Abs(clampedPosition.X - newEnemyPos.X);
            int distY = Math.Abs(clampedPosition.Y - newEnemyPos.Y);

            if (distX < combinedRadius && distY < combinedRadius)
            {
                playerDamageTaken += 1; // Deal contact damage per tick
            }
        }

        // 9. Update the player state with an immutable snapshot update
        // Used the 'with' expression on the record struct to produce a clean new state
        // Calculate damage and resolve authoritative health state
        int finalHealth = Math.Max(0, Player.Health - playerDamageTaken);
        bool isAlive = finalHealth > 0;

        Player = Player with
        {
            Position = clampedPosition,
            Health = finalHealth,
            IsAlive = isAlive,
            DashCooldownTicks = currentCooldown
        };
    }

    private void SpawnEnemy()
    {
        // Pick a pseudo-random point along the arena boundary
        int spawnX = _rng.NextRange(Arena.MinX + EnemyRadius, Arena.MaxX - EnemyRadius);
        int spawnY = _rng.NextRange(0, 2) == 0
            ? Arena.MinY + EnemyRadius
            : Arena.MaxY - EnemyRadius;

        _enemies.Add(new EnemyState(
            Id: _nextEnemyId++,
            Position: new SimVector2(spawnX, spawnY),
            Health: 30,
            IsAlive: true
        ));
    }
}