# AI Design Notes

Design notes for the RTS AI simulation. See the [README](../README.md) for an overview and setup.

---

## Executive Overview and System Architecture

This project is a multi-layered AI system for a real-time strategy simulation. The design is intentionally split into three interlocking layers:

1) Micro-level autonomy inside each unit, driven by finite state machines (FSMs), local steering, and perception queries.
2) Mid-level environmental reasoning, including a spatial index (QuadTree) and an influence heatmap that act as global AI sensors.
3) Macro-level strategic control in the FactionCommander, which manages economy, production, expansion, defense, and offensive task forces.

The project prioritizes performance and scale. Instead of brute-force neighbor checks, all relevant logic uses a custom QuadTree that rebuilds every frame based on the current entity list. This allows rapid area queries for combat vision, threat detection, and flocking neighbors. The grid-based influence map provides a second AI perspective: a simplified tactical heatmap used to judge safety, calculate defense strength, and estimate the resistance between bases and targets.

The result is a layered AI stack that achieves both tactical authenticity and strategic intent. Harvesters make practical mining choices, avoid threats, and report danger. Troops coordinate via boids-style flocking, defend perimeters, and adapt their state based on heatmap data. The Commander actively expands territory, decides when to repair or build units, launches retaliatory or opportunistic attacks, and manages a growing defensive perimeter.

The architecture is not just a list of systems. These systems actively shape each other:

- The QuadTree allows fast, reactive unit-level behaviors (scan, flee, avoid, attack) without high CPU cost.
- The influence heatmap influences macro decisions such as expansion or defense, and triggers local AI state changes like falling back from unsafe regions.
- The Commander reacts to field reports from harvesters and uses those reports to redirect troops, reinforcing the economy and defense loop.

Together, these interactions form a cohesive AI ecosystem. The system is data-driven in the sense that global conditions (resources, influence levels, troop counts) influence behavior choices dynamically. It also embraces ambiguity: some decisions include randomness to avoid deterministic play patterns, ensuring that repeated simulations remain varied.

The rest of this document breaks down each script, AI subsystem and algorithm.

---

## Spatial Partitioning: QuadTree and Entity Registry

### QuadTree.cs

The QuadTree is the primary spatial index used for all localized perception in the simulation. It operates over a bounded rectangular area defined by SimulationManager.simulationBounds. Each frame, the QuadTree is rebuilt, inserting all registered entities.

Key algorithmic aspects:

- Axis-aligned partitioning: The QuadTree divides space into quadrants based on a rectangular root node. Each node stores a list of entities until it reaches capacity, then subdivides into four children.
- Capacity-based split: When a node reaches the configured capacity, it subdivides into top-left, top-right, bottom-left, and bottom-right nodes. Entities are re-inserted into appropriate child nodes.
- Query by rectangle: The Query method takes a Rect range and collects all entities contained within that rectangle. It uses overlap checks to prune branches, only descending into nodes that overlap the query rectangle.
- Stale reference protection: The IsValid check prevents Unity-destroyed or null entities from contaminating the tree. This is crucial because UnityObjects can be invalid without being strictly null in C#.

The QuadTree API is intentionally small but powerful:

- Insert(IEntity): Adds a new entity if it is valid and within bounds.
- Query(Rect, List<IEntity>): Returns all entities inside a rectangle, allowing any caller to run fast local searches.
- DrawDebug: Renders nested wire cubes for debugging and visualization.

By using a spatial index with capacity-based splitting, the code avoids a full O(n^2) neighbor scan. This is especially important for systems like flocking and threat detection where multiple units repeatedly query nearby agents each frame. The QuadTree keeps complexity closer to O(n log n), which supports scaling to large swarms.

### SimulationManager.cs

The SimulationManager is the global registrar for entities and the central runtime loop for the QuadTree. It provides a shared registry that includes all units, bases, turrets, and even resource nodes (which means every meaningful game object can be queried spatially).

Core responsibilities:

- Initializes the QuadTree during Awake.
- Rebuilds the QuadTree each Update by inserting all currently registered entities.
- Supports simulation time control with a toggleable speed multiplier.
- Provides RegisterEntity and RemoveEntity to maintain the entity list consistently.

Notable algorithmic implications:

