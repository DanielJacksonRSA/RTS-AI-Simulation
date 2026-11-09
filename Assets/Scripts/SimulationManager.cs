using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SimulationManager : MonoBehaviour
{
    [Header("Simulation Boundaries")]
    public Rect simulationBounds = new Rect(-50, -25, 100, 50);
    public int quadTreeCapacity = 4;
    
    [Header("Time Control")]
    public float normalSpeed = 1.0f;
    public float fastSpeedFactor = 3.0f; 
    private bool isFastForwarding = false;
    
    public QuadTree quadTree;
    private List<IEntity> allEntities = new List<IEntity>();

    void Awake() 
    {
        quadTree = new QuadTree(simulationBounds, quadTreeCapacity);
        Time.timeScale = normalSpeed; 
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ToggleSimulationSpeed();
        }

        quadTree = new QuadTree(simulationBounds, quadTreeCapacity);
        
        foreach (var entity in allEntities)
        {
            quadTree.Insert(entity);
        }
    }

    public void ToggleSimulationSpeed()
    {
        isFastForwarding = !isFastForwarding;
        Time.timeScale = isFastForwarding ? fastSpeedFactor : normalSpeed;
        
        Debug.Log($"[Simulation Clock] Speed set to {Time.timeScale}x");
    }

    public void SetExactSimulationSpeed(float speed)
    {
        Time.timeScale = Mathf.Clamp(speed, 0.1f, 10.0f); 
        Debug.Log($"[Simulation Clock] Speed set to {Time.timeScale}x");
    }

    // Avoid duplicate registrations.
    public void RegisterEntity(IEntity entity) 
    { 
        if (!allEntities.Contains(entity))
        {
            allEntities.Add(entity); 
        }
    }
    
    public void RemoveEntity(IEntity entity) { allEntities.Remove(entity); }
    public List<IEntity> GetAllEntities() { return allEntities; }

    void OnDrawGizmos()
    {
        if (quadTree != null) quadTree.DrawDebug();
    }
}