namespace Ghostline.Simulation;

/* 
 * A 2D integer vector representing coordinates in millipixels (1000 units = 1 pixel).
 * Avoids floating-point divergence across different hardware platforms.
 */ 
public readonly record struct SimVector2(int X, int Y)
{
    public static readonly SimVector2 Zero = new(0, 0);

    public static SimVector2 operator +(SimVector2 a, SimVector2 b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static SimVector2 operator -(SimVector2 a, SimVector2 b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static SimVector2 operator *(SimVector2 a, int scalar) =>
        new(a.X * scalar, a.Y * scalar);

    // Helper to convert simulation units back to screen pixels for Godot rendering.
    public (float ScreenX, float ScreenY) ToScreenPixels() =>
        (X / 1000f, Y / 1000f);

    // Helper to create a SimVector2 from floating-point screen coordinates.
    public static SimVector2 FromScreenPixels(float screenX, float screenY) =>
        new((int)(screenX * 1000f), (int)(screenY * 1000f));
}