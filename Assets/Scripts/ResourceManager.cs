using System.Collections.Generic;
using UnityEngine;

public class Resource : IEntity
{
    public Vector2 Position { get; private set; }
    
    public float currentWealth;
    public float maxWealth; 
    
    public GameObject graphicsObject;
    public SpriteRenderer fillSprite; 

    public Resource(Vector2 pos, float wealth, GameObject graphics)
    {
        Position = pos;
        currentWealth = wealth;
        maxWealth = wealth;
        graphicsObject = graphics;
        
        // Cache the renderer for alpha-based depletion.
        if (graphicsObject != null)
        {
            fillSprite = graphicsObject.GetComponent<SpriteRenderer>();
        }
        UpdateVisual();
    }
    
    public void UpdateVisual()
    {
        if (fillSprite != null)
        {
            Color c = fillSprite.color;
            c.a = Mathf.Clamp01(currentWealth / maxWealth);
            fillSprite.color = c;
        }
    }
}

public class ResourceManager : MonoBehaviour
{
    [Header("Resource Settings")]
    public float maxWealthPerNode = 100f; 

    public List<Resource> activeNodes = new List<Resource>();
    private SimulationManager simManager;

    void Start()
    {
        simManager = GetComponent<SimulationManager>();
        if (simManager == null) simManager = FindFirstObjectByType<SimulationManager>();
        
        GameObject[] resourceObjects = GameObject.FindGameObjectsWithTag("Resource");
        foreach (GameObject ro in resourceObjects)
        {
            Vector2 pos = ro.transform.position;
            Resource newNode = new Resource(pos, maxWealthPerNode, ro);
            activeNodes.Add(newNode);
            simManager.RegisterEntity(newNode); 
        }
    }

    void Update()
    {
        // No regeneration logic.
    }

    public float Mine(Resource node, float amountRequested)
    {
        if (node != null && node.currentWealth > 0)
        {
            float mined = Mathf.Min(amountRequested, node.currentWealth);
            node.currentWealth -= mined;
            
            // Fade the resource as it depletes.
            node.UpdateVisual();

            if (node.currentWealth <= 0) 
            {
                activeNodes.Remove(node);
                
                if (simManager != null) 
                    simManager.RemoveEntity(node); 

                if (node.graphicsObject != null) 
                    Destroy(node.graphicsObject); 
            }

            return mined;
        }
        return 0f;
    }
}