# Mod ↔ overlay protocol (version 1)

The mod listens on `127.0.0.1:47615` (changeable in the mod config). It only accepts
connections from this computer. One overlay can be connected at a time; a new one
replaces the old one. All numbers are little-endian.

## Frame (mod → overlay)

Sent up to *FramesPerSecond* times a second, only while an overlay is connected.

| Offset | Type   | Field |
|-------:|--------|-------|
| 0      | u32    | magic `0x52464454` ("TDFR") |
| 4      | u16    | protocol version (1) |
| 6      | u16    | flags: 1 = in a world, 2 = on the ground, 4 = walk animation is being driven by the overlay, 8 = mounted |
| 8      | i32    | width in pixels (0 when not in a world) |
| 12     | i32    | height in pixels |
| 16     | i32    | feet row: the pixel row the player's feet stand on |
| 20     | i32    | direction the player faces (-1 left, 1 right) |
| 24     | f32    | velocity X, game pixels per tick (60 ticks = 1 second) |
| 28     | f32    | velocity Y, positive is down |
| 32     | i32    | payload length = width × height × 4 |
| 36     | u32    | frame number |
| 40     | bytes  | pixels, BGRA, **premultiplied** alpha, top row first |

The picture is drawn by the game's own `Main.PlayerRenderer.DrawPlayer`, so everything
the game draws on the player (armor, vanity, dyes, wings, held item, animation) is in it.

## Control (overlay → mod)

20 bytes, sent whenever something changes.

| Offset | Type | Field |
|-------:|------|-------|
| 0      | u32  | magic `0x54434454` ("TDCT") |
| 4      | i32  | mode: 0 = follow the game, 1 = wander |
| 8      | i32  | direction to face in wander mode (-1 / 1) |
| 12     | i32  | walking (0 / 1) in wander mode |
| 16     | f32  | walking speed, game pixels per tick, used to pace the leg animation |

In **follow** mode the mod draws the player exactly as it is in game, and the overlay
moves the window by the player's velocity. In **wander** mode the overlay picks where
to walk, and the mod plays the game's walking frames (leg/body frames 7–19) while
the overlay says it is walking. Using an item, a mount or a hook keeps the real animation.

This control channel is where game inputs from the desktop will be added later.
