namespace Ghostline.Simulation;

// Holds the authoritative state of the player entity at any given tick.

public readonly record struct PlayerState(
    SimVector2 Position,
    int Health,
    int MaxHealth,
    int Shield,
    int ShieldActiveTicks,
    int ShieldCooldownTicks,
    int DashCooldownTicks,
    int AttackCooldownTicks,
    int QCooldownTicks,
    int RCooldownTicks,
    int LaserActiveTicks,
    SimVector2 LaserAimDirection,
    bool IsAlive
)
{
    public static PlayerState CreateDefault(SimVector2 spawnPosition) =>
        new(
            Position: spawnPosition,
            Health: 100,
            MaxHealth: 100,
            Shield: 0,
            ShieldActiveTicks: 0,
            ShieldCooldownTicks: 0,
            DashCooldownTicks: 0,
            AttackCooldownTicks: 0,
            QCooldownTicks: 0,
            RCooldownTicks: 0,
            LaserActiveTicks: 0,
            LaserAimDirection: new SimVector2(1000, 0),
            IsAlive: true
        );
}