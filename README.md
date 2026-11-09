<div align="center">

# RTS AI Simulation

**A real-time strategy game where the computer players run themselves.** Three AI factions fight over resources on a 2D map — they gather ore, build bases, defend their territory and attack each other, all without a human telling them what to do. You can jump in and command troops yourself, or just watch the AIs battle it out.

![Engine](https://img.shields.io/badge/Unity-6000.4-000?logo=unity&logoColor=white)
![Genre](https://img.shields.io/badge/2D-RTS-0b5fff)
![Language](https://img.shields.io/badge/written_in-C%23-512bd4?logo=dotnet&logoColor=white)
![AI](https://img.shields.io/badge/AI-hand--written-orange)
![Licence](https://img.shields.io/badge/licence-All_rights_reserved-red)

**[Read the AI design notes](docs/AI_DESIGN.md)** — how each behaviour works, written up per system.

</div>

## What it is

Think of a game like StarCraft or Age of Empires, shrunk down to 2D. Each faction has a base, harvester units that collect ore, and military troops that fight. The point of the project isn't the game itself — it's the **artificial intelligence** that runs each faction. Every decision, from a single harvester deciding where to dig, up to a faction deciding when to launch a full attack, is made by code written from scratch. No AI libraries.

## The three levels of "thinking"

The AI is built in layers, like a real army:

- **The individual soldier or worker** makes its own moment-to-moment decisions — where to move, when to run away, what to shoot. Each one follows a small set of rules (a *finite state machine*) like "if I'm carrying ore, take it home" or "if an enemy is close and I'm weak, flee."
- **Shared awareness.** Units need to know what's around them without every unit asking every other unit (which would be far too slow). Two systems handle this: one quickly answers "who is near me," and another keeps a heat-map of which faction controls which part of the map.
- **The commander.** Each faction has one high-level brain that thinks about the big picture: do we need more harvesters? Should we build a defensive turret? Is now a good time to attack, and with how many troops?

## What makes it interesting

- **It stays fast.** With dozens of units all sensing the world, checking every pair would be slow. Units instead look things up in a structure that only searches their local area, so the simulation stays smooth as it grows.
- **Units aren't fearless.** A harvester that keeps getting attacked will avoid that ore patch, flee when danger approaches, and report back to its commander so the faction knows an area is unsafe.
- **Every run is different.** Each commander randomly picks its own personality at the start — how aggressive it is, how much it values defence, how big an army it wants — so factions behave differently from one game to the next.
- **Attacks are planned, not random.** Before attacking, a commander estimates how much resistance it will meet along the way and sizes its attack force accordingly. It won't throw its whole army away and leave home undefended.
- **A faction can survive losing its main base.** If the headquarters falls, the commander relocates to a secondary base and keeps fighting.

## Controls

| Input | Action |
|---|---|
| Left mouse drag | Box-select troops (a click selects one) |
| Right mouse click | Order selected troops to move there |
| Space | Toggle fast-forward (3× speed) |

Troops under your control still scan for enemies and stay in formation while they move.

## Running it

Install Unity **6000.4.5f1** (free), open this folder in Unity Hub, open `Assets/Scenes/SampleScene.unity`, and press Play. To see what the AI "sees," switch to the Scene view while it's running — it draws the search regions and the territorial heat-map on screen.

---

<details>
<summary><b>Technical detail</b> — the AI stack, for a developer reader</summary>

| Layer | System | Where |
|---|---|---|
| Micro | Harvester FSM: `Idle`, `SeekingOre`, `Mining`, `Returning`, `Fleeing`, `Waiting` | `HarvesterAI.cs` |
| Micro | Troop FSM: `Rallying`, `Guarding`, `Attacking`, `FallingBack`, `PlayerCommand` | `TroopAI.cs` |
| Micro | Steering with unit/base avoidance and whisker-style wall avoidance | `Unit.cs` |
| Micro | Boids-style separation and cohesion for troop packs | `TroopAI.cs` |
| Mid | QuadTree spatial index, rebuilt every frame, with stale-reference checks | `QuadTree.cs`, `SimulationManager.cs` |
| Mid | Grid influence map per faction with quadratic falloff, decay and a heat cap | `CustomMapManager.cs` |
| Macro | Faction commander: production priorities, expansion, perimeter defence, task-force attacks | `FactionCommander.cs` |
| Economy | Ore nodes that deplete and respawn at random positions | `ResourceManager.cs`, `OreMine.cs` |
| Defence | Turrets and expansion bases that can take over as HQ | `TurretAI.cs`, `ExpansionBaseAI.cs` |

**Highlights**

- **Spatial queries without O(n²) scans.** Perception, flocking and threat checks query a QuadTree instead of looping over every entity. The tree is rebuilt each frame, so there's no incremental-update bookkeeping or stale positions.
- **Two views of the map.** The QuadTree answers "who is near me"; the influence map answers "who controls this area." Troops fall back when enemy influence is high, and harvesters use the map to pick a safe place to flee to.
- **Threat-aware harvesters.** Reject ore nodes with enemies nearby, flee when threatened, report repeated danger to the commander, blacklist nodes they get stuck at, and retarget when a closer safe node appears.
- **Commander personalities.** Each randomises its desired harvester count, safe-zone size, task-force size and turret preference at start.
- **Attack planning.** Samples enemy influence along the line to a target to estimate resistance, then sizes a task force from that, its personality and a resentment value that rises when attacked. It only attacks if enough troops stay home.
- **Production priority tree.** Per base: emergency repairs, turret repairs, emergency harvesters, defensive buffer, expansion, more harvesters, turrets where danger shows, then turrets or troops by preference.
- **Survives losing its HQ.** The commander moves to an expansion base; if none remain, the faction's assets are removed.

See `docs/AI_DESIGN.md` for the algorithm-level write-up.

</details>

<details>
<summary><b>Project layout</b></summary>

```
Assets/
  Scripts/        All gameplay and AI code
  Prefabs/        Bases, expansion bases, harvesters, troops, turrets, ore
  Scenes/         SampleScene (map, walls, SimulationManager)
docs/
  AI_DESIGN.md    Per-system design notes
```

</details>

<details>
<summary><b>Known limitations</b></summary>

- Units steer around obstacles locally. There's no global path planner, so they can take awkward routes in concave wall layouts.
- The map is a single hand-built scene. Faction setup is done in the scene and prefabs, not through a menu.
- There are no automated tests. Behaviour was verified by watching simulations, including fast-forwarded runs.

</details>

---

<div align="center">
<sub>Daniel Jackson · <a href="https://github.com/DanielJacksonRSA">GitHub</a> · <a href="LICENSE">All rights reserved</a></sub>
</div>