- The QuadTree rebuild ensures all queries are always current. This prevents the need for incremental update logic and reduces bugs from stale positions.
- Entities are registered across multiple systems (Unit base class, ResourceManager, TurretAI, ExpansionBaseAI). This central registry forms the backbone of all AI perception.

The simulation speed toggle introduces a rapid evaluation mode, allowing the AI to be observed under accelerated conditions without modifying the base logic. This is useful for testing or gameplay pacing and demonstrates foresight into evaluation needs.

---

## Core Agent Abstractions and Movement Steering

### Unit.cs

The Unit class serves as the shared base for all mobile entities such as TroopAI and HarvesterAI. It also implements ITargetable, which standardizes health and faction data for combat and threat evaluation.

Key interface and base attributes:

- IEntity exposes Position, enabling QuadTree indexing and spatial queries.
- ITargetable extends IEntity with FactionID, Health, and TakeDamage.

Movement system:

The Unit class provides a steering-based movement system that blends multiple forces. It is not a simple "MoveTowards" but a flexible steering engine:

- Desired direction is derived from target direction or direct command.
- Avoidance of other units can be enabled via avoidOtherUnits. This uses Physics2D.CircleCastAll to detect nearby units and adds a separation force. A tangent component helps units slide around each other instead of deadlocking.
- Avoidance of the base can be enabled via avoidBase. This calculates the closest point on the base collider and pushes the unit away if too close. The push strength scales with proximity, ensuring smooth repulsion.
- Obstacle avoidance uses whisker-style circle casts to detect walls. When a collision is detected, the unit selects an evasion direction based on wall normals and retains this direction briefly via a timer to prevent oscillation. This is a classic steering technique to avoid "ping pong" behavior.

Rotation is handled by aligning the unit orientation to the movement vector. This adds readability to the simulation and visual feedback for directionality.

Combat system:

The Unit class includes basic combat stats, cooldowns, and damage handling. Derived classes may override behavior, but damage processing and destruction are standardized. Health visualization is achieved through alpha changes on the unit sprite, maintaining consistent visual language across the AI system.

This base class provides one reusable steering system instead of hard-coded movement rules in each AI class. It also ensures that AI units can smoothly navigate and avoid obstacles without full pathfinding, which is important for real-time swarm behavior.

---

## Resource Economy: ResourceManager and OreMine

### ResourceManager.cs and Resource

The Resource system models ore nodes as lightweight entities that can be mined and removed. Each resource node is inserted into the QuadTree, enabling harvesters to search for resources efficiently.

Key mechanics:

- Resource nodes are created from GameObjects tagged as "Resource". This allows designers to seed the map visually.
- Each Resource stores Position, currentWealth, maxWealth, and a reference to its graphics object.
- The Resource.UpdateVisual method fades the sprite alpha based on current wealth, delivering immediate feedback on depletion.
- Mining uses ResourceManager.Mine, which reduces resource wealth, updates visuals, and removes the node when depleted. This removes both the node from the active list and its associated graphics object from the scene.

Algorithmic impact:

- By registering resources in the QuadTree, harvesters can query spatially and avoid global scans.
- Depleted nodes are removed from the QuadTree, which prevents AI from wasting time on exhausted targets.

### OreMine.cs

OreMine introduces procedural resource spawning over time. It adds dynamism to the economy and ensures that the resource landscape evolves.

Spawn logic:

- Each mine uses a random spawn interval between spawnIntervalMin and spawnIntervalMax. This creates non-deterministic resource availability and reduces predictable patterns.
- Spawn locations are chosen randomly within a ring defined by spawnRadiusMin and spawnRadiusMax.
- The CustomMapManager is used to avoid blocked tiles, which prevents spawning inside walls.
- Each new resource node uses a randomized wealth value between oreWealthMin and oreWealthMax. This creates variable reward and encourages exploration.

The OreMine system ensures that economic pressure never becomes static. Harvesters must constantly adapt to new nodes and depleted ones. This means the AI economy remains active and strategically relevant across long simulation periods.

---

## HarvesterAI: Mining FSM, Safety, and Flow Fields

### HarvesterAI.cs

The HarvesterAI is one of the strongest highlights of this project. It is not a simple move-and-mine agent. Instead, it is a multi-state FSM with safety logic, dynamic retargeting, and anti-stuck behaviors, making it feel intelligent and resilient.

