# LOLProximityVC

A proximity-based voice system for League of Legends. It uses computer vision to detect champion positions on the minimap in real time and adjusts voice volumes based on in-game distance.

## How it works

The system has two components: a Python detection script and a C# program.

**detection.py** runs on the host machine with a spectator view of the game. It uses YOLOv8 to detect champions on the minimap and sends their positions over a local UDP bridge to the C# program.

**league-proximity-vc.exe** acts as both server and client. The host runs the server, which manages connections, runs proximity calculations, and routes audio or volume data. Each player connects to the server and adapts automatically to the selected mode.

## Modes

**Custom VOIP:** The app handles all voice routing. Audio is captured from each client's microphone, sent to the server, mixed with proximity-based volumes, and sent back. No third-party voice software needed.
> Status: Work in Progress.

**Discord:** Clients stay in a normal Discord call. The server calculates proximity volumes and tells each client which users to adjust. Each client then sets those volumes locally via Discord's IPC pipe, identical to manually right-clicking a user and moving their volume slider. No audio passes through the app in this mode.

## Requirements

**Server / host machine**
- Python 3.10+
- `pip install ultralytics mss opencv-python numpy`
- A trained YOLOv8 model (`model.pt`) for champion minimap detection
- .NET 8 runtime

**Client**
- .NET 8 runtime
- Discord running locally (Discord mode only)

## Setup

**1. Train or obtain a YOLOv8 model**

You need a `model.pt` file trained to detect champion icons on the League of Legends minimap. An already trained model will be provided. Place it anywhere on the host machine, the server tab has a browse button to locate it.

**2. Run the server**

Open `league-proximity-vc.exe` and switch to the Server tab. Select your mode (Custom VOIP or Discord), fill in the detection script and model paths, set your proximity distances, and click Start Server. The app will start `detection.py` automatically with the configured settings.

Forward UDP port 7777 on your router so clients can connect.

**3. Connect as a client**

Each player opens `league-proximity-vc.exe`, stays on the "Client" tab, enters their champion name and the server address, and clicks "Connect". The client automatically detects the server mode and shows the appropriate controls, audio settings for VOIP mode, or a Discord user ID prompt for Discord mode.

To find your Discord user ID, enable Developer Mode in Discord (Settings → Advanced → Developer Mode), then right-click your own username and click Copy User ID.

**4. Start the game**

Once everyone is connected, start the spectator view on the host machine. Detection will begin after the configured countdown. The live map window (Server tab → Show live map window) shows detected champion positions and their hearing radii in real time.

## UDP packet protocol

All communication is over UDP. Each packet is 1 byte type followed by an optional payload.

| Type | Hex | Direction | Description |
|------|-----|-----------|-------------|
| CONNECT | 0x01 | client → server | Champion name (UTF-8) |
| AUDIO | 0x02 | bidirectional | Raw 16-bit PCM mono audio |
| KEEPALIVE | 0x03 | client → server | Empty, sent every 1 second |
| DISCONNECT | 0x04 | client → server | Empty |
| ACK | 0xA1 | server → client | Connection accepted |
| REJECT | 0xA2 | server → client | Reason string |
| CLIENTS | 0xA3 | server → client | Comma-separated champion names |
| LEVELS | 0xA4 | server → client | champ:level pairs |
| MODE | 0xA5 | server → client | "voip" or "discord" |
| DISCORD_ID | 0xA6 | client → server | Discord user ID string |
| VOLUMES | 0xA7 | server → client | discordId:volume pairs |

## Audio settings (VOIP mode)

- Sample rate: 44100hz
- Bit depth: 16-bit PCM mono
- Chunk size: 882 samples = 20ms = 1764 bytes

## Networking

Only the server port (default 7777 UDP) needs to be forwarded on the router. The detection bridge port (default 7778) is local only and needs no forwarding. Address matching uses IP-only comparison to handle NAT source port variation.

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE) for details.