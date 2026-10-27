namespace Ghostline.Simulation;

// Authoritative state of a moving projectile bullet.

public readonly record struct ProjectileState(
    int Id,
    SimVector2 Position,
    SimVector2 Velocity,
    int RemainingLifeTicks,
    int Damage,
    bool IsAlive
);