FSM states:

- Idle: Searches for resources using QuadTree queries.
- SeekingOre: Moves toward a selected resource, checking for stuck states and rerouting if a better node is found.
- Mining: Extracts ore at a configured rate and switches to Returning or new mining targets based on cargo and node depletion.
- Returning: Navigates back to the nearest base and deposits ore.
- Fleeing: Activated when danger is detected, moving toward a safe zone.
- Waiting: Pauses at safe zones before returning to work, avoiding immediate re-engagement.

Threat detection:

- DetectDanger scans a radius using the QuadTree. It ignores friendly harvesters but reacts to any enemy targetable unit.
- When danger is detected, the harvester clears its mining target, requests area clearance, and switches to fleeing behavior.
- The system uses a fleeCount to detect repeated panic events near the same location. After multiple detections, it escalates to the Commander via RequestAreaClearance.

Safety-aware mining:

- IsNodeSafe checks for enemy units near potential mining nodes using the QuadTree. If hostile presence is detected, the node is rejected and a report is sent to the commander.
- This adds a risk assessment layer, preventing the harvester from walking into ambushes and encouraging tactical mining choices.

Stuck recovery:

- The harvester tracks its position every 0.5 seconds. If displacement is below a threshold for too long, it assumes it is stuck.
- Stuck nodes are temporarily blacklisted with a cooldown, forcing the harvester to find a different node and preventing loops.

Dynamic retargeting:

- The harvester periodically calls OptimizeOreTarget. If a closer and safe node appears, it switches targets.
- This ensures responsiveness to dynamic resource spawns and avoids long travel when a nearer node becomes available.

Flocking-inspired navigation:

- CalculateHarvesterFlow blends a direct path to the target with a separation force that keeps harvesters from clumping.
- The separation force includes a small tangential component, creating a subtle swirl effect that reduces head-on deadlocks.

Return logic and deposit:

- The harvester dynamically chooses the closest base, including expansion bases, to reduce travel time.
- When within dropOffReach, ore is deposited into the Commander economy and cargo is reset.

These behaviors make harvesters feel alive and purposeful rather than deterministic drones. The combination of safety scanning, dynamic retargeting, and local flow steering gives economic units behaviour beyond a simple move-and-mine loop.

---

## TroopAI: Combat FSM and Boids-Style Flocking

### TroopAI.cs

TroopAI implements a combat unit with an FSM, flocking behavior, and a decision tree that ensures economic units (harvesters) receive right of way. This adds believable unit coordination and avoids pathing chaos.

FSM states:

- Rallying: Units move toward a rally point to form a cohesive group near the base.
- Guarding: Units hold a specific defense point and remain alert for enemies.
- Attacking: Units pursue and fight enemy targets within weapon range.
- FallingBack: Units retreat to a safe zone when enemy influence is too high.
- PlayerCommand: Units obey player-issued move orders and still engage enemies when appropriate.

Combat logic:

- ScanForEnemies uses a QuadTree query within a vision range and chooses the closest valid enemy target.
- Attack uses a weapon cooldown timer and deals damage when in range.
- If a target dies, the troop checks whether it is still marching toward a command position; otherwise it returns to rallying and re-enters the Commander pool.

Flocking (boids-style):

- CalculateFlocking computes separation and cohesion using neighbors in a flockRadius obtained from the QuadTree.
- Separation pushes away from nearby allies with distance-weighted force. Cohesion pulls toward the average neighbor position.
- A targetWeight scales the intent toward a destination, ensuring that the group still progresses toward goals.
- The method returns a normalized blended direction, enabling smooth and organic group movement.

Dynamic stop radius:

- The stop radius expands based on currentFlockCount. This reduces crowding and allows larger clusters to settle without jitter.

Harvester yield decision tree:

- EvaluateHarvesterYieldTree scans a small radius for friendly harvesters. If found, it generates a vector that pushes the troop away.
- This enforces a low-level traffic rule: economic units have priority over combat units. It reduces collisions and ensures harvester flow is uninterrupted.

Safety integration:

- When rallying or guarding, troops check the influence heatmap. If enemy influence exceeds a threshold, they transition into FallingBack automatically.
- This is not a simple health-based retreat. It uses spatial threat estimation, which is more strategic and proactive.

