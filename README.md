# HiddenBull Networking

A complete server framework for Unity, built on **Mirror** and **Steam (Facepunch.Steamworks)**. Run host, dedicated, and peer-to-peer servers — all behind a single clean `NetworkState` facade.

**Features**
- **Server browser** — dedicated (Steam game-server list), LAN, and P2P (Steam lobbies) in one merged list, with ping, map, player count, version-match and lock/mod badges.
- **Session management** — Steam-ticket authentication, connection approval, scene sync, replicated roster, and a synchronized fixed-rate tick clock.
- **Live ping** — per-player latency, broadcast to everyone.
- **Channel chat** — server-authoritative channels (global, team, whisper) with anti-spam and pluggable filtering.
- **Roles & moderation** — permission flags, role hierarchy, bans and a whitelist, persisted to JSON.
- **Dedicated-server console** — in-game and headless command console.
- **One public API** — gameplay, UI, and console talk only to `NetworkState`; the implementation stays internal. Optional layers plug in through one-way seams (`NetworkContentGate`, `NetworkSceneGate`, `NetworkChatGate`).

## Requirements

**Resolved automatically** (registry packages, pulled in by the package):
- `com.unity.nuget.newtonsoft-json` — **3.2.2**
- `com.unity.inputsystem` — **1.19.0**
- `com.unity.ugui` — **2.0.0**

**Install yourself** — add these to your project *before* installing this package:

| Dependency | Source | Recommended version |
|---|---|---|
| **Mirror** | https://github.com/MirrorNetworking/Mirror | **96.10.0** or newer |
| **Facepunch.Steamworks** | https://github.com/Facepunch/Facepunch.Steamworks | 2.5.1 or newer |
| **PicoShot.Localization** | https://github.com/PicoShot/Localization-Unity | latest (`?path=/Package`) |

> **VespaIO** — **already bundled** inside this package under `Runtime/Console/VespalO`, so **do not install it separately**. It is a **modified version** of the original ( `<VespaIO-repo-link>` ) adapted for this framework; replacing it with the upstream package will break the console.

## Installation

Install via the Unity Package Manager using the git URL:

1. Open **Window → Package Manager**.
2. Click **+** → **Add package from git URL…**
3. Paste and click **Add**:

```
https://github.com/rytiex/HiddenBull-Networking.git?path=/Package
```

Or add it to `Packages/manifest.json`:

```json
"com.hiddenbull.networking": "https://github.com/rytiex/HiddenBull-Networking.git?path=/Package"
```

> Pin a version with a tag: `...git?path=/Package#v0.1.0`

**➡ Strongly recommended:** import the **Demo Setup** sample right after installing (see Setup, step 2). It gives you a ready-to-run bootstrap prefab, a Steam config asset, and the console input actions — far easier than wiring everything by hand.

## Setup

### 1. Dependencies in place
Make sure Mirror, Facepunch.Steamworks, and PicoShot.Localization are already in your project (see **Requirements**). The package will not compile until they're present.

### 2. Import the Demo Setup *(strongly recommended)*
**Window → Package Manager → HiddenBull Networking → Samples → Demo Setup → Import.** This copies `DemoSetup.unitypackage` into your project — **double-click it** to import:
- `BOOT_GameInitializer` prefab — the self-loading bootstrap (session manager, Steam lifecycle, console, debug GUI)
- `DATA_SteamConfig` — your Steam identity asset
- `INPUT_Action.inputactions` — the console hotkey actions

### 3. Input System
The console reads its hotkeys from the **project-wide** Input Actions:
- **Project Settings → Input System Package → Project-wide Actions** → assign **`INPUT_Action.inputactions`**.
- **Project Settings → Player → Active Input Handling** → **Input System** (or *Both*).

Without this, the console hotkeys silently do nothing.

### 4. Steam identity
Open **`DATA_SteamConfig`** (it must live in a **`Resources/`** folder) and set your real **App ID**, **Mod Dir**, and **Game Description**. If no `SteamConfig` is found in `Resources/`, networking is disabled at startup.

### 5. Run
Press Play — the `BOOT_GameInitializer` prefab boots automatically and persists across scenes. Use the on-screen GUI to **Host**, or open the **server browser** via **Join**.

> **Dedicated servers:** to show up in the Steam master-server list, forward the **query port** (`GamePort + 1`, UDP) and use a real Steam **App ID** (not 480).

> **Build note:** `ENABLE_INPUT_SYSTEM` is set automatically from *Player → Active Input Handling* (Input System or Both) — it does **not** appear in the Scripting Define Symbols list. Mirror's `MIRROR_*` defines **are** added to that list by Mirror's compiler-symbols tool. If Mirror's reader/writer for the framework's `NetworkMessage`s misbehave in a build, verify the `MIRROR_*` defines under *Project Settings → Player → Scripting Define Symbols*.

## Documentation

Detailed guides for each system:
- [Getting Started](Docs/getting-started.md)
- [NetworkState — the facade](Docs/network-state.md)
- [Server Browser](Docs/server-browser.md)
- [Roles & Moderation](Docs/roles-moderation.md)
- [Chat](Docs/chat.md)
- [Console & Commands](Docs/console.md)
- [Seams & Extensibility](Docs/seams.md)

## Roadmap

Planned for future releases:
- 🧩 **Steam Workshop support** — discover, subscribe to, and sync content/mods through Workshop.
- 🎙️ **Voice chat** — channel-based team / proximity voice over the Steam transport.
