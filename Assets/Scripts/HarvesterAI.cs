using System.Collections.Generic;
using UnityEngine;

public class HarvesterAI : Unit, ITargetable
{
    public float miningRate = 20f;
    public float maxCargo = 100f;
    public float currentCargo = 0f;

    [Header("Mining Settings")]
    public float miningReach = 0.6f;
    public float dropOffReach = 1.2f; 
    public ResourceManager resourceManager;
    private CustomMapManager mapManager;

    [Header("Stuck & Collision Recovery")]
    public float stuckThresholdTime = 2.5f;
    private float stuckTimer = 0f;
    private Vector2 lastCheckPosition;
    private float positionCheckTimer = 0f;
    private Resource blacklistedNode = null;
    private float blacklistCooldown = 0f;

    [Header("Dynamic Proximity Optimization")]
    public float rerouteInterval = 0.5f;
    private float rerouteTimer = 0f;

    private enum State { Idle, SeekingOre, Mining, Returning, Fleeing, Waiting }
    private State currentState = State.Idle;
    
    private Vector2 targetPos;
    private Resource targetNode;
    private Vector2 safeZoneTarget;
    
    private float waitTimer = 0f;
    
    // Extra buffer to detect threats before entering combat range.
    [Header("Early Warning Radar")]
    public float dangerDetectionRadius = 3.8f; 
    private Vector2 lastFleeLocation = Vector2.zero;
    private int fleeCount = 0;

    public override void Initialize(SimulationManager sim, FactionCommander cmd, int faction)
    {
        base.Initialize(sim, cmd, faction);
        mapManager = FindFirstObjectByType<CustomMapManager>();
        avoidOtherUnits = false; 

        lastCheckPosition = Position;
    }

    private Vector2 GetClosestBase()
    {
        if (commander == null) return Position;

        Vector2 closest = commander.Position;
        float minDist = Vector2.Distance(Position, closest);

        foreach (var b in commander.expansionBases)
        {
            if (b != null)
            {
                float d = Vector2.Distance(Position, b.Position);
                if (d < minDist)
                {
                    minDist = d;
                    closest = b.Position;
                }
            }
        }
        return closest;
    }