Player command integration:

- SetPlayerCommand allows direct RTS control while keeping flocking and scanning active. Units still behave intelligently, maintaining formations and reacting to threats.

TroopAI demonstrates a strong blend of AI ideas: FSM control, flocking algorithms, utility-like checks (yield decision), and environmental reasoning through the influence map. This combination yields emergent, coordinated behavior that is higher-level than typical unit scripts.

---

## TurretAI and ExpansionBaseAI: Static Defense and Survival

### TurretAI.cs

Turrets are static defensive agents with clear, efficient scanning and firing behavior. They integrate into the same AI ecosystem using the QuadTree and ITargetable interface.

Key behaviors:

- ScanAndFire queries a rectangular area around the turret using the QuadTree. This is faster than scanning the entire entity list.
- The turret selects the closest enemy within attackRange and fires after cooldown.
- Rotation is aligned to the target direction for visual clarity and combat feedback.
- A laser line renderer displays a brief shot effect with a faction-specific color.
- Repairs are throttled to prevent stacking. This uses a cooldown window similar to the base and expansion base repairs.

Turrets also emit influence in the heatmap. The CustomMapManager applies a strong influence weight for turrets, which means they form defensive "heat islands" that alter AI decisions at the strategic level.

### ExpansionBaseAI.cs

Expansion bases provide a second level of survivability and economy scaling. They are fully targetable and integrate with the commander for repairs, production, and eventual survival transfer.

Key features:

- Each base tracks health, displays alpha-based damage, and can be repaired with throttling.
- Upon destruction, it is removed from the SimulationManager and the Commander's expansion list.
- Expansion bases have their own spawn timers and can produce units via the Commander (ProcessBaseProduction).

The expansion system becomes strategically powerful because it is tied to:

- The Commander's dynamic safe zone radius and perimeter balancing.
- The ability to "transfer" the faction HQ to an expansion base if the main base is destroyed, preserving the faction with a new anchor point.

Together, TurretAI and ExpansionBaseAI provide the necessary defensive and territorial pillars that transform a simple skirmish into a long-term strategic simulation.

---

## CustomMapManager: Influence Heatmap and Safety Logic

### CustomMapManager.cs and PathNode

The CustomMapManager provides a grid-based influence map and multiple safety utilities that underpin strategy, defense, and retreat logic. It is the main strategic sensor in the codebase.

Influence map:

- The grid is built from a configurable world size and node radius.
- Each node tracks per-faction influence values (three separate channels for factions 0, 1, and 2).
- Influence decays over time at a uniform rate.
- Each frame, influence is injected based on all targetable entities. Different entity types apply different radii and power multipliers:
  - Turrets: highest power, larger radius.
  - Commanders and expansion bases: moderate power and radius.
  - Troops: standard power.
  - Harvesters are excluded to prevent economic units from falsely inflating control.

Falloff behavior:

- Influence uses a quadratic falloff: power scales with (1 - dist / radius)^2. This produces soft gradients instead of hard edges.
- A max heat cap prevents influence values from growing without bound, which stabilizes long-term simulations.

Safety utilities:

- GetPerimeterSecuredPercentage samples points around a circular ring to estimate defensive control.
- IsPerimeterSecured uses this sampling to determine if a base has enough coverage.
- GetWeakestDefensePointNearBase identifies a low-influence position on a perimeter ring, with random offsets to avoid predictable points.
- GetNearestSafeZone searches a large radius around a unit and returns a location with low enemy influence and adequate friendly influence. This is used by harvesters when fleeing.

Obstacles and walkability:

- Obstacles are collected by scanning for "Wall" objects at startup and caching their bounds.
- Node walkability is computed using IsPositionBlocked, which checks against obstacle bounds.

Debug visualization:

- Gizmos render a heatmap in the editor, with color channels representing each faction. This is the main tool for debugging AI behaviour and for demonstrating the system.

The influence map creates a strategic environment where AI decisions are not purely local. Units can sense global control patterns, and the Commander can evaluate which areas are safe or contested.

---

## FactionCommander: Strategy, Production, and Warfare

### FactionCommander.cs

The FactionCommander is the macro-level AI brain. It is a complex system combining economy, production queues, defensive orchestration, and offensive planning. It uses randomized parameters to create emergent faction personalities, which is a strong design choice for replayability.

