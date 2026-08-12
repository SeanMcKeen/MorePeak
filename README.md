# MorePeak

Skip the daily map timer. Play any level you want, or let the mod pick one at random.

**Compatible with PEAK 2.0.a** · Version **2.0.0**

[Thunderstore](https://thunderstore.io/c/peak/p/smckeen/MorePeak/) · [GitHub](https://github.com/SeanMcKeen/MorePeak)

---

## Features

- **Random** — a new map every round
- **Daily** — keep vanilla daily rotation (mod effectively off)
- **Specific level** — e.g. `Level_0`
- **Custom pool** — comma-separated list, random pick from that set  
  e.g. `Level_0, Level_1, Level_2`
- **In-game settings cog** — change options between rounds without restarting
- **Optional HUD** — show current level and/or config on screen

---

## Installation (manual)

1. Install **BepInExPack PEAK** (required dependency).
2. Extract / copy BepInEx into your PEAK install folder  
   (`Steam -> PEAK -> Manage -> Browse Local Files`).
3. Launch PEAK once so `BepInEx/plugins` is created.
4. Place `MorePeak.dll` into `BepInEx/plugins` (or `BepInEx/plugins/MorePeak`).
5. Launch PEAK again.

Thunderstore / r2modman users can install normally through the mod manager.

---

## Configuration

Edit:

`BepInEx/config/com.smckeen.morepeak.cfg`

Or use the **settings cog** in the top-right (available between rounds).

| Setting | Options |
| --- | --- |
| `SelectedLevel` | `Random`, `Daily`, a level name, or a comma-separated list |
| `ShowCurrentLevel` | Show the active level name during a round |
| `ShowSelectedLevelConfig` | Show your current config value during a round |
| `VerboseLogging` | Extra diagnostic logs (off by default) |

Available level names are printed to the BepInEx log the first time you start a game.

---

## Speedrunners

For a seedless-valid run, enable **Show Current Level** and **Show Config**.

---

## Changelog

See [`thunderstore/template/CHANGELOG.md`](thunderstore/template/CHANGELOG.md) for full history.

### 1.9.3
- Updated for PEAK **2.0.a**
- Updated `BeginIslandLoadRPC` patch for the new run-settings parameter