    void Update()
    {
        if (blacklistCooldown > 0) blacklistCooldown -= Time.deltaTime;
        else blacklistedNode = null;

        // Spot and report threats before entering combat range.
        if (currentState != State.Fleeing && currentState != State.Returning && DetectDanger())
        {
            if (Vector2.Distance(Position, lastFleeLocation) < 4.0f) fleeCount++;
            else
            {
                lastFleeLocation = Position;
                fleeCount = 1;
            }

            if (fleeCount >= 3)
            {
                commander.RequestAreaClearance(Position);
                fleeCount = 0;
            }

            targetNode = null;
            if (mapManager != null) safeZoneTarget = mapManager.GetNearestSafeZone(Position, GetClosestBase(), factionID);
            else safeZoneTarget = GetClosestBase();
            
            currentState = State.Fleeing;
        }

        // Standard state machine for harvesting behavior.
        switch (currentState)
        {
            case State.Idle:
                FindOreUsingQuadTree();
                break;
            
            case State.SeekingOre:
                positionCheckTimer += Time.deltaTime;
                if (positionCheckTimer >= 0.5f)
                {
                    if (Vector2.Distance(Position, lastCheckPosition) < 0.15f) stuckTimer += positionCheckTimer;
                    else stuckTimer = 0f;
                    
                    lastCheckPosition = Position;
                    positionCheckTimer = 0f;
                }

                if (stuckTimer >= stuckThresholdTime)
                {
                    blacklistedNode = targetNode;
                    blacklistCooldown = 7.0f; 
                    stuckTimer = 0f;
                    targetNode = null;
                    currentState = State.Idle; 
                    break;
                }

                rerouteTimer += Time.deltaTime;
                if (rerouteTimer >= rerouteInterval)
                {
                    rerouteTimer = 0f;
                    OptimizeOreTarget();
                }

                if (Vector2.Distance(Position, targetPos) < miningReach)
                {
                    if (rb != null) rb.linearVelocity = Vector2.zero;
                    currentState = State.Mining;
                }
                else
                {
                    MoveInDirection(CalculateHarvesterFlow(targetPos));
                }
                break;
            
            case State.Mining:
                stuckTimer = 0f; 
                
                if (targetNode == null || targetNode.currentWealth <= 0)
                {
                    if (currentCargo >= maxCargo) currentState = State.Returning;
                    else AttemptToFindMoreOre(); 
                    break;
                }

                if (Vector2.Distance(Position, targetNode.Position) > miningReach + 0.1f)
                {
                    currentState = State.SeekingOre;
                    break;
                }

                if (rb != null) rb.linearVelocity = Vector2.zero;

                float mined = resourceManager.Mine(targetNode, miningRate * Time.deltaTime);
                currentCargo += mined;

                if (currentCargo >= maxCargo) currentState = State.Returning;
                else if (targetNode.currentWealth <= 0) AttemptToFindMoreOre(); 
                break;
            
            case State.Returning:
                Vector2 targetBase = GetClosestBase();
                if (Vector2.Distance(Position, targetBase) <= dropOffReach)
                {
                    commander.DepositOre(currentCargo);
                    currentCargo = 0f;
                    currentState = State.Idle;
                }
                else
                {
                    MoveInDirection(CalculateHarvesterFlow(targetBase));
                }
                break;

            case State.Fleeing:
                if (Vector2.Distance(Position, safeZoneTarget) < 0.35f)
                {
                    currentState = State.Waiting;
                    waitTimer = 3.5f;
                    if (currentCargo > 0 && Vector2.Distance(Position, GetClosestBase()) <= dropOffReach)
                    {
                        commander.DepositOre(currentCargo);
                        currentCargo = 0f;
                    }
                }
                else
                {
                    MoveInDirection(CalculateHarvesterFlow(safeZoneTarget));
                }
                break;

            case State.Waiting:
                if (rb != null) rb.linearVelocity = Vector2.zero;
                waitTimer -= Time.deltaTime;
                if (waitTimer <= 0) currentState = State.Idle;
                break;
        }
    }

    private void AttemptToFindMoreOre()
    {
        if (simManager == null) return;
        
        Rect searchArea = simManager.simulationBounds;
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        float closestDist = float.MaxValue;
        Resource bestSafeNode = null;

        foreach (var entity in nearby)
        {
            if (entity is Resource node && node.currentWealth > 0 && node != blacklistedNode)
            {
                float d = Vector2.Distance(Position, node.Position);
                if (d < closestDist && IsNodeSafe(node.Position))
                {
                    closestDist = d;
                    bestSafeNode = node;
                }
            }
        }

        if (bestSafeNode != null)
        {
            targetNode = bestSafeNode;
            targetPos = bestSafeNode.Position;
            currentState = State.SeekingOre;
            stuckTimer = 0f;
            lastCheckPosition = Position;
        }
        else
        {
            if (currentCargo > 0) currentState = State.Returning;
            else currentState = State.Idle;
        }
    }

    // Scan a safety radius before committing to a node.
    private bool IsNodeSafe(Vector2 nodePos)
    {
        Rect checkArea = new Rect(nodePos.x - dangerDetectionRadius, nodePos.y - dangerDetectionRadius, dangerDetectionRadius * 2, dangerDetectionRadius * 2);
        List<IEntity> nearbyToNode = new List<IEntity>();
        simManager.quadTree.Query(checkArea, nearbyToNode);

        foreach (var entity in nearbyToNode)
        {
            if (entity is ITargetable t && t.FactionID != this.factionID)
            {
                if (t is HarvesterAI) continue; 
                
                // Report threats so the commander can respond.
                if (commander != null) commander.ReportHostileMine(nodePos);
                return false; 
            }
        }
        return true; 
    }

