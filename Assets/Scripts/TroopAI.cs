using System.Collections.Generic;
using UnityEngine;

public class TroopAI : Unit, ITargetable
{
    private enum State { Rallying, Guarding, Attacking, FallingBack, PlayerCommand }
    private State currentState = State.Rallying;
    
    private Vector2 targetPos = Vector2.zero;
    public Vector2 TargetPos => targetPos; 

    public bool isSelected = false; 

    [Header("Combat Stats")]
    // Health is managed by the base Unit class.
    public float attackDamage = 15f;
    public float attackRate = 1.0f;       
    public float weaponRange = 0.5f;      
    public float visionRange = 3.5f;      
    
    private float attackTimer = 0f;
    private ITargetable currentTarget;    
    private CustomMapManager mapManager; 

    [Header("Miniature Flocking Settings")]
    public float flockRadius = 1.5f;      
    public float separationWeight = 1.2f; 
    public float cohesionWeight = 0.8f;   
    public float targetWeight = 2.0f;     

    [Header("Decision Tree Settings")]
    public float harvesterYieldRadius = 1.0f; 

    private int currentFlockCount = 0; 
    public int FactionID => factionID;
    public float Health => health; // Exposes base Unit health for ITargetable.

    public override void Initialize(SimulationManager sim, FactionCommander cmd, int faction)
    {
        base.Initialize(sim, cmd, faction);
        health = maxHealth; // Initialize base Unit health.
        avoidOtherUnits = false; 
        mapManager = FindFirstObjectByType<CustomMapManager>();
    }

    void Update()
    {
        Vector2 giveWayVector = EvaluateHarvesterYieldTree();
        
        if (giveWayVector != Vector2.zero)
        {
            MoveInDirection(giveWayVector);
            return; 
        }

        if (currentState == State.Guarding || currentState == State.Rallying)
        {
            if (mapManager != null)
            {
                PathNode localNode = mapManager.NodeFromWorldPoint(Position);
                if (localNode != null)
                {
                    float enemyHeat = mapManager.GetEnemyInfluence(localNode, factionID); 
                    
                    if (enemyHeat > 20.0f) 
                    {
                        currentState = State.FallingBack;
                    }
                }
            }
        }

        switch(currentState)
        {
            case State.Rallying:
                if (targetPos == Vector2.zero)
                    targetPos = (Vector2)commander.rallyPoint.position + (Random.insideUnitCircle * 1.5f);

                float rallyStopRadius = 0.2f + (currentFlockCount * 0.15f);

                if (Vector2.Distance(Position, targetPos) > rallyStopRadius)
                {
                    Vector2 flockingDirection = CalculateFlocking(targetPos, separationWeight, cohesionWeight);
                    MoveInDirection(flockingDirection);
                }
                else
                {
                    if (rb != null) rb.linearVelocity = Vector2.zero; 
                }
                
                ScanForEnemies();
                break;
                
            case State.Guarding:
                float guardStopRadius = 0.2f + (currentFlockCount * 0.15f);

                if (Vector2.Distance(Position, targetPos) > guardStopRadius)
                {
                    Vector2 flockingDirection = CalculateFlocking(targetPos, separationWeight, cohesionWeight);
                    MoveInDirection(flockingDirection);
                }
                else
                {
                    if (rb != null) rb.linearVelocity = Vector2.zero; 
                }
                
                ScanForEnemies(); 
                break;

            case State.PlayerCommand:
                float distToTarget = Vector2.Distance(Position, targetPos);
                float dynamicStopRadius = 0.2f + (currentFlockCount * 0.15f);

                if (distToTarget > 1.5f)
                {
                    Vector2 marchDirection = CalculateFlocking(targetPos, separationWeight, cohesionWeight);
                    MoveInDirection(marchDirection * 2f);
                }
                else
                {
                    if (distToTarget < dynamicStopRadius)
                    {
                        if (rb != null) rb.linearVelocity = Vector2.zero; 
                    }
                    else
                    {
                        Vector2 packDirection = CalculateFlocking(targetPos, 0.2f, 2.0f);
                        MoveInDirection(packDirection * 1.5f);
                    }
                }
                
                ScanForEnemies();
                break;

            case State.Attacking:
                if (currentTarget == null || currentTarget.Health <= 0 || ((MonoBehaviour)currentTarget) == null)
                {
                    currentTarget = null;

                    if (targetPos != Vector2.zero && Vector2.Distance(Position, targetPos) > 1.5f)
                    {
                        Vector2 marchDirection = CalculateFlocking(targetPos, separationWeight, cohesionWeight);
                        MoveInDirection(marchDirection * 2f); 
                        
                        ScanForEnemies(); 
                        break; 
                    }
                    else
                    {
                        targetPos = Vector2.zero; 
                        currentState = State.Rallying;
                        
                        if (commander != null) commander.ReturnToDuty(this); 
                        break;
                    }
                }

                targetPos = currentTarget.Position; 
                float distanceToEnemy = Vector2.Distance(Position, targetPos);

                if (distanceToEnemy <= weaponRange)
                {
                    MoveInDirection(CalculateFlocking(Position, separationWeight, 0f));

                    attackTimer += Time.deltaTime;
                    if (attackTimer >= attackRate)
                    {
                        currentTarget.TakeDamage(attackDamage);
                        attackTimer = 0f; 
                    }
                }
                else
                {
                    Vector2 attackDirection = CalculateFlocking(targetPos, separationWeight, cohesionWeight);
                    MoveInDirection(attackDirection * 2f);
                }
                break;
                
            case State.FallingBack:
                if (Vector2.Distance(Position, commander.Position) < 4.0f)
                {
                    targetPos = Vector2.zero;
                    currentState = State.Rallying;
                    if (commander != null) commander.ReturnToDuty(this);
                }
                else
                {
                    Vector2 retreatDirection = CalculateFlocking(commander.Position, separationWeight, cohesionWeight);
                    MoveInDirection(retreatDirection * 2.2f); 
                }
                break;
        }
    }

