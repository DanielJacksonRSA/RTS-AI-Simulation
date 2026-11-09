using System.Collections.Generic;
using UnityEngine;

public class PathNode
{
    public bool isWalkable;
    public Vector2 worldPosition;
    public int gridX;
    public int gridY;

    public int gCost;
    public int hCost;
    public PathNode parent;
    public int fCost { get { return gCost + hCost; } }

    public float faction0Influence; 
    public float faction1Influence; 
    public float faction2Influence; 

    public PathNode(bool _isWalkable, Vector2 _worldPos, int _gridX, int _gridY)
    {
        isWalkable = _isWalkable;
        worldPosition = _worldPos;
        gridX = _gridX;
        gridY = _gridY;
        faction0Influence = 0f;
        faction1Influence = 0f;
        faction2Influence = 0f;
    }
}

public class CustomMapManager : MonoBehaviour
{
    [Header("Pathfinding Grid Settings")]
    public Vector2 gridWorldSize = new Vector2(100f, 50f); 
    public float nodeRadius = 1.0f; 
    
    [Header("Heatmap Settings")]
    public float influenceDecayRate = 1.0f; 
    public float influencePower = 2.0f; 

    public List<Bounds> obstacleBounds = new List<Bounds>();
    public PathNode[,] grid;

    private float nodeDiameter;
    private int gridSizeX, gridSizeY;
    private SimulationManager simManager;

    void Start()
    {
        simManager = FindFirstObjectByType<SimulationManager>();
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
        
        ScanForWalls();
        CreateGrid();
    }

    void Update()
    {
        UpdateInfluenceMap();
    }

    public float GetMyInfluence(PathNode node, int myFactionID)
    {
        if (node == null) return 0f;
        if (myFactionID == 0) return node.faction0Influence;
        if (myFactionID == 1) return node.faction1Influence;
        if (myFactionID == 2) return node.faction2Influence;
        return 0f;
    }

    public float GetEnemyInfluence(PathNode node, int myFactionID)
    {
        if (node == null) return 0f;
        float hostileThreat = 0f;
        if (myFactionID != 0) hostileThreat += node.faction0Influence;
        if (myFactionID != 1) hostileThreat += node.faction1Influence;
        if (myFactionID != 2) hostileThreat += node.faction2Influence;
        return hostileThreat;
    }

