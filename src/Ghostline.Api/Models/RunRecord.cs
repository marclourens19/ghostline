namespace Ghostline.Api.Models;

// Database entity representing a completed gameplay run and its verification verdict.
public class RunRecord
{
    // Unique identifier for the run (Primary Key)
    public Guid Id { get; set; } = Guid.NewGuid();

    // Display name of the submitting player
    public string PlayerName { get; set; } = string.Empty;

    // Score claimed by the client
    public int ClaimedScore { get; set; }

    // Authoritative score calculated by the server during re-simulation
    public int VerifiedScore { get; set; }

    // Total duration of the run measured in fixed ticks
    public int TotalTicks { get; set; }

    // PRNG seed used for the match
    public uint Seed { get; set; }

    // True only if headless server re-simulation matched the claimed score and state
    public bool IsVerified { get; set; }

    // Reason for rejection if verification failed (null if verified successfully)
    public string? VerificationError { get; set; }

    // Raw binary replay payload (GHST format) stored for leaderboard playback
    public byte[] ReplayData { get; set; } = Array.Empty<byte>();

    // Timestamp when the run was submitted
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}