namespace Ghostline.Simulation;

/*  
 * Represents the player's intentional actions during a specific simulation tick.
 * Recorded on the client and sent to the server for replay re-simulation.
 */
public readonly record struct PlayerInput(
    int Tick,
    sbyte MoveX,    // -1 (Left), 0 (Neutral), 1 (Right)
    sbyte MoveY,    // -1 (Up),   0 (Neutral), 1 (Down)
    bool Dash,
    bool Attack
);