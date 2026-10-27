namespace Ghostline.Simulation;

/*
 * A fast, cross-platform deterministic pseudo-random number generator using XorShift32.
 * Guarantees the exact same random sequence across any CPU architecture or operating system.
*/

public class DeterministicRng
{
    private uint _state;

    public DeterministicRng(uint seed)
    {
        // XorShift state must never be 0; fallback to a default non-zero seed if 0 is passed.
        _state = seed == 0 ? 0xDEADBEEF : seed;
    }

    // Generates the next pseudo-random unsigned 32-bit integer.
    public uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    // Returns an integer within a specified range: [minInclusive, maxExclusive).
    public int NextRange(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            return minInclusive;

        uint range = (uint)(maxExclusive - minInclusive);
        return (int)(minInclusive + (NextUInt() % range));
    }
}