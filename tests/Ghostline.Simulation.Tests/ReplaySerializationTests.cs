using System.IO;
using Xunit;
using Ghostline.Api.Services;

namespace Ghostline.Simulation.Tests;

public class ReplaySerializationTests
{
    [Fact]
    public void ReplayFile_BinaryRoundtrip_PreservesAllHeaderAndInputData()
    {
        // ARRANGE: Construct a test replay with simulated inputs
        var spawnPoint = new SimVector2(350_000, 450_000);
        var inputs = new List<PlayerInput>
        {
            new(Tick: 0, MoveX: 1, MoveY: 0, Dash: false, Attack: true, SkillQ: false, SkillE: false, SkillR: false, AimTargetX: 500_000, AimTargetY: 450_000),
            new(Tick: 1, MoveX: 0, MoveY: -1, Dash: true, Attack: false, SkillQ: true, SkillE: false, SkillR: false, AimTargetX: 510_000, AimTargetY: 440_000),
            new(Tick: 2, MoveX: -1, MoveY: 1, Dash: false, Attack: false, SkillQ: false, SkillE: true, SkillR: true, AimTargetX: 520_000, AimTargetY: 430_000),
        };

        var originalHeader = new ReplayHeader(ReplayFile.CurrentVersion, Seed: 98765, spawnPoint, inputs.Count);
        var originalReplay = new ReplayFile(originalHeader, inputs);

        // ACT: Serialize to binary and deserialize back
        byte[] payload = originalReplay.Serialize();
        ReplayFile deserializedReplay = ReplayFile.Deserialize(payload);

        // ASSERT: Header verification
        Assert.Equal(originalReplay.Header.Version, deserializedReplay.Header.Version);
        Assert.Equal(originalReplay.Header.Seed, deserializedReplay.Header.Seed);
        Assert.Equal(originalReplay.Header.SpawnPosition, deserializedReplay.Header.SpawnPosition);
        Assert.Equal(originalReplay.Header.InputCount, deserializedReplay.Header.InputCount);

        // ASSERT: Input frames match identically
        Assert.Equal(originalReplay.Inputs.Count, deserializedReplay.Inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(originalReplay.Inputs[i], deserializedReplay.Inputs[i]);
        }
    }

    [Fact]
    public void Deserialization_ThrowsInvalidDataException_WhenMagicBytesAreCorrupted()
    {
        // ARRANGE: Create valid bytes then corrupt the first magic byte
        var spawnPoint = new SimVector2(100_000, 100_000);
        var replay = new ReplayFile(new ReplayHeader(1, 1337, spawnPoint, 0), Array.Empty<PlayerInput>());
        byte[] payload = replay.Serialize();

        payload[0] = (byte)'X'; // Corrupt 'G' -> 'X'

        // ACT & ASSERT: Must reject payload with InvalidDataException
        Assert.Throws<InvalidDataException>(() => ReplayFile.Deserialize(payload));
    }

    [Fact]
    public void ReplayedSimulation_MatchesOriginalSimulationAuthoritativeState()
    {
        // ARRANGE: Run an authoritative live simulation session
        uint seed = 2026;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var liveSim = new SimulationWorld(spawnPoint, seed: seed);
        var recordedInputs = new List<PlayerInput>();

        for (int tick = 0; tick < 240; tick++)
        {
            sbyte moveX = tick < 120 ? (sbyte)1 : (sbyte)-1;
            bool attack = (tick % 15 == 0);
            bool skillQ = (tick == 120);
            bool skillE = (tick == 60);

            var input = new PlayerInput(
                Tick: tick,
                MoveX: moveX,
                MoveY: 0,
                Dash: tick == 30,
                Attack: attack,
                SkillQ: skillQ,
                SkillE: skillE,
                SkillR: false,
                AimTargetX: 600_000,
                AimTargetY: 400_000
            );

            recordedInputs.Add(input);
            liveSim.Step(input);
        }

        // ACT: Pack into ReplayFile, serialize to bytes, and simulate from deserialized data
        var header = new ReplayHeader(ReplayFile.CurrentVersion, seed, spawnPoint, recordedInputs.Count);
        var replayFile = new ReplayFile(header, recordedInputs);
        byte[] binaryData = replayFile.Serialize();

        ReplayFile loadedReplay = ReplayFile.Deserialize(binaryData);

        var verifySim = new SimulationWorld(loadedReplay.Header.SpawnPosition, seed: loadedReplay.Header.Seed);
        foreach (var input in loadedReplay.Inputs)
        {
            verifySim.Step(input);
        }

        // ASSERT: Server re-simulation produces the exact same final state
        Assert.Equal(liveSim.CurrentTick, verifySim.CurrentTick);
        Assert.Equal(liveSim.Score, verifySim.Score);
        Assert.Equal(liveSim.Player.Position, verifySim.Player.Position);
        Assert.Equal(liveSim.Player.Health, verifySim.Player.Health);
        Assert.Equal(liveSim.Player.Shield, verifySim.Player.Shield);
        Assert.Equal(liveSim.Enemies.Count, verifySim.Enemies.Count);
    }

    [Fact]
    public void ReplayVerification_ApprovesMatchingScoreAndValidInputs()
    {
        // ARRANGE: Generate an authoritative match run
        uint seed = 777;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var sim = new SimulationWorld(spawnPoint, seed: seed);
        var inputs = new List<PlayerInput>();

        // 120 ticks: Enemy spawns at 120 and gets hit by Q shockwave
        for (int tick = 0; tick < 120; tick++)
        {
            var input = new PlayerInput(tick, 0, 0, false, false, false, false, false, 0, 0);
            inputs.Add(input);
            sim.Step(input);
        }

        var attackInput = new PlayerInput(120, 0, 0, false, false, SkillQ: true, false, false, sim.Enemies[0].Position.X, sim.Enemies[0].Position.Y);
        inputs.Add(attackInput);
        sim.Step(attackInput);

        int actualScore = sim.Score;
        var replay = new ReplayFile(new ReplayHeader(1, seed, spawnPoint, inputs.Count), inputs);
        byte[] payload = replay.Serialize();

        // ACT: Run through verification service
        var verifier = new ReplayVerificationService();
        var result = verifier.Verify(payload, claimedScore: actualScore);

        // ASSERT:
        Assert.True(result.IsValid);
        Assert.Equal(actualScore, result.AuthoritativeScore);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ReplayVerification_RejectsCheatedScoreClaim()
    {
        // ARRANGE: Live run finishes with actual score of 0
        uint seed = 777;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var inputs = new List<PlayerInput>
        {
            new(0, 0, 0, false, false, false, false, false, 0, 0),
            new(1, 0, 0, false, false, false, false, false, 0, 0)
        };

        var replay = new ReplayFile(new ReplayHeader(1, seed, spawnPoint, inputs.Count), inputs);
        byte[] payload = replay.Serialize();

        // ACT: Client claims a fake score of 999999
        var verifier = new ReplayVerificationService();
        var result = verifier.Verify(payload, claimedScore: 999_999);

        // ASSERT: Must fail validation with score mismatch
        Assert.False(result.IsValid);
        Assert.Equal(0, result.AuthoritativeScore);
        Assert.Contains("Score mismatch", result.ErrorMessage);
    }
}