# Terraria Desktop

Your real, live Terraria character on your Windows desktop.

A tModLoader mod draws your player every frame with the game's own player renderer,
so armor, vanity, dyes, wings, held items and animations are exactly what you see in
game. It streams that picture to a small overlay app, which shows the character
standing on top of your taskbar and walking around. The overlay window is see-through,
always on top, and every click passes straight through it.

Mining and boss fights on the desktop come later. This is the first piece.

```
tModLoader  ──(TerrariaDesktop mod)──>  127.0.0.1:47615  ──>  TerrariaDesktopOverlay.exe
  draws Main.LocalPlayer into a           frames: pixels +        transparent, click-through,
  192×192 texture each frame              velocity, facing        topmost window on the taskbar
                                   <──  control: walk / face  ──
```

## What's in here

| Folder | What it is |
|---|---|
| `TerrariaDesktop/` | The tModLoader mod (C#). |
| `DesktopOverlay/` | The Windows overlay app (C#, .NET 8). |
| `PROTOCOL.md` | The tiny format the two use to talk to each other. |
| `install.ps1` | Links the mod into tModLoader's ModSources so you can build it in game. |

## Install

You need tModLoader from Steam on Windows.

### Option A: use the ready-built files

Every pull request and push builds both files. Open the **Actions** tab on GitHub,
pick the latest **Build** run, and download the **TerrariaDesktop** artifact. It holds:

- `TerrariaDesktop.tmod`: the mod
- `TerrariaDesktopOverlay.exe`: the overlay (no .NET install needed)

1. Copy `TerrariaDesktop.tmod` into
   `Documents\My Games\Terraria\tModLoader\Mods\`.
2. Start tModLoader, open **Mods**, turn on **Terraria Desktop**, and press **Reload Mods**.
3. Put `TerrariaDesktopOverlay.exe` anywhere you like (your Desktop is fine).

If tModLoader says the mod was built for a different version, use option B instead.

### Option B: build the mod from source in game

1. Clone this repository (or download it as a ZIP and unzip it).
2. In the repository folder, run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
   This links `TerrariaDesktop` into `Documents\My Games\Terraria\tModLoader\ModSources`.
   (Or copy the `TerrariaDesktop` folder there yourself.)
3. In tModLoader, open **Workshop → Develop Mods** and press **Build + Reload** next to
   Terraria Desktop.
4. Build the overlay, if you have the .NET 8 SDK:
   ```powershell
   dotnet publish DesktopOverlay -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
   ```
   The app is `publish\TerrariaDesktopOverlay.exe`.

## Try it

1. Double-click `TerrariaDesktopOverlay.exe`. Nothing appears yet except a small icon in
   the system tray (by the clock; it may be under the **^** arrow). Hover it and it says
   *Waiting for Terraria...*.
2. Start tModLoader with the mod enabled. The tray icon changes to
   *Connected. Enter a world to see your character.*
3. Enter any world. Your character appears standing on top of your taskbar and starts
   wandering left and right on its own, stopping now and then.
4. Things to check:
   - Change armor, vanity or a dye in game. The desktop character changes right away.
   - Swing a weapon or use an item. The desktop character does the same swing.
   - Click "through" the character onto a desktop icon or window behind it. It works,
     because the overlay ignores the mouse.
   - Right-click the tray icon and choose **Copy my in-game movement**. Now the desktop
     character walks when you walk in game and jumps when you jump. Walk off one side of
     the screen and it comes back on the other.
   - **Size** in the tray menu makes it 1× to 4× bigger. 2× is the default.
5. Return to the main menu and the character disappears. Quit the overlay from the tray
   menu with **Exit**.

### Mod settings

In game: **Settings → Mod Configuration → Terraria Desktop**.

- **Send my character to the desktop**: turn streaming off and on.
- **Draw at full brightness**: on by default, so the character isn't dark in caves.
- **Frames per second**: 60 by default. Lower it if you ever notice slowdown.
- **Local port**: 47615. If you change it, start the overlay with
  `TerrariaDesktopOverlay.exe --port <number>`.

## Troubleshooting

- **Windows says "Windows protected your PC"** when opening the overlay: it isn't
  signed yet. Click **More info → Run anyway**.
- **Tray icon stays on "Waiting for Terraria..."**: check the mod is enabled, then look
  in `Documents\My Games\Terraria\tModLoader\Logs\client.log` for lines containing
  `TerrariaDesktop`. "Could not open port" means another program uses 47615; pick another
  port in the mod settings.
- **The character floats or sinks a little relative to the taskbar**: tell us your
  display scale (Settings → Display → Scale) and taskbar position. This first version
  assumes the taskbar is at the bottom of your main screen.

## How it works

- `TerrariaDesktop/Common/DesktopCaptureSystem.cs` hooks `Main.OnPreDraw`. Each frame it
  switches to a 192×192 `RenderTarget2D`, calls
  `Main.PlayerRenderer.DrawPlayer(...)` for `Main.LocalPlayer` in an immediate-mode
  `SpriteBatch` (so dye shaders apply), reads the pixels back, and hands them to
  `FrameServer`. It never rebuilds the character from the save file.
- In wander mode it temporarily swaps in the game's walking leg/body frames and the
  overlay's facing direction for that one draw call, then puts the real values back.
- `FrameServer` is a loopback-only TCP server. The game thread only ever copies the
  newest frame into a buffer; background threads do the sending, so a slow overlay can't
  slow the game down.
- `DesktopOverlay` receives frames, scales them up with nearest-neighbour so the pixel
  art stays crisp, and shows them with `UpdateLayeredWindow` (per-pixel alpha) in a
  window with `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW |
  WS_EX_NOACTIVATE`.
