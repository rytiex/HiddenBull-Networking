# HiddenBull Networking

A Mirror + Steam (Facepunch) server framework. Everything is reached through a single
`NetworkState` facade; the implementation is internal.

- **Server browser** - dedicated (Steam game-server list) + LAN + P2P (Steam lobbies), merged
- **Roster + ping**, channel chat, roles & moderation, dedicated-server console
- **Seams** - `NetworkContentGate` / `NetworkSceneGate` / `NetworkChatGate` for optional layers

## Installation

Install via the Unity Package Manager using the git URL:

1. Open **Window → Package Manager**.
2. Click the **+** button → **Add package from git URL…**
3. Paste the URL below and click **Add**: https://github.com/rytiex/HiddenBull-Networking.git?path=/Package

**Or** add it directly to your project's `Packages/manifest.json` under `"dependencies"`:
```json
"com.hiddenbull.networking": "https://github.com/rytiex/HiddenBull-Networking.git?path=/Package"
```

## Requirements

Registry packages (resolved automatically via `package.json`):
- `com.unity.nuget.newtonsoft-json`
- `com.unity.inputsystem` - set **Active Input Handling = Input System (or Both)** in Player Settings
- `com.unity.ugui`

Must be present in the project (NOT auto-resolved - Asset Store / git / bundled):
- **Mirror** - https://mirror-networking.com
- **Facepunch.Steamworks** - the Win/Posix DLLs
- **VespaIO** - bundled under `Runtime/Console/VespalO` (console / command system)
- **PicoShot.Localization** - git package:
  `"com.picoshot.localization": "https://github.com/PicoShot/Localization-Unity.git?path=/Package"`

## Setup

1. **Requirements** - import **Mirror** and **Facepunch.Steamworks**, and add the
   **PicoShot.Localization** git package (see above). The registry packages
   (Newtonsoft / Input System / uGUI) resolve automatically.

2. **Input System** - the console hotkeys read named actions (e.g. `DeveloperConsoleActive`)
   from the **project-wide** Input Actions, so the included asset must be assigned:
   - *Edit → Project Settings → Input System Package → Project-wide Actions* →
     set it to the imported **`INPUT_Action.inputactions`**.
   - *Edit → Project Settings → Player → Active Input Handling* → **Input System** (or Both).
   Without this the hotkeys silently do nothing (the action is "not found").

3. **Steam Config** - open **`Resources/DATA_SteamConfig`** (or create one via
   `Create → HiddenBull → Steam Config`) and fill in your real **AppId / ModDir /
   GameDescription**. It must stay in a **`Resources/`** folder — otherwise `SteamLifecycle`
   logs an error and disables networking.

4. **Boot** - the **`BOOT_GameInitializer`** prefab self-loads from `Resources` (via
   `RuntimeInitializeOnLoadMethod`) and persists across scenes. It carries
   `NetworkSessionManager`, `SteamLifecycle`, the console, and the debug GUI.

5. Enter Play - use the on-screen GUI to **Host**, or open the server browser via **Join**.

> Dedicated servers also need the **query port** (`GamePort + 1`, UDP) port-forwarded to appear
> in the Steam master-server list. A real Steam **AppId** (not 480) is required for live listing.

## Demo

Install the **Demo Setup** sample from the Package Manager (it copies `DemoSetup.unitypackage`
into your project - double-click it to import the BOOT prefab, `SteamConfig`, and input actions).

## Scripting Define Symbols

Set automatically; listed for troubleshooting:
- `ENABLE_INPUT_SYSTEM` - from *Player → Active Input Handling = Input System (or Both)*.
- `MIRROR`, `MIRROR_*_OR_NEWER` - from Mirror's compiler-symbols tool.

If Mirror's reader/writer for the framework's `NetworkMessage`s misbehave in a build, verify these
under *Project Settings → Player → Scripting Define Symbols*.

## API surface

Consumers use only: `NetworkState` (facade), `NetworkSessionManager`, the three gates,
`ServerStartSettings` / `ClientConnectSettings` / `SteamConfig`, the facade data types
(`PlayerInfo`, `ServerEntry`, `SceneData`, `SceneField`, enums, `Permissions`/`RoleDefinition`/
`AdminEntry`), read-only `ServerRoles` / `SteamInformation`, and the scene MonoBehaviours
(`SteamLifecycle`, `Terminal`, `NetworkCommandBridge`, `HiddenBullNetworkGUI`).
