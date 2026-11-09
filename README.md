# RTS AI Simulation

A 2D real-time strategy simulation where three AI factions compete for ore, expand, defend and attack each other. The player can also box-select troops and give them orders. Built in Unity (C#) with about 2,800 lines of hand-written gameplay and AI code and no third-party AI packages.

The focus is the AI stack: units make local decisions with finite state machines and steering, a spatial index and an influence map give them shared sensing, and a per-faction commander makes economic and strategic decisions on top.

| | |
|---|---|
| Engine | Unity 6000.4.5f1, 2D URP |
| Language | C# |
| Input | Unity Input System (mouse and keyboard) |
| Code | `Assets/Scripts` (12 scripts) |
| Design notes | [`docs/AI_DESIGN.md`](docs/AI_DESIGN.md) |

## What it demonstrates

| Layer | System | Where |
|---|---|---|
| Micro | Harvester FSM: `Idle`, `SeekingOre`, `Mining`, `Returning`, `Fleeing`, `Waiting` | `HarvesterAI.cs` |
| Micro | Troop FSM: `Rallying`, `Guarding`, `Attacking`, `FallingBack`, `PlayerCommand` | `TroopAI.cs` |
| Micro | Steering with unit and base avoidance and whisker-style wall avoidance | `Unit.cs` |
| Micro | Boids-style separation and cohesion for troop packs | `TroopAI.cs` |
| Mid | QuadTree spatial index, rebuilt every frame, with stale-reference checks | `QuadTree.cs`, `SimulationManager.cs` |
| Mid | Grid influence map per faction with quadratic falloff, decay and a heat cap | `CustomMapManager.cs` |
| Macro | Faction commander: production priorities, expansion, perimeter defence, task-force attacks | `FactionCommander.cs` |
| Economy | Ore nodes that deplete and respawn at random positions | `ResourceManager.cs`, `OreMine.cs` |
| Defence | Turrets and expansion bases that can take over as HQ | `TurretAI.cs`, `ExpansionBaseAI.cs` |

### Highlights

- **Spatial queries without O(n²) scans.** Perception, flocking and threat checks query a QuadTree instead of looping over every entity. The tree is rebuilt each frame, so there is no incremental-update bookkeeping and no stale positions.
- **Two views of the map.** The QuadTree answers "who is near me". The influence map answers "who controls this area". Troops fall back when enemy influence gets too high, and harvesters use the map to pick a safe place to flee to.
- **Threat-aware harvesters.** Harvesters reject ore nodes with enemies nearby, flee when threatened, report repeated danger to their commander, blacklist nodes they get stuck at, and retarget when a closer safe node appears.
- **Commander personalities.** Each commander randomises its desired harvester count, safe-zone size, task-force size and turret preference at start, so factions play differently from run to run.
- **Attack planning.** The commander samples enemy influence along the line to a target to estimate resistance, then sizes a task force from that resistance, its personality and a resentment value that rises when it is attacked. It only attacks if enough troops remain at home.
- **Production priority tree.** Per base: emergency repairs, turret repairs, emergency harvesters, defensive troop buffer, expansion, more harvesters, turrets where influence shows danger, then turrets or troops by preference.
- **Survives losing its HQ.** If the main base falls, the commander can move to an expansion base. If none remain, the faction's assets are removed.

See [`docs/AI_DESIGN.md`](docs/AI_DESIGN.md) for the algorithm-level write-up of each script.

## Controls

| Input | Action |
|---|---|
| Left mouse drag | Box-select troops (a click selects a single troop) |
| Right mouse click | Order the selected troops to that position |
| Space | Toggle fast-forward (3x by default) |

Troops under player control still scan for enemies and keep flocking while they move.

## Running it

1. Install Unity **6000.4.5f1** (other Unity 6 versions will likely work, but this is the version it was built with).
2. Clone the repo and open the folder as a project in Unity Hub. The first import takes a few minutes.
3. Open `Assets/Scenes/SampleScene.unity` and press Play.

To see the AI's view of the world, switch to the **Scene** view while playing. `SimulationManager` draws the QuadTree cells and `CustomMapManager` draws the per-faction influence heatmap as gizmos.

## Project layout

```
Assets/
  Scripts/        All gameplay and AI code
  Prefabs/        Bases, expansion bases, harvesters, troops, turrets, ore
  Scenes/         SampleScene (map, walls, SimulationManager)
docs/
  AI_DESIGN.md    Per-system design notes
```

## Known limitations

- Units steer around obstacles locally. There is no global path planner, so units can take awkward routes in concave wall layouts.
- The map is a single hand-built scene. Faction setup is done in the scene and prefabs, not through a menu.
- There are no automated tests. Behaviour was verified by watching simulations, including fast-forwarded runs.

## Author

Daniel Jackson. [GitHub](https://github.com/DanielJacksonRSA)

Licensed under the [All rights reserved](LICENSE).