    private void OptimizeOreTarget()
    {
        if (simManager == null || targetNode == null) return;

        Rect searchArea = simManager.simulationBounds;
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        float currentDist = Vector2.Distance(Position, targetNode.Position);
        float closestDist = currentDist;
        Resource bestNewNode = null;

        foreach (var entity in nearby)
        {
            if (entity is Resource node && node.currentWealth > 0 && node != targetNode && node != blacklistedNode)
            {
                float d = Vector2.Distance(Position, node.Position);
                
                if (d < closestDist - 1.5f && IsNodeSafe(node.Position))
                {
                    closestDist = d;
                    bestNewNode = node;
                }
            }
        }

        if (bestNewNode != null)
        {
            targetNode = bestNewNode;
            targetPos = bestNewNode.Position;
            stuckTimer = 0f;
            lastCheckPosition = Position;
        }
    }

    private Vector2 CalculateHarvesterFlow(Vector2 destination)
    {
        Vector2 desiredDirection = (destination - Position).normalized;
        Rect searchArea = new Rect(Position.x - 0.85f, Position.y - 0.85f, 1.7f, 1.7f);
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        Vector2 separation = Vector2.zero;
        int harvesterCount = 0;

        foreach (var entity in nearby)
        {
            if (entity is HarvesterAI other && other != this && other.FactionID == this.FactionID)
            {
                float dist = Vector2.Distance(Position, other.Position);
                if (dist > 0 && dist < 0.85f)
                {
                    Vector2 away = (Position - other.Position).normalized;
                    Vector2 tangent = new Vector2(-away.y, away.x) * 0.8f; 
                    separation += (away + tangent) * (1f / dist);
                    harvesterCount++;
                }
            }
        }

        if (harvesterCount > 0) return (desiredDirection + (separation.normalized * 1.5f)).normalized;
        return desiredDirection;
    }

    // Active reconnaissance while moving.
    bool DetectDanger()
    {
        Rect searchArea = new Rect(Position.x - dangerDetectionRadius, Position.y - dangerDetectionRadius, dangerDetectionRadius * 2, dangerDetectionRadius * 2);
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        foreach (var entity in nearby)
        {
            if (entity is ITargetable t && t.FactionID != this.factionID)
            {
                if (t is HarvesterAI) continue; 
                
                // If heading toward a node, flag it. Otherwise flag enemy patrol coordinate.
                Vector2 coordinatesToFlag = (targetNode != null) ? targetNode.Position : t.Position;
                if (commander != null) commander.ReportHostileMine(coordinatesToFlag);
                
                return true;
            }
        }
        return false;
    }

    void FindOreUsingQuadTree()
    {
        Rect searchArea = simManager.simulationBounds;
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        float closestDist = float.MaxValue;
        bool found = false;

        foreach (var entity in nearby)
        {
            if (entity is Resource node && node.currentWealth > 0 && node != blacklistedNode)
            {
                float d = Vector2.Distance(Position, node.Position);
                
                if (d < closestDist && IsNodeSafe(node.Position))
                {
                    closestDist = d;
                    targetPos = node.Position;
                    targetNode = node;
                    found = true;
                }
            }
        }

        if (found) 
        {
            currentState = State.SeekingOre;
            stuckTimer = 0f;
            lastCheckPosition = Position;
            positionCheckTimer = 0f;
        }
    }

    public override void TakeDamage(float amount)
    {
        commander.CallForHelp(Position, false);
        targetNode = null;
        
        if (mapManager != null) safeZoneTarget = mapManager.GetNearestSafeZone(Position, GetClosestBase(), factionID);
        else safeZoneTarget = GetClosestBase();
        
        currentState = State.Fleeing;
        base.TakeDamage(amount);
    }
}