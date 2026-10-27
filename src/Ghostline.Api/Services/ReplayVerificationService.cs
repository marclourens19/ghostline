using Ghostline.Simulation;

namespace Ghostline.Api.Services;

public record VerificationResult(
    bool IsValid,
    int AuthoritativeScore,
    int AuthoritativeTicks,
    string? ErrorMessage
);

public interface IReplayVerificationService
{
    VerificationResult Verify(byte[] replayBytes, int claimedScore);
}

public class ReplayVerificationService : IReplayVerificationService
{
    public VerificationResult Verify(byte[] replayBytes, int claimedScore)
    {
        // 1. Attempt to deserialize binary replay payload
        ReplayFile replay;
        try
        {
            replay = ReplayFile.Deserialize(replayBytes);
        }
        catch (Exception ex)
        {
            return new VerificationResult(
                IsValid: false,
                AuthoritativeScore: 0,
                AuthoritativeTicks: 0,
                ErrorMessage: $"Replay deserialization failed: {ex.Message}"
            );
        }

        // 2. Validate input sequence integrity
        if (replay.Inputs.Count == 0)
        {
            return new VerificationResult(
                IsValid: false,
                AuthoritativeScore: 0,
                AuthoritativeTicks: 0,
                ErrorMessage: "Replay contains zero input frames."
            );
        }

        // Verify that ticks are strictly contiguous (Tick 0, 1, 2, 3...)
        for (int i = 0; i < replay.Inputs.Count; i++)
        {
            if (replay.Inputs[i].Tick != i)
            {
                return new VerificationResult(
                    IsValid: false,
                    AuthoritativeScore: 0,
                    AuthoritativeTicks: 0,
                    ErrorMessage: $"Non-contiguous tick sequence at index {i}. Expected tick {i}, got {replay.Inputs[i].Tick}."
                );
            }
        }

        // 3. Instantiate authoritative headless simulation with the replay's seed and spawn point
        var sim = new SimulationWorld(
            playerSpawnPosition: replay.Header.SpawnPosition,
            seed: replay.Header.Seed
        );

        // 4. Re-simulate the entire match step by step
        foreach (var input in replay.Inputs)
        {
            sim.Step(input);
        }

        // 5. Compare server-derived authoritative score against client claim
        if (sim.Score != claimedScore)
        {
            return new VerificationResult(
                IsValid: false,
                AuthoritativeScore: sim.Score,
                AuthoritativeTicks: sim.CurrentTick,
                ErrorMessage: $"Score mismatch: Client claimed {claimedScore}, server computed {sim.Score}."
            );
        }

        // Run successfully verified
        return new VerificationResult(
            IsValid: true,
            AuthoritativeScore: sim.Score,
            AuthoritativeTicks: sim.CurrentTick,
            ErrorMessage: null
        );
    }
}