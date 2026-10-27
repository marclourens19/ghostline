using System;
using System.IO;
using System.Collections.Generic;

namespace Ghostline.Simulation;

// Metadata header stored at the beginning of every binary replay payload.
public readonly record struct ReplayHeader(
    byte Version,
    uint Seed,
    SimVector2 SpawnPosition,
    int InputCount
);

// Encapsulates a complete recorded match: initial state configuration and chronological inputs.
// Provides deterministic binary serialization and deserialization.
public class ReplayFile
{
    // Magic bytes: ASCII "GHST"
    public static readonly byte[] MagicBytes = new byte[] { (byte)'G', (byte)'H', (byte)'S', (byte)'T' };
    public const byte CurrentVersion = 1;

    // Header is 21 bytes; each input frame is 15 bytes
    public const int HeaderByteSize = 21;
    public const int FrameByteSize = 15;
    public const int MaxPayloadBytes = 5 * 1024 * 1024; // 5 MB max payload size
    public const int MaxInputFrames = 432_000;          // Max ~2 hours at 60 FPS

    // Action bitmask constants
    public const byte ActionFlagDash = 1 << 0; // 0000 0001
    public const byte ActionFlagAttack = 1 << 1; // 0000 0010
    public const byte ActionFlagSkillQ = 1 << 2; // 0000 0100
    public const byte ActionFlagSkillE = 1 << 3; // 0000 1000
    public const byte ActionFlagSkillR = 1 << 4; // 0001 0000

    public ReplayHeader Header { get; }
    public IReadOnlyList<PlayerInput> Inputs { get; }

    public ReplayFile(ReplayHeader header, IReadOnlyList<PlayerInput> inputs)
    {
        Header = header;
        Inputs = inputs;
    }

    // Serializes this replay instance to a byte array.
    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteTo(writer);
        return stream.ToArray();
    }

    // Writes the replay binary representation into an open BinaryWriter stream.
    public void WriteTo(BinaryWriter writer)
    {
        // 1. Write Header (21 bytes)
        writer.Write(MagicBytes);
        writer.Write(Header.Version);
        writer.Write(Header.Seed);
        writer.Write(Header.SpawnPosition.X);
        writer.Write(Header.SpawnPosition.Y);
        writer.Write(Inputs.Count);

        // 2. Write Input Frames (15 bytes per frame)
        for (int i = 0; i < Inputs.Count; i++)
        {
            PlayerInput input = Inputs[i];

            // Pack boolean states into a single byte bitmask
            byte actions = 0;
            if (input.Dash) actions |= ActionFlagDash;
            if (input.Attack) actions |= ActionFlagAttack;
            if (input.SkillQ) actions |= ActionFlagSkillQ;
            if (input.SkillE) actions |= ActionFlagSkillE;
            if (input.SkillR) actions |= ActionFlagSkillR;

            writer.Write(input.Tick);
            writer.Write(input.MoveX);
            writer.Write(input.MoveY);
            writer.Write(actions);
            writer.Write(input.AimTargetX);
            writer.Write(input.AimTargetY);
        }
    }

    // Deserializes a binary replay payload from a byte array.
    public static ReplayFile Deserialize(byte[] payload)
    {
        if (payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes)
            throw new InvalidDataException("Replay payload is empty or exceeds maximum allowed size.");

        using var ms = new MemoryStream(payload);
        using var reader = new BinaryReader(ms);

        // Delegate to ReadFrom so all validation and parsing rules stay consolidated in one place
        return ReadFrom(reader);
    }

    // Reads and validates a replay payload from an active BinaryReader stream.
    public static ReplayFile ReadFrom(BinaryReader reader)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        try
        {
            // 1. Validate Magic Bytes
            byte[] magic = reader.ReadBytes(4);
            if (magic.Length != 4 ||
                magic[0] != MagicBytes[0] ||
                magic[1] != MagicBytes[1] ||
                magic[2] != MagicBytes[2] ||
                magic[3] != MagicBytes[3])
            {
                throw new InvalidDataException("Invalid replay file signature: Header magic bytes mismatch.");
            }

            // 2. Validate Version
            byte version = reader.ReadByte();
            if (version != CurrentVersion)
            {
                throw new InvalidDataException($"Unsupported replay version: {version}. Expected: {CurrentVersion}.");
            }

            // 3. Read Header Metadata
            uint seed = reader.ReadUInt32();
            int spawnX = reader.ReadInt32();
            int spawnY = reader.ReadInt32();

            if (spawnX < -100_000 || spawnX > 2000_000 || spawnY < -100_000 || spawnY > 2000_000)
            {
                throw new InvalidDataException("Spawn coordinates out of valid bounds.");
            }

            int inputCount = reader.ReadInt32();

            if (inputCount < 0 || inputCount > MaxInputFrames)
            {
                throw new InvalidDataException($"Replay frame count ({inputCount}) exceeds allowed bounds.");
            }

            // Verify stream has enough remaining bytes for all claimed frames before allocating memory
            if (reader.BaseStream.CanSeek)
            {
                long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
                long expectedBytes = (long)inputCount * FrameByteSize;
                if (remainingBytes < expectedBytes)
                {
                    throw new InvalidDataException($"Truncated replay payload: Expected {expectedBytes} frame bytes, but only {remainingBytes} remain.");
                }
            }

            var header = new ReplayHeader(
                Version: version,
                Seed: seed,
                SpawnPosition: new SimVector2(spawnX, spawnY),
                InputCount: inputCount
            );

            // 4. Read Input Frames
            var inputs = new List<PlayerInput>(inputCount);
            for (int i = 0; i < inputCount; i++)
            {
                int tick = reader.ReadInt32();
                sbyte moveX = reader.ReadSByte();
                sbyte moveY = reader.ReadSByte();

                if (moveX < -1 || moveX > 1 || moveY < -1 || moveY > 1)
                {
                    throw new InvalidDataException($"Invalid movement input at tick {tick}. MoveX/MoveY must be exactly -1, 0, or 1.");
                }

                byte actions = reader.ReadByte();
                int aimTargetX = reader.ReadInt32();
                int aimTargetY = reader.ReadInt32();

                bool dash = (actions & ActionFlagDash) != 0;
                bool attack = (actions & ActionFlagAttack) != 0;
                bool skillQ = (actions & ActionFlagSkillQ) != 0;
                bool skillE = (actions & ActionFlagSkillE) != 0;
                bool skillR = (actions & ActionFlagSkillR) != 0;

                inputs.Add(new PlayerInput(
                    Tick: tick,
                    MoveX: moveX,
                    MoveY: moveY,
                    Dash: dash,
                    Attack: attack,
                    SkillQ: skillQ,
                    SkillE: skillE,
                    SkillR: skillR,
                    AimTargetX: aimTargetX,
                    AimTargetY: aimTargetY
                ));
            }

            return new ReplayFile(header, inputs);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Unexpected end of replay stream: payload is truncated.", ex);
        }
    }
}