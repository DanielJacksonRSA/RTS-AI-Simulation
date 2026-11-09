using UnityEngine;

public class OreMine : MonoBehaviour
{
    [Header("Spawn Interval Settings")]
    public GameObject resourcePrefab;     
    public float spawnIntervalMin = 2.0f;  // Minimum wait time
    public float spawnIntervalMax = 20.0f; // Maximum wait time
    private float currentSpawnInterval;
    
    [Header("Spawn Distance Settings")]
    public float spawnRadiusMin = 1.0f;    
    public float spawnRadiusMax = 6.0f;    
    
    [Header("Ore Wealth Settings")]
    public float oreWealthMin = 20f;       // Minimum resources per node
    
    // Upper bound for node wealth variance.
    public float oreWealthMax = 200f;      

    private SimulationManager simManager;
    private ResourceManager resourceManager;
    private CustomMapManager mapManager;

    private float spawnTimer = 0f;

    void Start()
    {
        simManager = FindFirstObjectByType<SimulationManager>();
        resourceManager = FindFirstObjectByType<ResourceManager>();
        mapManager = FindFirstObjectByType<CustomMapManager>();
        
        // Seed the first spawn interval.
        currentSpawnInterval = Random.Range(spawnIntervalMin, spawnIntervalMax);
    }

    void Update()
    {
        spawnTimer += Time.deltaTime;
        
        if (spawnTimer >= currentSpawnInterval)
        {
            spawnTimer = 0f; 
            
            // Roll the next interval after each spawn.
            currentSpawnInterval = Random.Range(spawnIntervalMin, spawnIntervalMax); 
            
            SpawnOreRandomly();
        }
    }

    void SpawnOreRandomly()
    {
        if (resourcePrefab == null || simManager == null || resourceManager == null) return;

        Vector2 spawnPos = Vector2.zero;
        bool validPositionFound = false;
        int maxAttempts = 15; 

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            float randomDist = Random.Range(spawnRadiusMin, spawnRadiusMax);
            Vector2 potentialPos = (Vector2)transform.position + (randomDir * randomDist);

            if (mapManager != null)
            {
                if (!mapManager.IsPositionBlocked(potentialPos))
                {
                    spawnPos = potentialPos;
                    validPositionFound = true;
                    break;
                }
            }
            else
            {
                spawnPos = potentialPos;
                validPositionFound = true;
                break;
            }
        }

        if (validPositionFound)
        {
            GameObject newOreObject = Instantiate(resourcePrefab, spawnPos, Quaternion.identity);
            newOreObject.tag = "Resource";

            // Randomize the wealth of this node.
            float randomWealth = Random.Range(oreWealthMin, oreWealthMax);
            Resource newNode = new Resource(spawnPos, randomWealth, newOreObject);

            resourceManager.activeNodes.Add(newNode);
            simManager.RegisterEntity(newNode);
        }
    }
}