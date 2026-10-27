namespace Ghostline.Simulation;

// Authoritative state of a single enemy entity.

public readonly record struct EnemyState(
    int Id,
    SimVector2 Position,
    int Health,
    bool IsAlive
);