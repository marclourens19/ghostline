namespace Ghostline.Simulation;

// Defines the spatial boundaries of the play arena in millipixels.
public readonly record struct ArenaConfig(
    int MinX,
    int MaxX,
    int MinY,
    int MaxY
)
{
    /*
     * Default arena: 800x600 visual pixels centered on the screen,
     * with borders at (100, 100) to (900, 700).
     */
    public static ArenaConfig CreateDefault() =>
        new(
            MinX: 100_000,
            MaxX: 900_000,
            MinY: 100_000,
            MaxY: 700_000
        );

    // Clamps an entity's center point so its bounding radius remains strictly inside the arena.
    public SimVector2 ClampPosition(SimVector2 position, int radiusMillipixels)
    {
        int clampedX = Math.Clamp(position.X, MinX + radiusMillipixels, MaxX - radiusMillipixels);
        int clampedY = Math.Clamp(position.Y, MinY + radiusMillipixels, MaxY - radiusMillipixels);
        return new SimVector2(clampedX, clampedY);
    }
}