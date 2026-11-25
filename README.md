# Far Player Map Markers

Shows **every online player** on the world map and minimap, however far away they are, with a pointer showing which way they're facing. Every player's **waypoints** are shared too, with the icon and colour the owner picked.

## Features

- All online players on the world map (M) and the minimap, at any distance. The server sends positions every second (adjustable).
- Players outside the visible area stay on the map edge, faded, pointing towards where they are.
- Everyone's waypoints are shared, with their real icons and colours. Pinned waypoints also stay on the map edge.
- Hover over a marker to see the player's or waypoint's name and how many blocks away it is.
- Choose your own marker colours with a colour picker (hue slider, shade square and hex box). Other players see your colours.
- The *Far players* layer can be switched off in the map's layer list.

## Settings

Type `.farmarkers` in chat to open the settings window.

- **Your marker:** fill and border colour. Click *Edit* or the colour square for the picker, or paste a hex code.
- **Others' markers:** colours for other players, or turn on *Use player-chosen colours* to see everyone in their own colours.
- **Display:** keep far players at the map edge, show or hide other players' waypoints.
- **Server settings (admins only):** update rate, hide crouching players, turn waypoint sharing on or off.

## Config files

- `ModConfig/FarPlayerMarkersServerConfig.json` (server): `UpdateIntervalMs` (250 to 10000, default 1000), `HideCrouchingPlayers`, `ShareWaypoints`.
- `ModConfig/FarPlayerMarkersConfig.json` (each player): colours, marker and waypoint size, edge and waypoint display options.

## Install

Needed on the **server and every client**. Players joining a server that has it will download it automatically.

## Notes

- Built for Vintage Story 1.22.
- Only players who are online are shown.
- Waypoint sharing shows waypoints to everyone on the server, so admins can turn it off if players want their waypoints kept private.

## Download

ModDB (1-click install): https://mods.vintagestory.at/farplayermarkers

## Report a problem

Please report bugs on the [Issues](../../issues) tab and include your game version, the mod version and your log. Security problems: see [SECURITY.md](SECURITY.md).

**Where to find your log**

The logs are in the Vintage Story data folder, in a folder called `Logs`:

| System | Data folder |
|---|---|
| Windows | `%appdata%\VintagestoryData` (paste that into the File Explorer address bar) |
| Linux | `~/.config/VintagestoryData` |
| Mac | `~/Library/Application Support/VintagestoryData` |

- **Playing on a server:** send `client-main.log` from your own game. If you run the server, also send `server-main.log` from the server's data folder.
- **Singleplayer:** `client-main.log` and `server-main.log` are both in the data folder above.
- Attach the file to the issue (drag it into the box), or paste just the lines with errors. Reproduce the problem first, then grab the log straight afterwards, so the error is near the end.
