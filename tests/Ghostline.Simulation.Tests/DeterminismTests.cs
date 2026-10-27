using Xunit;

namespace Ghostline.Simulation.Tests;

public class DeterminismTests
{
    [Fact]
    public void TwoSimulations_WithIdenticalInputs_ProduceIdenticalState()
    {
        /* 
         * ARRANGE:
         * Create two completely independent simulation instances with the same spawn point.
         * This simulates:
         * Instance A: Running locally inside Godot on the player's computer.
         * Instance B: Running inside our ASP.NET Core backend / Docker worker.
         */
        var spawnPoint = new SimVector2(100_000, 100_000); // (100, 100) in visual pixels
        var simA = new SimulationWorld(spawnPoint);
        var simB = new SimulationWorld(spawnPoint);

        // Generate a sequence of 120 ticks of inputs (2 seconds of recorded gameplay)
        // Moving right for 60 ticks, triggering a dash at tick 30, then moving down for 60 ticks.
        var inputs = new PlayerInput[120];
        for (int tick = 0; tick < 120; tick++)
        {
            sbyte moveX = tick < 60 ? (sbyte)1 : (sbyte)0;
            sbyte moveY = tick >= 60 ? (sbyte)1 : (sbyte)0;
            bool dash = (tick == 30);

            inputs[tick] = new PlayerInput(
                Tick: tick,
                MoveX: moveX,
                MoveY: moveY,
                Dash: dash,
                Attack: false
            );
        }

        // ACT:
        // Run the input sequence through both independent simulation worlds
        foreach (var input in inputs)
        {
            simA.Step(input);
            simB.Step(input);
        }

        // ASSERT:
        // Both worlds must land on the exact same tick and coordinates down to the millipixel
        Assert.Equal(120, simA.CurrentTick);
        Assert.Equal(simA.CurrentTick, simB.CurrentTick);
        Assert.Equal(simA.Player.Position, simB.Player.Position);
        Assert.Equal(simA.Player.DashCooldownTicks, simB.Player.DashCooldownTicks);
    }

    [Fact]
    public void PlayerMovement_IsConstrainedByArenaBoundaries()
    {
        // ARRANGE:
        // Place player near the right wall and attempt to walk past it for 300 ticks
        var arena = ArenaConfig.CreateDefault();
        var spawnPoint = new SimVector2(arena.MaxX - 5_000, 300_000);
        var sim = new SimulationWorld(spawnPoint, arena);

        // ACT:
        // Continuously send Right (MoveX = 1) inputs
        for (int tick = 0; tick < 300; tick++)
        {
            sim.Step(new PlayerInput(tick, MoveX: 1, MoveY: 0, Dash: false, Attack: false));
        }

        // ASSERT:
        // Position X must not exceed MaxX - PlayerRadius
        int expectedMaxAllowedX = arena.MaxX - SimulationWorld.PlayerRadius;
        Assert.Equal(expectedMaxAllowedX, sim.Player.Position.X);
    }

    [Fact]
    public void SeededEnemySpawns_ProduceIdenticalStateAcrossInstances()
    {
        // ARRANGE: Two simulation instances running identical seeds
        uint seed = 42;
        var spawnPoint = new SimVector2(500_000, 400_000);
        var simA = new SimulationWorld(spawnPoint, seed: seed);
        var simB = new SimulationWorld(spawnPoint, seed: seed);

        // ACT: Run 300 ticks with idle inputs to allow multiple enemy spawn waves
        for (int tick = 0; tick < 300; tick++)
        {
            var input = new PlayerInput(tick, 0, 0, false, false);
            simA.Step(input);
            simB.Step(input);
        }

        // ASSERT: Enemies count and positions must match identically
        Assert.True(simA.Enemies.Count > 0);
        Assert.Equal(simA.Enemies.Count, simB.Enemies.Count);

        for (int i = 0; i < simA.Enemies.Count; i++)
        {
            Assert.Equal(simA.Enemies[i].Id, simB.Enemies[i].Id);
            Assert.Equal(simA.Enemies[i].Position, simB.Enemies[i].Position);
            Assert.Equal(simA.Enemies[i].Health, simB.Enemies[i].Health);
        }
    }
}