    private void ScanForEnemies()
    {
        Rect searchArea = new Rect(Position.x - visionRange, Position.y - visionRange, visionRange * 2, visionRange * 2);
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        float closestDist = float.MaxValue;
        ITargetable bestTarget = null;

        foreach (var entity in nearby)
        {
            if (entity is ITargetable t && t.FactionID != this.factionID && t.Health > 0)
            {
                float d = Vector2.Distance(Position, entity.Position);
                if (d < closestDist)
                {
                    closestDist = d;
                    bestTarget = t;
                }
            }
        }

        if (bestTarget != null)
        {
            currentTarget = bestTarget;            
            targetPos = currentTarget.Position;    
            currentState = State.Attacking;        
        }
    }

    private Vector2 EvaluateHarvesterYieldTree()
    {
        if (currentState == State.Attacking) return Vector2.zero;

        Rect searchArea = new Rect(Position.x - harvesterYieldRadius, Position.y - harvesterYieldRadius, harvesterYieldRadius * 2, harvesterYieldRadius * 2);
        List<IEntity> nearbyEntities = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearbyEntities);

        Vector2 avoidForce = Vector2.zero;
        int activeHarvestersCount = 0;

        foreach (var entity in nearbyEntities)
        {
            if (entity is HarvesterAI harvester && harvester.FactionID == this.factionID)
            {
                float dist = Vector2.Distance(Position, harvester.Position);
                if (dist < harvesterYieldRadius && dist > 0)
                {
                    avoidForce += (Position - harvester.Position).normalized / dist;
                    activeHarvestersCount++;
                }
            }
        }

        if (activeHarvestersCount > 0) return avoidForce.normalized; 
        return Vector2.zero; 
    }

    public void SetGuardMission(Vector2 defensePoint)
    {
        targetPos = defensePoint;
        currentTarget = null;
        currentState = State.Guarding;
    }

    public void SetAttackMission(Vector2 target)
    {
        targetPos = target;
        currentTarget = null; 
        currentState = State.Attacking;
        ScanForEnemies(); 
    }

    public void SetPlayerCommand(Vector2 target)
    {
        targetPos = target;
        currentTarget = null; 
        currentState = State.PlayerCommand;
    }

    private Vector2 CalculateFlocking(Vector2 finalDestination, float currentSepWeight, float currentCohWeight)
    {
        Rect searchArea = new Rect(Position.x - flockRadius, Position.y - flockRadius, flockRadius * 2, flockRadius * 2);
        List<IEntity> neighbors = new List<IEntity>();
        simManager.quadTree.Query(searchArea, neighbors);

        Vector2 separation = Vector2.zero;
        Vector2 cohesion = Vector2.zero;
        currentFlockCount = 0; 

        foreach (var entity in neighbors)
        {
            if (entity is TroopAI otherTroop && otherTroop != this && otherTroop.FactionID == this.factionID)
            {
                float dist = Vector2.Distance(Position, otherTroop.Position);
                if (dist > 0 && dist < flockRadius)
                {
                    float pushStrength = Mathf.Min(1f / dist, 5f);
                    separation += (Position - otherTroop.Position).normalized * pushStrength;
                    
                    cohesion += otherTroop.Position;
                    currentFlockCount++;
                }
            }
        }

        Vector2 desiredDirection = (finalDestination - Position).normalized * targetWeight;

        if (currentFlockCount > 0)
        {
            cohesion = ((cohesion / currentFlockCount) - Position).normalized * currentCohWeight;
            separation = separation.normalized * currentSepWeight;
            desiredDirection += separation + cohesion;
        }

        return desiredDirection.normalized;
    }

    // Delegate damage and death handling to the base class.
    public override void TakeDamage(float amount)
    {
        if (currentState != State.Attacking)
        {
            ScanForEnemies();
        }

        // Base Unit handles health subtraction and destruction.
        base.TakeDamage(amount); 
    }

    public bool IsAvailableForReassignment()
    {
        return currentState == State.Rallying || currentState == State.Guarding;
    }
}