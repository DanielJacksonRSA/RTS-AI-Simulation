using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class FactionCommander : MonoBehaviour, ITargetable
{
    public Vector2 Position => transform.position;
    public int FactionID => factionID;
    public float Health => health;
    
    [Header("Faction Details")]
    public int factionID = 1;
    public SimulationManager simManager;
    public ResourceManager resourceManager;
    private CustomMapManager mapManager;
    public FactionCommander enemyCommander;

    [Header("Base Health & Visuals")]
    public float maxHealth = 500f;
    public float health = 500f;
    public SpriteRenderer fillSprite;

    [Header("Repair Throttle")]
    private float nextAllowedRepairTime = 0f;
    public float repairThrottlingInterval = 1.0f; 

    [Header("Economy Trackers")]
    public float totalOre = 0f;
    public List<HarvesterAI> activeHarvesters = new List<HarvesterAI>();
    public List<TurretAI> activeTurrets = new List<TurretAI>();

    [Header("Base Expansion")]
    public ExpansionBaseAI expansionBasePrefab;
    public float expansionBaseCost = 600f; 
    public float expansionBaseCooldown = 45f;
    public List<ExpansionBaseAI> expansionBases = new List<ExpansionBaseAI>();

    [Header("Production Costs & Timers")]
    public HarvesterAI harvesterPrefab;
    public float harvesterCost = 150f;
    public float harvesterCooldown = 10f;
    public int maxHarvesters = 6;           
    public float surplusThreshold = 300f;   

    public TurretAI turretPrefab;
    public float turretCost = 100f;
    public float turretCooldown = 8f;
    public int maxTurrets = 10;

    public float repairCost = 25f;
    public float repairAmount = 50f;
    public float repairCooldown = 1.5f;

    public TroopAI troopPrefab;
    public float troopCost = 50f;
    public float troopCooldown = 5f;

    public Transform spawnPoint;
    public Transform rallyPoint;
    private float spawnTimer = 0f; 

    [Header("RA2 Task Force Settings")]
    public int maxArmyCap = 30;               
    private int dynamicTaskForceTarget;       
    
    public List<TroopAI> gatheringTaskForce = new List<TroopAI>();
    public List<TroopAI> allActiveTroops = new List<TroopAI>();

    [Header("Strategic AI Personality")]
    public bool isProvoked = false;           
    private float strategyEvaluationTimer = 0f;
    private float nextStrategyEvaluationTime = 2.0f; 
    private float currentPatienceMultiplier = 1.0f;  
    private float perimeterRebalanceTimer = 0f;

    // Extended cooldown windows to pace attacks during growth.
    [Header("Offensive Attack Cooldowns")]
    public float offensiveCooldownTimer = 0f;
    public float minAttackCooldown = 30f;
    public float maxAttackCooldown = 75f;

    [Header("Resentment System")]
    public float resentmentLevel = 0f;        
    public float resentmentDecayRate = 0.5f;  

    [Header("Dynamic Expanding Safety Zone")]
    public float currentSafeZoneRadius = 5.0f; 
    public float maxSafeZoneRadius = 25.0f;    
    public float zoneExpansionRate = 0.5f;     

    [Header("Resource Warfare Matrix")]
    public float lowResourceThreshold = 100f;  
    private float lastMineProtectTime = 0f;    
    private Dictionary<Vector2, int> hostileMineReports = new Dictionary<Vector2, int>();

    private int ai_desiredHarvesters;
    private int ai_safeZoneSize;
    private int ai_baseTaskForceSize;
    private float ai_turretPreference; 
    private string commanderPersonalityType;

    void Start()
    {
        health = maxHealth;
        if (fillSprite == null) fillSprite = GetComponent<SpriteRenderer>();
        UpdateHealthVisual();

        mapManager = FindFirstObjectByType<CustomMapManager>();
        simManager.RegisterEntity(this);

        ai_desiredHarvesters = Random.Range(1, 4);      
        ai_safeZoneSize = Random.Range(2, 7);           
        ai_baseTaskForceSize = Random.Range(4, 10);     
        ai_turretPreference = Random.Range(0.1f, 0.4f); 
        
        currentPatienceMultiplier = Random.Range(0.8f, 1.5f);
        offensiveCooldownTimer = Random.Range(minAttackCooldown, maxAttackCooldown);

        if (ai_baseTaskForceSize >= 8) commanderPersonalityType = "Aggressive Blitz Mastermind";
        else if (ai_turretPreference >= 0.3f) commanderPersonalityType = "Turtle Defensive Strategist";
        else commanderPersonalityType = "Balanced Industrialist";

        dynamicTaskForceTarget = ai_baseTaskForceSize; 
        SpawnHarvesterAt(spawnPoint.position);
    }

    public List<Vector2> GetAllBasePositions()
    {
        List<Vector2> bases = new List<Vector2> { Position };
        foreach (var b in expansionBases) if (b != null) bases.Add(b.Position);
        return bases;
    }

    public float GetEmpireSecurityPercentage()
    {
        if (mapManager == null) return 1.0f;
        float totalSecured = 0f;
        List<Vector2> bases = GetAllBasePositions();
        
        if (bases.Count == 0) return 1.0f;

        foreach (var bp in bases)
        {
            totalSecured += mapManager.GetPerimeterSecuredPercentage(bp, currentSafeZoneRadius, factionID, 1.5f);
        }
        return totalSecured / bases.Count;
    }

    Vector2? GetBestEnemyBaseTarget()
    {
        Vector2 bestTarget = Vector2.zero;
        float closestDist = float.MaxValue;
        bool foundAnyEnemy = false;

        FactionCommander[] allCmds = FindObjectsByType<FactionCommander>(FindObjectsSortMode.None);
        foreach (var cmd in allCmds)
        {
            if (cmd != null && cmd.FactionID != this.factionID && cmd.Health > 0)
            {
                float d = Vector2.Distance(Position, cmd.Position);
                if (d < closestDist) { closestDist = d; bestTarget = cmd.Position; foundAnyEnemy = true; }
            }
        }

        ExpansionBaseAI[] allExps = FindObjectsByType<ExpansionBaseAI>(FindObjectsSortMode.None);
        foreach (var exp in allExps)
        {
            if (exp != null && exp.FactionID != this.factionID && exp.Health > 0)
            {
                float d = Vector2.Distance(Position, exp.Position);
                if (d < closestDist) { closestDist = d; bestTarget = exp.Position; foundAnyEnemy = true; }
            }
        }

        if (foundAnyEnemy) return bestTarget;
        return null; 
    }

    public bool RepairMainBase(float amount)
    {
        if (Time.time < nextAllowedRepairTime) return false;

        health = Mathf.Min(maxHealth, health + amount);
        UpdateHealthVisual();

        nextAllowedRepairTime = Time.time + repairThrottlingInterval;
        return true;
    }

    void Update()
    {
        activeHarvesters.RemoveAll(h => h == null);
        activeTurrets.RemoveAll(t => t == null);
        gatheringTaskForce.RemoveAll(t => t == null); 
        allActiveTroops.RemoveAll(t => t == null);
        expansionBases.RemoveAll(b => b == null);

        int baseCount = 1 + expansionBases.Count;
        int currentMaxArmyCap = maxArmyCap * baseCount;

        if (resentmentLevel > 0) resentmentLevel = Mathf.Max(0, resentmentLevel - (Time.deltaTime * resentmentDecayRate));

        if (offensiveCooldownTimer > 0f) offensiveCooldownTimer -= Time.deltaTime;

        strategyEvaluationTimer += Time.deltaTime;
        if (strategyEvaluationTimer > nextStrategyEvaluationTime)
        {
            EvaluateStrategy(currentMaxArmyCap);
            strategyEvaluationTimer = 0f;
            nextStrategyEvaluationTime = Random.Range(2.0f, 6.0f); 
        }

        bool intrusionDetected = CheckSafetyZoneIntrusions(out Vector2 intrusionPoint);
        bool baseIsSafe = IsBaseSafe();
        perimeterRebalanceTimer += Time.deltaTime;

        float dynamicMaxRadius = Mathf.Min(maxSafeZoneRadius, 5.0f + (allActiveTroops.Count * 0.8f));

        if (intrusionDetected)
        {
            currentSafeZoneRadius = Mathf.Max(5.0f, currentSafeZoneRadius - (Time.deltaTime * zoneExpansionRate * 4f));
            if (perimeterRebalanceTimer >= 1.0f && gatheringTaskForce.Count > 0)
            {
                DispatchGuardsToWeakPoint(intrusionPoint, Mathf.Min(3, gatheringTaskForce.Count));
                perimeterRebalanceTimer = 0f;
            }
        }
        else if (baseIsSafe)
        {
            bool allPerimetersSecure = true;
            foreach (var bp in GetAllBasePositions())
            {
                if (!mapManager.IsPerimeterSecured(bp, currentSafeZoneRadius, factionID, 1.5f)) allPerimetersSecure = false;
            }

            if (currentSafeZoneRadius < dynamicMaxRadius && allPerimetersSecure) currentSafeZoneRadius += Time.deltaTime * zoneExpansionRate;
            else if (currentSafeZoneRadius > dynamicMaxRadius) currentSafeZoneRadius = Mathf.Max(5.0f, currentSafeZoneRadius - (Time.deltaTime * zoneExpansionRate * 2f));
        }
        
        if (perimeterRebalanceTimer >= 2.0f) { RebalancePerimeter(); perimeterRebalanceTimer = 0f; }

        lastMineProtectTime += Time.deltaTime;
        if (totalOre < lowResourceThreshold && gatheringTaskForce.Count >= 2 && lastMineProtectTime > 20f) ProtectNearestMine();

        ProcessBaseProduction(ref spawnTimer, Position, spawnPoint.position, true, null);

        foreach (var expBase in expansionBases)
        {
            if (expBase != null)
            {
                ProcessBaseProduction(ref expBase.spawnTimer, expBase.Position, expBase.spawnPoint.position, false, expBase);
            }
        }
    }

    void ProcessBaseProduction(ref float timer, Vector2 basePos, Vector2 spawnPos, bool isMainBase, ExpansionBaseAI expBase)
    {
        timer += Time.deltaTime;
        
        bool baseIsSafeLocally = true; 
        PathNode node = mapManager.NodeFromWorldPoint(basePos);
        if (node != null)
        {
            float myInf = mapManager.GetMyInfluence(node, factionID);       
            float enemyInf = mapManager.GetEnemyInfluence(node, factionID); 
            if (myInf < 10.0f || enemyInf > 2.0f) baseIsSafeLocally = false;
        }

        float currentHealth = isMainBase ? health : expBase.Health;
        float currentMaxHealth = isMainBase ? maxHealth : expBase.maxHealth;

        bool baseNeedsAssertiveRepair = (baseIsSafeLocally && currentHealth < currentMaxHealth);
        bool baseNeedsEmergencyRepair = (!baseIsSafeLocally && currentHealth < currentMaxHealth * 0.4f && currentHealth > 0);
        
        int baseCount = 1 + expansionBases.Count;
        int currentMaxHarvesters = maxHarvesters * baseCount;
        int currentMaxTurrets = maxTurrets * baseCount;
        int currentMaxArmyCap = maxArmyCap * baseCount;

        TurretAI damagedTurret = null;
        if (baseIsSafeLocally) foreach (var t in activeTurrets) if (t.Health < t.maxHealth) { damagedTurret = t; break; }

        bool needsEmergencyHarvester = activeHarvesters.Count == 0;
        bool buildingSafeZone = allActiveTroops.Count < (ai_safeZoneSize * baseCount); 
        bool needsSecondHarvester = activeHarvesters.Count < (ai_desiredHarvesters * baseCount); 
        bool mapHasExcessOre = resourceManager != null && (resourceManager.activeNodes.Count > activeHarvesters.Count * 2);
        bool bankHasSurplus = totalOre >= surplusThreshold;

        if (baseNeedsAssertiveRepair || baseNeedsEmergencyRepair)
        {
            if (totalOre >= repairCost && timer >= repairCooldown) { 
                bool repairExecuted = false;
                if (isMainBase) repairExecuted = RepairMainBase(repairAmount);
                else repairExecuted = expBase.Repair(repairAmount);

                if (repairExecuted)
                {
                    totalOre -= repairCost; 
                    timer = 0f; 
                }
            }
        }
        else if (damagedTurret != null)
        {
            if (totalOre >= repairCost && timer >= repairCooldown) { 
                if (damagedTurret.Repair(repairAmount))
                {
                    totalOre -= repairCost; 
                    timer = 0f; 
                }
            }
        }
        else if (needsEmergencyHarvester)
        {
            if (totalOre >= harvesterCost && timer >= harvesterCooldown) { totalOre -= harvesterCost; SpawnHarvesterAt(spawnPos); timer = 0f; }
        }
        else if (buildingSafeZone)
        {
            if (totalOre >= troopCost && timer >= troopCooldown) { totalOre -= troopCost; SpawnTroopAt(spawnPos); timer = 0f; }
        }
        else if (totalOre >= expansionBaseCost && timer >= expansionBaseCooldown && activeTurrets.Count >= 2 && expansionBases.Count < 2)
        {
            Vector2 expandSpot = FindExpansionSpot(basePos);
            if (expandSpot != basePos) 
            {
                totalOre -= expansionBaseCost;
                timer = 0f;
                
                ExpansionBaseAI newBase = Instantiate(expansionBasePrefab, expandSpot, Quaternion.identity);
                newBase.Initialize(simManager, this, factionID);
                expansionBases.Add(newBase);
                
                SpawnHarvesterAt(newBase.spawnPoint.position); 
                GenerateTacticalLog("Expanding Territory: Built New Base", 0, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));
            }
        }
        else if ((needsSecondHarvester || mapHasExcessOre || bankHasSurplus) && activeHarvesters.Count < currentMaxHarvesters)
        {
            if (totalOre >= harvesterCost && timer >= harvesterCooldown) { totalOre -= harvesterCost; SpawnHarvesterAt(spawnPos); timer = 0f; }
        }
        else if (!baseIsSafeLocally && activeTurrets.Count < currentMaxTurrets)
        {
            if (totalOre >= turretCost && timer >= turretCooldown) { totalOre -= turretCost; SpawnTurretNear(basePos); timer = 0f; }
        }
        else
        {
            bool wantTurret = (activeTurrets.Count < currentMaxTurrets) && (Random.value < ai_turretPreference);
            if (wantTurret)
            {
                if (totalOre >= turretCost && timer >= turretCooldown) { totalOre -= turretCost; SpawnTurretNear(basePos); timer = 0f; }
            }
            else
            {
                if (totalOre >= troopCost && timer >= troopCooldown && allActiveTroops.Count < currentMaxArmyCap) { totalOre -= troopCost; SpawnTroopAt(spawnPos); timer = 0f; }
            }
        }
    }

    private void GenerateTacticalLog(string actionTrigger, int amountSent, int perceivedResistance)
    {
        StringBuilder logMessage = new StringBuilder();
        logMessage.AppendLine($"=== [FACTION {factionID} STRATEGIC DIRECTIVE] ===");
        logMessage.AppendLine($"Decision Trigger: {actionTrigger}");
        if (amountSent > 0) logMessage.AppendLine($"Deployment Force: Sending {amountSent} Troops");
        logMessage.AppendLine($"Current Safety Perimeter Radius Claimed: {currentSafeZoneRadius:F1} World Units.");
        logMessage.AppendLine($"Economic Status: {totalOre} Credits | {activeHarvesters.Count} Miners | {expansionBases.Count + 1} HQs");
        logMessage.AppendLine($"Fog of War Data: Path Heatmap Resistance Evaluated at Level {perceivedResistance}.");
        logMessage.AppendLine("=======================================================");
        Debug.Log(logMessage.ToString());
    }

    Vector2 FindExpansionSpot(Vector2 origin)
    {
        if (activeTurrets.Count > 0 && Random.value > 0.5f) origin = activeTurrets[Random.Range(0, activeTurrets.Count)].Position;
        
        for (int i = 0; i < 20; i++)
        {
            Vector2 candidate = origin + (Random.insideUnitCircle.normalized * Random.Range(6f, 10f));
            if (!mapManager.IsPositionBlocked(candidate) && mapManager.NodeFromWorldPoint(candidate) != null) return candidate;
        }
        
        Vector2 fallback = origin + (Random.insideUnitCircle.normalized * 5f);
        if (mapManager != null)
        {
            float minX = mapManager.transform.position.x - (mapManager.gridWorldSize.x / 2f) + 2.0f;
            float maxX = mapManager.transform.position.x + (mapManager.gridWorldSize.x / 2f) - 2.0f;
            float minY = mapManager.transform.position.y - (mapManager.gridWorldSize.y / 2f) + 2.0f;
            float maxY = mapManager.transform.position.y + (mapManager.gridWorldSize.y / 2f) - 2.0f;
            
            fallback.x = Mathf.Clamp(fallback.x, minX, maxX);
            fallback.y = Mathf.Clamp(fallback.y, minY, maxY);
        }
        return fallback;
    }

    void ProtectNearestMine()
    {
        if (resourceManager == null || resourceManager.activeNodes.Count == 0) return;
        
        // Skip mine protection if overall security is low.
        if (GetEmpireSecurityPercentage() < 0.75f) return;

        Resource bestNode = null;
        float closest = float.MaxValue;
        foreach (var node in resourceManager.activeNodes)
        {
            if (node.currentWealth > 0)
            {
                float d = Vector2.Distance(Position, node.Position);
                if (d < closest) { closest = d; bestNode = node; }
            }
        }

        if (bestNode != null)
        {
            int amount = Random.Range(2, Mathf.Max(3, gatheringTaskForce.Count + 1));
            amount = Mathf.Min(amount, gatheringTaskForce.Count); 
            if (amount > 0) 
            { 
                lastMineProtectTime = 0f; 
                GenerateTacticalLog("Securing Vulnerable Ore Mine", amount, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));
                DispatchGuardsToWeakPoint(bestNode.Position, amount); 
            }
        }
    }

    public void ReportHostileMine(Vector2 minePos)
    {
        // Skip reinforcements if overall security is low.
        if (GetEmpireSecurityPercentage() < 0.75f) return;

        Vector2 fuzzyKey = new Vector2(Mathf.Round(minePos.x), Mathf.Round(minePos.y));
        if (hostileMineReports.ContainsKey(fuzzyKey)) hostileMineReports[fuzzyKey]++;
        else hostileMineReports[fuzzyKey] = 1;

        if (hostileMineReports[fuzzyKey] >= 2)
        {
            float enemyInf = 0f;
            PathNode node = mapManager != null ? mapManager.NodeFromWorldPoint(minePos) : null;
            if (node != null) enemyInf = mapManager.GetEnemyInfluence(node, factionID); 

            int amountNeeded = Mathf.CeilToInt(enemyInf * 0.4f) + 2; 
            int amountToSend = Mathf.Min(amountNeeded, gatheringTaskForce.Count);
            
            if (amountToSend >= 2)
            {
                GenerateTacticalLog("Resource Warfare: Hostile Mine Clearance", amountToSend, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));

                List<TroopAI> available = new List<TroopAI>();
                foreach (var t in gatheringTaskForce) if (t.IsAvailableForReassignment()) available.Add(t);
                available.Sort((a, b) => Vector2.Distance(a.Position, minePos).CompareTo(Vector2.Distance(b.Position, minePos)));

                int sent = 0;
                foreach (var troop in available)
                {
                    if (sent >= amountToSend) break;
                    troop.SetGuardMission(minePos);
                    sent++;
                }
                hostileMineReports[fuzzyKey] = 0; 
            }
        }
    }

    bool CheckSafetyZoneIntrusions(out Vector2 intrusionPoint)
    {
        intrusionPoint = Vector2.zero;
        if (simManager == null) return false;

        foreach (var bp in GetAllBasePositions())
        {
            Rect searchArea = new Rect(bp.x - currentSafeZoneRadius, bp.y - currentSafeZoneRadius, currentSafeZoneRadius * 2, currentSafeZoneRadius * 2);
            List<IEntity> nearby = new List<IEntity>();
            simManager.quadTree.Query(searchArea, nearby);

            foreach (var entity in nearby)
            {
                if (entity is ITargetable t && t.FactionID != this.factionID && t.Health > 0 && !(t is HarvesterAI))
                {
                    if (Vector2.Distance(bp, t.Position) <= currentSafeZoneRadius)
                    {
                        intrusionPoint = t.Position;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    bool IsBaseSafe()
    {
        if (mapManager == null) return true;
        foreach (var bp in GetAllBasePositions())
        {
            PathNode node = mapManager.NodeFromWorldPoint(bp);
            if (node != null)
            {
                float myInf = mapManager.GetMyInfluence(node, factionID);       
                float enemyInf = mapManager.GetEnemyInfluence(node, factionID); 
                if (myInf < 10.0f || enemyInf > 2.0f) return false;
            }
        }
        return true;
    }

    int GetPerceivedEnemyResistance(Vector2 enemyPos)
    {
        if (mapManager == null || mapManager.grid == null) return 0;
        float totalThreat = 0f;
        Vector2 start = Position;
        Vector2 end = enemyPos;
        float dist = Vector2.Distance(start, end);
        Vector2 dir = (end - start).normalized;

        for (float d = 0; d < dist; d += 1.0f)
        {
            PathNode node = mapManager.NodeFromWorldPoint(start + dir * d);
            if (node != null) totalThreat += mapManager.GetEnemyInfluence(node, factionID); 
        }
        return Mathf.CeilToInt(totalThreat / 15f); 
    }

    // Adjust attack thresholds based on personality and pressure.
    void EvaluateStrategy(int currentMaxArmyCap)
    {
        Vector2? targetBase = GetBestEnemyBaseTarget();
        if (targetBase == null) return; 

        // Abort offensive planning if security is low.
        if (GetEmpireSecurityPercentage() < 0.75f) return;

        int perceivedResistance = GetPerceivedEnemyResistance(targetBase.Value);
        int angerDiscount = Mathf.FloorToInt(resentmentLevel / 15f);

        bool isDesperate = totalOre <= (lowResourceThreshold * 1.5f); 
        
        // Favor defense when resources are stable.
        float scarcityMultiplier = isDesperate ? 0.7f : 2.0f; 

        dynamicTaskForceTarget = Mathf.Max(ai_baseTaskForceSize, (perceivedResistance + 3) - angerDiscount);
        dynamicTaskForceTarget = Mathf.Min(dynamicTaskForceTarget, currentMaxArmyCap);

        int customizedLaunchThreshold = Mathf.RoundToInt(dynamicTaskForceTarget * currentPatienceMultiplier * scarcityMultiplier);
        
        // Reserve a large home garrison before sending attackers.
        int requiredDefenders = (1 + expansionBases.Count) * 15; 
        int availableAttackers = gatheringTaskForce.Count - requiredDefenders;

        string attackReason = "";
        bool shouldLaunch = false;
        bool isOffensiveAttack = false;

        if (isProvoked && availableAttackers >= Mathf.Max(2, ai_baseTaskForceSize / 2)) 
        { 
            shouldLaunch = true; 
            attackReason = "Retaliatory Strike"; 
            isProvoked = false; 
            customizedLaunchThreshold = Mathf.Min(availableAttackers, customizedLaunchThreshold);
        }
        else if (offensiveCooldownTimer <= 0f) 
        {
            if (isDesperate && availableAttackers >= customizedLaunchThreshold)
            {
                shouldLaunch = true; attackReason = "Resource Desperation Strike"; isOffensiveAttack = true;
            }
            else if (perceivedResistance <= 1 && availableAttackers >= 10 && Random.value < 0.05f) 
            { 
                customizedLaunchThreshold = availableAttackers; shouldLaunch = true; attackReason = "Opportunistic Skirmish"; isOffensiveAttack = true; 
            }
            else if (currentPatienceMultiplier < 1.0f && availableAttackers >= customizedLaunchThreshold) 
            { 
                shouldLaunch = true; attackReason = "Aggressive Push (Patience Expired)"; isOffensiveAttack = true; 
            }
            else if (currentPatienceMultiplier > 1.2f && availableAttackers >= customizedLaunchThreshold) 
            { 
                shouldLaunch = true; attackReason = "Methodical Overwhelming Assault"; isOffensiveAttack = true; 
            }
            else if (availableAttackers >= customizedLaunchThreshold) 
            { 
                shouldLaunch = true; attackReason = "Calculated Heatmap Projection Met"; isOffensiveAttack = true; 
            }
        }

        if (shouldLaunch && customizedLaunchThreshold > 0)
        {
            GenerateTacticalLog(attackReason, customizedLaunchThreshold, perceivedResistance);
            LaunchTaskForce(customizedLaunchThreshold); 
            currentPatienceMultiplier = Random.Range(0.7f, 1.6f);
            
            if (isOffensiveAttack)
            {
                // Slow the cadence when wealthy; push faster when desperate.
                float cooldownMult = isDesperate ? 0.5f : 2.0f;
                offensiveCooldownTimer = Random.Range(minAttackCooldown * cooldownMult, maxAttackCooldown * cooldownMult);
                Debug.Log($"[Faction {factionID}] Offensive forces resting. Next attack wave randomized to deploy in {offensiveCooldownTimer:F1} seconds.");
            }
        }
    }

    void RebalancePerimeter()
    {
        if (mapManager == null) return;

        Vector2 absoluteWeakSpot = Position;
        float lowestHeat = float.MaxValue;
        bool hasCriticalHole = false;

        foreach (var bp in GetAllBasePositions())
        {
            Vector2 weakSpot = mapManager.GetWeakestDefensePointNearBase(bp, factionID, currentSafeZoneRadius);
            PathNode weakNode = mapManager.NodeFromWorldPoint(weakSpot);
            if (weakNode != null)
            {
                float heat = mapManager.GetMyInfluence(weakNode, factionID);
                if (heat < lowestHeat) { lowestHeat = heat; absoluteWeakSpot = weakSpot; }
                if (heat < 1.0f) hasCriticalHole = true;
            }
        }

        int dispatchedToHole = 0;
        foreach (var troop in gatheringTaskForce)
        {
            if (!troop.IsAvailableForReassignment()) continue;

            bool isGuardingMine = false;
            if (resourceManager != null)
            {
                foreach (var node in resourceManager.activeNodes)
                {
                    if (Vector2.Distance(troop.TargetPos, node.Position) < 2.0f) { isGuardingMine = true; break; }
                }
            }
            
            if (isGuardingMine) continue;

            float closestDistToBase = float.MaxValue;
            foreach (var bp in GetAllBasePositions())
            {
                float d = Vector2.Distance(troop.TargetPos, bp);
                if (d < closestDistToBase) closestDistToBase = d;
            }
            
            bool holdsTheLine = Mathf.Abs(closestDistToBase - currentSafeZoneRadius) <= 1.5f;

            if (!holdsTheLine)
            {
                if (hasCriticalHole && dispatchedToHole < 1)
                {
                    troop.SetGuardMission(absoluteWeakSpot);
                    dispatchedToHole++;
                }
                else
                {
                    Vector2 closestBase = Position;
                    float cDist = float.MaxValue;
                    foreach (var bp in GetAllBasePositions()) { float d = Vector2.Distance(troop.Position, bp); if (d < cDist) { cDist = d; closestBase = bp; } }

                    Vector2 currentDir = (troop.Position - closestBase).normalized;
                    if (currentDir == Vector2.zero) currentDir = Random.insideUnitCircle.normalized;
                    Vector2 pushOutwardPos = closestBase + (currentDir * currentSafeZoneRadius);
                    
                    float minX = mapManager.transform.position.x - (mapManager.gridWorldSize.x / 2f) + 1.5f;
                    float maxX = mapManager.transform.position.x + (mapManager.gridWorldSize.x / 2f) - 1.5f;
                    float minY = mapManager.transform.position.y - (mapManager.gridWorldSize.y / 2f) + 1.5f;
                    float maxY = mapManager.transform.position.y + (mapManager.gridWorldSize.y / 2f) - 1.5f;
                    
                    pushOutwardPos.x = Mathf.Clamp(pushOutwardPos.x, minX, maxX);
                    pushOutwardPos.y = Mathf.Clamp(pushOutwardPos.y, minY, maxY);
                    troop.SetGuardMission(pushOutwardPos);
                }
            }
        }
    }

    public void ReturnToDuty(TroopAI troop)
    {
        if (troop != null && !gatheringTaskForce.Contains(troop) && troop.Health > 0)
        {
            gatheringTaskForce.Add(troop);
        }
    }

    void LaunchTaskForce(int amountToSend)
    {
        Vector2? targetBasePos = GetBestEnemyBaseTarget();
        if (targetBasePos == null) return;

        int sentCount = 0;
        for (int i = gatheringTaskForce.Count - 1; i >= 0; i--)
        {
            if (sentCount >= amountToSend) break;
            TroopAI troop = gatheringTaskForce[i];
            troop.SetAttackMission(targetBasePos.Value);
            gatheringTaskForce.RemoveAt(i); 
            sentCount++;
        }
    }

    void DispatchGuardsToWeakPoint(Vector2 weakSpot, int amount)
    {
        List<TroopAI> available = new List<TroopAI>();
        foreach (var t in gatheringTaskForce) if (t.IsAvailableForReassignment()) available.Add(t);
        available.Sort((a, b) => Vector2.Distance(a.Position, weakSpot).CompareTo(Vector2.Distance(b.Position, weakSpot)));

        int dispatched = 0;
        foreach (var troop in available)
        {
            if (dispatched >= amount) break;
            troop.SetGuardMission(weakSpot);
            dispatched++;
        }
    }

    void SpawnHarvesterAt(Vector2 pos) 
    { 
        HarvesterAI harv = Instantiate(harvesterPrefab, pos, Quaternion.identity); 
        harv.Initialize(simManager, this, factionID); 
        harv.resourceManager = resourceManager; 
        activeHarvesters.Add(harv); 
    }

    void SpawnTurretNear(Vector2 targetBase)
    {
        Vector2 weakSpot = mapManager.GetWeakestDefensePointNearBase(targetBase, factionID, currentSafeZoneRadius);
        Vector3 strictFloorPosition = new Vector3(weakSpot.x, weakSpot.y, 0f);
        
        TurretAI turret = Instantiate(turretPrefab, strictFloorPosition, Quaternion.identity);
        turret.Initialize(simManager, factionID);
        activeTurrets.Add(turret);
    }

    void SpawnTroopAt(Vector2 pos) 
    { 
        TroopAI troop = Instantiate(troopPrefab, pos, Quaternion.identity); 
        troop.Initialize(simManager, this, factionID); 
        allActiveTroops.Add(troop); 
        gatheringTaskForce.Add(troop); 
    }

    public void DepositOre(float amount) { totalOre += amount; }

    public void RequestAreaClearance(Vector2 problemArea)
    {
        int amount = Mathf.Min(2, gatheringTaskForce.Count);
        GenerateTacticalLog("Harvester Distress Call", amount, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));
        for (int i = 0; i < amount; i++)
        {
            int lastIndex = gatheringTaskForce.Count - 1;
            gatheringTaskForce[lastIndex].SetAttackMission(problemArea);
            gatheringTaskForce.RemoveAt(lastIndex);
        }
    }

    public void CallForHelp(Vector2 distressPos, bool isBaseDefense = false)
    {
        isProvoked = true; 
        resentmentLevel = Mathf.Min(100f, resentmentLevel + 20f);

        int amount = isBaseDefense ? gatheringTaskForce.Count : Mathf.Min(2, gatheringTaskForce.Count);
        if (amount > 0)
        {
            GenerateTacticalLog(isBaseDefense ? "Emergency Base Defense Triggered" : "Squad Engaged Enemy", amount, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));
            for (int i = 0; i < amount; i++)
            {
                int lastIndex = gatheringTaskForce.Count - 1;
                gatheringTaskForce[lastIndex].SetAttackMission(distressPos);
                gatheringTaskForce.RemoveAt(lastIndex);
            }
        }
    }

    public void TakeDamage(float amount)
    {
        health -= amount;
        UpdateHealthVisual();
        
        isProvoked = true; 
        resentmentLevel = Mathf.Min(100f, resentmentLevel + 25f);
        if (gatheringTaskForce.Count > 0) CallForHelp(Position, true);

        if (health <= 0) 
        { 
            expansionBases.RemoveAll(b => b == null);

            if (expansionBases.Count > 0)
            {
                GenerateTacticalLog("CRITICAL: MAIN HQ DESTROYED. EXECUTING HIVE-MIND TRANSFER TO EXPANSION BASE.", 0, GetPerceivedEnemyResistance(GetBestEnemyBaseTarget() ?? Position));

                ExpansionBaseAI newBody = expansionBases[0];
                expansionBases.RemoveAt(0);

                transform.position = newBody.Position;
                health = newBody.health;
                maxHealth = newBody.maxHealth;
                spawnPoint = newBody.spawnPoint;
                rallyPoint = newBody.rallyPoint;

                simManager.RemoveEntity(newBody);
                Destroy(newBody.gameObject);
                UpdateHealthVisual();
            }
            else
            {
                GenerateTacticalLog("TOTAL ANNIHILATION: Final HQ Lost. Terminating all faction assets.", 0, 0);

                foreach(var t in allActiveTroops.ToArray()) if(t != null) t.TakeDamage(9999f);
                foreach(var h in activeHarvesters.ToArray()) if(h != null) h.TakeDamage(9999f);
                foreach(var t in activeTurrets.ToArray()) if(t != null) t.TakeDamage(9999f);
                
                simManager.RemoveEntity(this); 
                Destroy(gameObject); 
            }
        }
    }

    private void UpdateHealthVisual()
    {
        if (fillSprite != null)
        {
            Color c = fillSprite.color;
            c.a = Mathf.Clamp01(health / maxHealth);
            fillSprite.color = c;
        }
    }
}