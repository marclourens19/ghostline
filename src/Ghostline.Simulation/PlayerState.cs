namespace Ghostline.Simulation;

// Holds the authoritative state of the player entity at any given tick.

public readonly record struct PlayerState(
    SimVector2 Position,
    int Health,
    int MaxHealth,
    int DashCooldownTicks,
    bool IsAlive
)
{
    public static PlayerState CreateDefault(SimVector2 spawnPosition) =>
        new(
            Position: spawnPosition,
            Health: 100,
            MaxHealth: 100,
            DashCooldownTicks: 0,
            IsAlive: true
        );
}