    void UpdateInfluenceMap()
    {
        if (grid == null || simManager == null) return;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                grid[x, y].faction0Influence = Mathf.Max(0, grid[x, y].faction0Influence - Time.deltaTime * influenceDecayRate);
                grid[x, y].faction1Influence = Mathf.Max(0, grid[x, y].faction1Influence - Time.deltaTime * influenceDecayRate);
                grid[x, y].faction2Influence = Mathf.Max(0, grid[x, y].faction2Influence - Time.deltaTime * influenceDecayRate);
            }
        }

        foreach (var entity in simManager.GetAllEntities())
        {
            if (entity is ITargetable t)
            {
                if (t is HarvesterAI) continue; 

                PathNode centerNode = NodeFromWorldPoint(t.Position);
                if (centerNode == null) continue; 
                
                float splashWorldRadius = 3.0f; 
                float powerMultiplier = influencePower * 0.8f; 
                float maxHeatCap = 50.0f; 

                if (t is TurretAI turret)
                {
                    splashWorldRadius = turret.attackRange + 1.0f; 
                    powerMultiplier = influencePower * 4.0f; 
                    maxHeatCap = 100.0f; 
                }
                else if (t is FactionCommander || t is ExpansionBaseAI)
                {
                    splashWorldRadius = 2.0f; 
                    powerMultiplier = influencePower * 1.5f; 
                    maxHeatCap = 25.0f; 
                }

                int splashRadiusNodes = Mathf.Max(1, Mathf.RoundToInt(splashWorldRadius / nodeDiameter));

                for (int x = -splashRadiusNodes; x <= splashRadiusNodes; x++)
                {
                    for (int y = -splashRadiusNodes; y <= splashRadiusNodes; y++)
                    {
                        int checkX = centerNode.gridX + x;
                        int checkY = centerNode.gridY + y;

                        if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
                        {
                            float distNodes = new Vector2(x, y).magnitude;
                            if (distNodes <= splashRadiusNodes)
                            {
                                float falloff = 1f - (distNodes / splashRadiusNodes);
                                float power = (falloff * falloff) * Time.deltaTime * powerMultiplier;
                                
                                if (t.FactionID == 0) 
                                {
                                    grid[checkX, checkY].faction0Influence += power;
                                    grid[checkX, checkY].faction0Influence = Mathf.Min(grid[checkX, checkY].faction0Influence, maxHeatCap);
                                }
                                else if (t.FactionID == 1) 
                                {
                                    grid[checkX, checkY].faction1Influence += power;
                                    grid[checkX, checkY].faction1Influence = Mathf.Min(grid[checkX, checkY].faction1Influence, maxHeatCap);
                                }
                                else if (t.FactionID == 2) 
                                {
                                    grid[checkX, checkY].faction2Influence += power;
                                    grid[checkX, checkY].faction2Influence = Mathf.Min(grid[checkX, checkY].faction2Influence, maxHeatCap);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Samples a ring around a base and returns the secured fraction.
    public float GetPerimeterSecuredPercentage(Vector2 basePos, float radius, int factionID, float requiredInfluence = 1.5f)
    {
        int numSamples = Mathf.Max(12, Mathf.RoundToInt(radius * 6)); 
        int pointsChecked = 0;
        int pointsSecured = 0;

        for (int i = 0; i < numSamples; i++)
        {
            float angle = i * (360f / numSamples);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector2 samplePos = basePos + (dir * radius);

            PathNode n = NodeFromWorldPoint(samplePos);
            
            // Only consider valid, walkable nodes.
            if (n != null && n.isWalkable)
            {
                pointsChecked++;
                float myInf = GetMyInfluence(n, factionID); 
                
                if (myInf >= requiredInfluence) pointsSecured++;
            }
        }

        // If no valid samples exist, treat the perimeter as secure.
        if (pointsChecked == 0) return 1.0f;
        return (float)pointsSecured / pointsChecked;
    }

    public bool IsPerimeterSecured(Vector2 basePos, float radius, int factionID, float requiredInfluence = 1.5f)
    {
        return GetPerimeterSecuredPercentage(basePos, radius, factionID, requiredInfluence) >= 0.90f;
    }
    public Vector2 GetWeakestDefensePointNearBase(Vector2 basePos, int factionID, float customRadius = 3.5f)
    {
        Vector2 bestSpot = basePos;
        float lowestHeat = float.MaxValue;

        int numSamples = Mathf.Max(12, Mathf.RoundToInt(customRadius * 6));
        float randomOffset = Random.Range(0f, 360f);

        for (int i = 0; i < numSamples; i++)
        {
            float angle = randomOffset + (i * (360f / numSamples));
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector2 candidatePos = basePos + (dir * customRadius);

            PathNode n = NodeFromWorldPoint(candidatePos);
            
            if (n == null || !n.isWalkable) continue;

            Collider2D overlap = Physics2D.OverlapCircle(candidatePos, 0.4f);
            if (overlap != null) continue;

            float myInf = GetMyInfluence(n, factionID); 
            float score = myInf + Random.Range(0f, 0.5f); 

            if (score < lowestHeat)
            {
                lowestHeat = score;
                bestSpot = candidatePos;
            }
        }

        if (bestSpot == basePos) 
        {
            Vector2 fallback = basePos + (Random.insideUnitCircle.normalized * Mathf.Min(customRadius, 5.0f));
            float minX = transform.position.x - (gridWorldSize.x / 2f) + 1f;
            float maxX = transform.position.x + (gridWorldSize.x / 2f) - 1f;
            float minY = transform.position.y - (gridWorldSize.y / 2f) + 1f;
            float maxY = transform.position.y + (gridWorldSize.y / 2f) - 1f;
            
            fallback.x = Mathf.Clamp(fallback.x, minX, maxX);
            fallback.y = Mathf.Clamp(fallback.y, minY, maxY);
            return fallback;
        }
        
        return bestSpot;
    }

    public Vector2 GetNearestSafeZone(Vector2 currentPos, Vector2 basePos, int factionID)
    {
        PathNode bestNode = null;
        float bestScore = 0.1f; 
        
        PathNode startNode = NodeFromWorldPoint(currentPos);
        if (startNode == null) return basePos; 

        int searchRadiusNodes = Mathf.RoundToInt(25.0f / nodeDiameter); 

        for (int x = -searchRadiusNodes; x <= searchRadiusNodes; x++)
        {
            for (int y = -searchRadiusNodes; y <= searchRadiusNodes; y++)
            {
                int cX = startNode.gridX + x;
                int cY = startNode.gridY + y;

                if (cX >= 0 && cX < gridSizeX && cY >= 0 && cY < gridSizeY)
                {
                    PathNode n = grid[cX, cY];
                    if (!n.isWalkable) continue;

                    float myInf = GetMyInfluence(n, factionID); 
                    float enemyInf = GetEnemyInfluence(n, factionID); 

                    if (enemyInf > 0.2f) continue;

                    float distToBase = Vector2.Distance(n.worldPosition, basePos);
                    float score = myInf - (distToBase * 0.5f);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = n;
                    }
                }
            }
        }
        
        return bestNode != null ? bestNode.worldPosition : basePos; 
    }

    void ScanForWalls()
    {
        GameObject[] walls = GameObject.FindGameObjectsWithTag("Wall");
        foreach (GameObject wall in walls)
        {
            BoxCollider2D collider = wall.GetComponent<BoxCollider2D>();
            if (collider != null) obstacleBounds.Add(collider.bounds);
        }
    }

    void CreateGrid()
    {
        grid = new PathNode[gridSizeX, gridSizeY];
        Vector2 worldBottomLeft = (Vector2)transform.position - (Vector2.right * gridWorldSize.x / 2) - (Vector2.up * gridWorldSize.y / 2);

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector2 worldPoint = worldBottomLeft + Vector2.right * (x * nodeDiameter + nodeRadius) + Vector2.up * (y * nodeDiameter + nodeRadius);
                bool walkable = !IsPositionBlocked(worldPoint);
                grid[x, y] = new PathNode(walkable, worldPoint, x, y);
            }
        }
    }

    public bool IsPositionBlocked(Vector2 position)
    {
        foreach (Bounds bound in obstacleBounds)
        {
            if (bound.Contains(new Vector3(position.x, position.y, 0))) return true;
        }
        return false;
    }

    public PathNode NodeFromWorldPoint(Vector2 worldPosition)
    {
        float percentX = (worldPosition.x - transform.position.x + gridWorldSize.x / 2) / gridWorldSize.x;
        float percentY = (worldPosition.y - transform.position.y + gridWorldSize.y / 2) / gridWorldSize.y;
        
        if (percentX < 0 || percentX > 1 || percentY < 0 || percentY > 1) return null;

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);
        return grid[x, y];
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, gridWorldSize.y, 1));
        if (grid != null)
        {
            foreach (PathNode n in grid)
            {
                float f0 = Mathf.Clamp01(Mathf.Sqrt(n.faction0Influence / 40f)); // Blue
                float f1 = Mathf.Clamp01(Mathf.Sqrt(n.faction1Influence / 40f)); // Red
                float f2 = Mathf.Clamp01(Mathf.Sqrt(n.faction2Influence / 40f)); // Green
                
                if (f0 > 0.05f || f1 > 0.05f || f2 > 0.05f)
                {
                    Color c = new Color(f1, f2, f0, Mathf.Max(f0, Mathf.Max(f1, f2)) * 0.6f); 
                    Gizmos.color = c;
                    Gizmos.DrawCube(n.worldPosition, Vector3.one * (nodeDiameter * 0.95f));
                }
            }
        }
    }
}