Personality parameters:

- ai_desiredHarvesters, ai_safeZoneSize, ai_baseTaskForceSize, ai_turretPreference are randomized at start.
- These values produce distinct commander styles such as aggressive blitz, turtle defense, or balanced industry.
- currentPatienceMultiplier and attack cooldowns vary the timing of attacks.

Economy and production pipeline:

- The Commander manages ore, harvesters, turrets, troops, and expansion bases.
- The ProcessBaseProduction method executes a full priority-based production logic per base:
  1) Emergency repairs if base health is low.
  2) Repair damaged turrets if safe.
  3) Spawn emergency harvesters if none exist.
  4) Build a defensive troop buffer if safe zone size is below target.
  5) Build expansion bases when strong enough and wealthy.
  6) Spawn harvesters if the map has surplus ore or the bank is high.
  7) Build turrets if local influence indicates danger.
  8) Otherwise, choose between turrets or troops based on turret preference.

This priority tree lets the economy adapt to current conditions.

Dynamic safe zone and perimeter defense:

- The Commander maintains a currentSafeZoneRadius that grows when the area is secure and shrinks on intrusion.
- The perimeter is checked using the influence map, and troops are reassigned to weak spots.
- RebalancePerimeter redistributes troops to maintain a ring-like defense, including logic to dispatch a soldier to critical gaps.

Threat reaction and resentment:

- When attacked, the Commander sets isProvoked and increases resentmentLevel.
- Resentment decays over time but can reduce the perceived resistance threshold, encouraging retaliation.
- CallForHelp and RequestAreaClearance dispatch troops to conflict points and remove them from the gathering pool.

Resource warfare:

- Harvesters can report hostile mines. The Commander records reports and responds after repeated alerts.
- A mine protection routine dispatches troops to guard vulnerable resources when the economy is low but security is high.

Offensive planning and task force launches:

- The Commander evaluates strategy at randomized intervals.
- It calculates perceived resistance by sampling enemy influence along a line to the target base.
- It computes a dynamicTaskForceTarget based on resistance, personality, and resentment.
- It launches attacks only if enough troops are available beyond a required home garrison.
- Attack types include retaliatory strikes, opportunistic skirmishes, desperation strikes, and methodical assaults.

Base survival and hive-mind transfer:

- If the main base is destroyed, the Commander can transfer its position to an expansion base, preserving the faction.
- If no expansion bases remain, all assets are terminated.

The commander covers the layers found in many RTS AIs: economy, defense, offense, expansion, and emergent personality. Influence maps and adaptive thresholds keep its behaviour consistent.

---

## Emergent Behaviour and Robustness

### Emergent behaviors

The interaction of all systems produces emergent results beyond any single script:

- Harvesters naturally form mining flows due to separation and tangential avoidance, reducing clustering without explicit pathfinding.
- Troops form cohesive packs that tighten and loosen based on flock size, producing more realistic formations.
- Defense lines stabilize because the Commander constantly rebalances around a perimeter ring based on heatmap data.
- Resource warfare emerges because miners report danger and commanders dispatch guards, tying economy to military response.
- Faction personalities lead to distinct play styles, which creates replay value and makes the AI appear more human.

### Design highlights

1. **Two spatial layers.** The QuadTree handles local perception (who is near me right now). The influence heatmap handles strategic reasoning (who controls this area).
2. **Micro, mid and macro AI in one loop.** Steering and FSMs in units, spatial sensing in the map and tree, strategy in the commander.
3. **A living economy.** Harvesters, ore spawning and base expansion keep resource pressure changing. The commander can move its HQ to an expansion base to survive losing the main base.
4. **Boids-style combat movement.** Troops move as packs using separation and cohesion instead of marching in lines.
5. **Threat-aware mining.** Harvesters check node safety, flee early, escalate repeated danger to the commander and blacklist bad nodes.
6. **Adaptive offence.** Attack decisions depend on resources, sampled resistance along the path and a resentment value that rises when the faction is attacked.

### Robustness

The system is robust due to:

- Stale object checks in the QuadTree.
- Per-frame rebuild of spatial index to ensure no stale positions.
- Repair throttling to avoid exploitative behavior.
- Safe zone logic to keep the AI from blindly walking into high-threat zones.
