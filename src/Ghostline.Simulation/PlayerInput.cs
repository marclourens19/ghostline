namespace Ghostline.Simulation;

/*  
 * Represents the player's intentional actions during a specific simulation tick.
 * Recorded on the client and sent to the server for replay re-simulation.
 */
public readonly record struct PlayerInput(
    int Tick,
    sbyte MoveX,        // -1 (Left), 0 (Neutral), 1 (Right)
    sbyte MoveY,        // -1 (Up),   0 (Neutral), 1 (Down)
    bool Dash,          // Space
    bool Attack,        // Left Click / Bullet fire
    bool SkillQ,        // Wide Radial AoE
    bool SkillE,        // 15 HP Shield
    bool SkillR,        // 5-second Continuous Laser Beam
    int AimTargetX,     // Mouse cursor X in millipixels
    int AimTargetY      // Mouse cursor Y in millipixels
);