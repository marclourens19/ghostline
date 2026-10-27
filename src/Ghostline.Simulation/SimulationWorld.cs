namespace Ghostline.Simulation;

using System;
using System.Collections.Generic;

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

    // Bullet (Normal Attack) constants
    public const int BulletSpeedPerTick = 12_000;   // 12 visual pixels per tick
    public const int BulletRadius = 4_000;            // 4 visual pixels
    public const int BulletDamage = 30;
    public const int BulletLifetimeTicks = 90;       // 1.5 seconds life span
    public const int AttackCooldownDuration = 15;    // 0.25 second cooldown

    // Skill Q (Wide Radial AoE) constants
    public const int SkillQRadius = 75_000;          // 75 visual pixels
    public const int SkillQDamage = 45;
    public const int SkillQCooldownDuration = 300;   // 5.0 seconds cooldown (5 * 60)

    // Skill E (Shield) constants
    public const int ShieldAmount = 15;
    public const int ShieldDurationTicks = 300;      // 5.0 seconds active
    public const int ShieldCooldownDuration = 1500;  // 25.0 seconds cooldown (25 * 60)

    // Skill R (Laser Beam) constants
    public const int LaserDurationTicks = 300;       // 5.0 seconds continuous active
    public const int LaserCooldownDuration = 2700;   // 45.0 seconds cooldown (45 * 60)
    public const int LaserLength = 1_000_000;        // 1000 visual pixels span
    public const int LaserBeamWidth = 16_000;        // 16 visual pixels beam half-width
    public const int LaserDamagePerTick = 2;         // Continuous burn damage

    public const int ScorePerKill = 100;

    // Enemy constants
    public const int EnemyRadius = 10_000;           // 10 visual pixels
    public const int EnemySpeedPerTick = 2_000;      // 2 visual pixels per tick (slower than player)
    public const int EnemySpawnCadenceTicks = 120;   // Spawn an enemy every 2 seconds

    // Current simulation tick count (starts at 0 and increments every Step)
    public int CurrentTick { get; private set; }

    // Authoritative state of the player
    public PlayerState Player { get; private set; }

    // Arena boundary configuration
    public ArenaConfig Arena { get; }
    public uint Seed { get; }

    // Combat & scoring state
    public int Score { get; private set; }

    // Private simulation state fields
    private readonly DeterministicRng _rng;
    private readonly List<EnemyState> _enemies = new();
    private readonly List<ProjectileState> _projectiles = new();
    private int _nextEnemyId = 1;
    private int _nextProjectileId = 1;

    // Expose a read-only view of entities for the client presentation layer and tests
    public IReadOnlyList<EnemyState> Enemies => _enemies;
    public IReadOnlyList<ProjectileState> Projectiles => _projectiles;

    // Initializes a fresh simulation world with a defined starting position.
    public SimulationWorld(SimVector2 playerSpawnPosition, ArenaConfig? arena = null, uint seed = 1337)
    {
        CurrentTick = 0;
        Score = 0;
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

        // 2. Calculate Dash, Attack, and Skill Cooldown progressions
        int currentDashCooldown = Player.DashCooldownTicks > 0
            ? Player.DashCooldownTicks - 1
            : 0;

        int attackCooldown = Player.AttackCooldownTicks > 0
            ? Player.AttackCooldownTicks - 1
            : 0;

        int qCooldown = Player.QCooldownTicks > 0
            ? Player.QCooldownTicks - 1
            : 0;

        int eCooldown = Player.ShieldCooldownTicks > 0
            ? Player.ShieldCooldownTicks - 1
            : 0;

        int rCooldown = Player.RCooldownTicks > 0
            ? Player.RCooldownTicks - 1
            : 0;

        // Decrement active ability durations
        int shieldActive = Player.ShieldActiveTicks > 0
            ? Player.ShieldActiveTicks - 1
            : 0;
        int currentShield = shieldActive > 0 ? Player.Shield : 0;

        int laserActive = Player.LaserActiveTicks > 0
            ? Player.LaserActiveTicks - 1
            : 0;
        SimVector2 laserDirection = Player.LaserAimDirection;

        // 3. Check if a dash was triggered on this tick
        bool isDashing = input.Dash && currentDashCooldown == 0;
        if (isDashing)
        {
            currentDashCooldown = DashCooldownDuration;
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

        // Compute aim direction vector using deterministic integer math
        int diffX = input.AimTargetX - clampedPosition.X;
        int diffY = input.AimTargetY - clampedPosition.Y;
        long distSquared = ((long)diffX * diffX) + ((long)diffY * diffY);
        int distance = IntegerSqrt(distSquared);
        if (distance == 0) distance = 1;

        int normX = (int)(((long)diffX * 1000) / distance);
        int normY = (int)(((long)diffY * 1000) / distance);

        // 7. Check if Skill E (Shield) was triggered on this tick
        if (input.SkillE && eCooldown == 0)
        {
            currentShield = ShieldAmount;
            shieldActive = ShieldDurationTicks;
            eCooldown = ShieldCooldownDuration;
        }

        // 8. Check if Skill R (Laser Beam) was triggered on this tick
        if (input.SkillR && rCooldown == 0)
        {
            laserActive = LaserDurationTicks;
            rCooldown = LaserCooldownDuration;
            laserDirection = new SimVector2(normX, normY);
        }
        else if (laserActive > 0)
        {
            // Laser tracks aim direction while active
            laserDirection = new SimVector2(normX, normY);
        }

        // 9. Check if Normal Attack (Bullet) was triggered on this tick
        if (input.Attack && attackCooldown == 0)
        {
            attackCooldown = AttackCooldownDuration;

            int velX = (int)(((long)normX * BulletSpeedPerTick) / 1000);
            int velY = (int)(((long)normY * BulletSpeedPerTick) / 1000);

            _projectiles.Add(new ProjectileState(
                Id: _nextProjectileId++,
                Position: clampedPosition,
                Velocity: new SimVector2(velX, velY),
                RemainingLifeTicks: BulletLifetimeTicks,
                Damage: BulletDamage,
                IsAlive: true
            ));
        }

        // 10. Check if Skill Q (Wide Radial AoE) was triggered on this tick
        bool executedSkillQ = input.SkillQ && qCooldown == 0;
        if (executedSkillQ)
        {
            qCooldown = SkillQCooldownDuration;
        }

        // 11. Periodic Enemy Spawning
        if (CurrentTick % EnemySpawnCadenceTicks == 0)
        {
            SpawnEnemy();
        }

        // 12. Update Bullets and Bullet-Enemy Collisions
        for (int pIdx = _projectiles.Count - 1; pIdx >= 0; pIdx--)
        {
            var proj = _projectiles[pIdx];
            if (!proj.IsAlive || proj.RemainingLifeTicks <= 0)
            {
                _projectiles.RemoveAt(pIdx);
                continue;
            }

            SimVector2 nextPos = proj.Position + proj.Velocity;
            bool hitTarget = false;

            for (int eIdx = 0; eIdx < _enemies.Count; eIdx++)
            {
                var enemy = _enemies[eIdx];
                if (!enemy.IsAlive) continue;

                int hitDist = EnemyRadius + BulletRadius;
                long dx = nextPos.X - enemy.Position.X;
                long dy = nextPos.Y - enemy.Position.Y;
                long distSq = (dx * dx) + (dy * dy);
                long hitDistSq = (long)hitDist * hitDist;

                if (distSq <= hitDistSq)
                {
                    hitTarget = true;
                    int newHp = enemy.Health - proj.Damage;
                    if (newHp <= 0)
                    {
                        Score += ScorePerKill;
                        _enemies[eIdx] = enemy with { IsAlive = false, Health = 0 };
                    }
                    else
                    {
                        _enemies[eIdx] = enemy with { Health = newHp };
                    }
                    break;
                }
            }

            if (hitTarget)
            {
                _projectiles.RemoveAt(pIdx);
            }
            else
            {
                _projectiles[pIdx] = proj with
                {
                    Position = nextPos,
                    RemainingLifeTicks = proj.RemainingLifeTicks - 1
                };
            }
        }

        // 13. Update Enemies, Skills (Q & R Laser) & Check Player Collisions
        int incomingDamage = 0;

        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            EnemyState enemy = _enemies[i];
            if (!enemy.IsAlive)
            {
                _enemies.RemoveAt(i);
                continue;
            }

            // Skill Q wide shockwave damage check
            if (executedSkillQ)
            {
                int qDistX = Math.Abs(clampedPosition.X - enemy.Position.X);
                int qDistY = Math.Abs(clampedPosition.Y - enemy.Position.Y);
                if (qDistX <= SkillQRadius && qDistY <= SkillQRadius)
                {
                    int updatedHp = enemy.Health - SkillQDamage;
                    if (updatedHp <= 0)
                    {
                        Score += ScorePerKill;
                        _enemies.RemoveAt(i);
                        continue;
                    }
                    enemy = enemy with { Health = updatedHp };
                }
            }

            // Skill R continuous piercing laser damage check
            if (laserActive > 0)
            {
                int toEnemyX = enemy.Position.X - clampedPosition.X;
                int toEnemyY = enemy.Position.Y - clampedPosition.Y;

                long dot = ((long)toEnemyX * laserDirection.X + (long)toEnemyY * laserDirection.Y) / 1000;

                if (dot > 0 && dot <= LaserLength)
                {
                    long perpX = toEnemyX - ((dot * laserDirection.X) / 1000);
                    long perpY = toEnemyY - ((dot * laserDirection.Y) / 1000);
                    long perpDistSq = perpX * perpX + perpY * perpY;

                    long maxAllowedDist = LaserBeamWidth + EnemyRadius;
                    if (perpDistSq <= maxAllowedDist * maxAllowedDist)
                    {
                        int laserHp = enemy.Health - LaserDamagePerTick;
                        if (laserHp <= 0)
                        {
                            Score += ScorePerKill;
                            _enemies.RemoveAt(i);
                            continue;
                        }
                        enemy = enemy with { Health = laserHp };
                    }
                }
            }

            // Simple integer pursuit: step towards player position
            int dirX = Math.Sign(clampedPosition.X - enemy.Position.X);
            int dirY = Math.Sign(clampedPosition.Y - enemy.Position.Y);

            SimVector2 newEnemyPos = enemy.Position + new SimVector2(
                dirX * EnemySpeedPerTick,
                dirY * EnemySpeedPerTick
            );

            _enemies[i] = enemy with { Position = newEnemyPos };

            // Check collision with player (True circular distance check)
            int combinedRadius = PlayerRadius + EnemyRadius;
            long contactDx = clampedPosition.X - newEnemyPos.X;
            long contactDy = clampedPosition.Y - newEnemyPos.Y;
            long contactDistSq = (contactDx * contactDx) + (contactDy * contactDy);
            long combinedRadiusSq = (long)combinedRadius * combinedRadius;

            if (contactDistSq < combinedRadiusSq)
            {
                incomingDamage += 1; // Deal contact damage per tick
            }
        }

        // Apply incoming damage: Shield absorbs damage before player health
        int damageToHealth = incomingDamage;
        if (currentShield > 0 && incomingDamage > 0)
        {
            if (currentShield >= incomingDamage)
            {
                currentShield -= incomingDamage;
                damageToHealth = 0;
            }
            else
            {
                damageToHealth = incomingDamage - currentShield;
                currentShield = 0;
            }
        }

        // 14. Update the player state with an immutable snapshot update
        // Used the 'with' expression on the record struct to produce a clean new state
        // Calculate damage and resolve authoritative health state
        int finalHealth = Math.Max(0, Player.Health - damageToHealth);
        bool isAlive = finalHealth > 0;

        Player = Player with
        {
            Position = clampedPosition,
            Health = finalHealth,
            Shield = currentShield,
            ShieldActiveTicks = shieldActive,
            ShieldCooldownTicks = eCooldown,
            DashCooldownTicks = currentDashCooldown,
            AttackCooldownTicks = attackCooldown,
            QCooldownTicks = qCooldown,
            RCooldownTicks = rCooldown,
            LaserActiveTicks = laserActive,
            LaserAimDirection = laserDirection,
            IsAlive = isAlive
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

    // Deterministic integer square root (Newton-Raphson)
    private static int IntegerSqrt(long val)
    {
        if (val <= 0) return 0;
        long x0 = val / 2;
        if (x0 == 0) return 1;
        long x1 = (x0 + val / x0) / 2;
        while (x1 < x0)
        {
            x0 = x1;
            x1 = (x0 + val / x0) / 2;
        }
        return (int)x0;
    }
}