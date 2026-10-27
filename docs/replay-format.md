# Ghostline Replay Format (`GHST`) Specification

**Version:** 1  
**Endianness:** Little-Endian  
**Extension:** `.replay`

The `GHST` binary format is designed for deterministic replay storage and network transmission. It records only the initial PRNG state and quantized chronological player input frames, enabling the authoritative server to re-simulate matches tick-by-tick headlessly.

---

## 1. File Structure Overview

A `.replay` file consists of a fixed-size header followed by a contiguous stream of fixed-size input frames.

| Component | Size | Description |
|---|---:|---|
| Header | 21 bytes | Contains the magic identifier, version, PRNG seed, player spawn coordinates, and input frame count. |
| Frame 0 | 15 bytes | Contains the first recorded input frame. |
| Frame 1 | 15 bytes | Contains the second recorded input frame. |
| ... | ... | Continues until all input frames have been recorded. |
| Frame N-1 | 15 bytes | Contains the final recorded input frame. |

**Total File Size:**

$$\text{File Size} = 21 + (N \times 15) \text{ bytes}$$

At 60 ticks per second, one minute of gameplay generates 3,600 frames, resulting in approximately 54 KB of replay data.

---

## 2. Header Layout (21 Bytes)

| Offset | Field | Type | Size | Description |
|---|---|---|---|---|
| `0x00` | `Magic` | ASCII (`char[4]`) | 4 bytes | Magic identifier: ASCII `"GHST"` (`0x47`, `0x48`, `0x53`, `0x54`). |
| `0x04` | `Version` | `uint8` (`byte`) | 1 byte | Format specification version (current: `1`). |
| `0x05` | `Seed` | `uint32` | 4 bytes | PRNG seed for deterministic enemy spawn placement. |
| `0x09` | `SpawnX` | `int32` | 4 bytes | Player spawn X position in fixed-point millipixels. |
| `0x0D` | `SpawnY` | `int32` | 4 bytes | Player spawn Y position in fixed-point millipixels. |
| `0x11` | `InputCount` | `int32` | 4 bytes | Total number of recorded input frames (\(N\)). |

---

## 3. Frame Layout (15 Bytes per Frame)

Each recorded tick is serialized into exactly 15 bytes.

| Offset | Field | Type | Size | Description / Range |
|---|---|---|---|---|
| `0x00` | `Tick` | `int32` | 4 bytes | Chronological simulation tick index (\(\geq 0\)). |
| `0x04` | `MoveX` | `int8` (`sbyte`) | 1 byte | Horizontal axis input: `-1`, `0`, or `1`. |
| `0x05` | `MoveY` | `int8` (`sbyte`) | 1 byte | Vertical axis input: `-1`, `0`, or `1`. |
| `0x06` | `Actions` | `uint8` (`byte`) | 1 byte | Bitmask of active abilities and actions. |
| `0x07` | `AimTargetX` | `int32` | 4 bytes | Mouse/reticle target X in millipixels. |
| `0x0B` | `AimTargetY` | `int32` | 4 bytes | Mouse/reticle target Y in millipixels. |

### Action Bitmask Flags (`Actions`)

Active abilities are bitwise OR-packed into byte offset `0x06`.

| Bit | Mask | Flag | Description |
|---|---|---|---|
| Bit 0 | `0x01` (`1 << 0`) | `ActionFlagDash` | Dash burst triggered. |
| Bit 1 | `0x02` (`1 << 1`) | `ActionFlagAttack` | Normal bullet attack active. |
| Bit 2 | `0x04` (`1 << 2`) | `ActionFlagSkillQ` | Shockwave radial AoE triggered. |
| Bit 3 | `0x08` (`1 << 3`) | `ActionFlagSkillE` | Barrier shield triggered. |
| Bit 4 | `0x10` (`1 << 4`) | `ActionFlagSkillR` | Continuous laser beam active. |
| Bits 5–7 | `0xE0` | Reserved | Unused, reserved for future extensions. |

---

## 4. Fixed-Point Coordinate System

To prevent floating-point divergence across compiler architectures (x86_64, ARM64) and runtime environments (.NET Core Linux Docker container vs. Godot Desktop client), all spatial coordinates use integer millipixels.

$$1 \text{ visual screen pixel} = 1{,}000 \text{ simulation millipixels}$$

This representation allows coordinates to be serialized as integers, avoiding floating-point values in the replay data.

---

## 5. Security & Verification Constraints

Before passing deserialized replays to `SimulationWorld.Step()`, the parser enforces strict bounds.

1. **Payload Size:** Maximum 5 MiB (\(5 \times 1024 \times 1024\) bytes).
2. **Frame Count:** Maximum 432,000 frames (approximately 2 hours at 60 ticks per second).
3. **Spawn Boundaries:** \(X, Y \in [-100{,}000,\ 2{,}000{,}000]\) millipixels.
4. **Directional Input Range:** `MoveX` and `MoveY` must strictly belong to \(\{-1, 0, 1\}\).
5. **Anti-Truncation:** The byte stream must contain at least `InputCount * 15` bytes following the 21-byte header.

The parser must validate the header, version, frame count, payload length, and input ranges before simulation begins.
