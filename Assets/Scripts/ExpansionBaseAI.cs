using UnityEngine;

public class ExpansionBaseAI : MonoBehaviour, ITargetable
{
    public Vector2 Position => transform.position;
    public int FactionID { get; private set; }
    public float Health => health;

    [Header("Base Stats")]
    public float maxHealth = 500f;
    public float health = 500f;
    
    public Transform spawnPoint;
    public Transform rallyPoint;
    public SpriteRenderer fillSprite;
    public float spawnTimer = 0f;

    [Header("Repair Throttle")]
    private float nextAllowedRepairTime = 0f;
    public float repairThrottlingInterval = 1.0f; // Throttle repairs to reduce stacking.

    private SimulationManager simManager;
    private FactionCommander commander;

    public void Initialize(SimulationManager sim, FactionCommander cmd, int faction)
    {
        simManager = sim;
        commander = cmd;
        FactionID = faction;
        health = maxHealth;
        spawnTimer = 0f;
        
        if (fillSprite == null) fillSprite = GetComponent<SpriteRenderer>();
        UpdateHealthVisual();
        
        simManager.RegisterEntity(this);
    }

    // Reject repairs until the cooldown window expires.
    public bool Repair(float amount)
    {
        if (Time.time < nextAllowedRepairTime) return false;

        health = Mathf.Min(maxHealth, health + amount);
        UpdateHealthVisual();

        nextAllowedRepairTime = Time.time + repairThrottlingInterval;
        return true;
    }

    public void TakeDamage(float amount)
    {
        health -= amount;
        UpdateHealthVisual();
        
        if (health <= 0)
        {
            simManager.RemoveEntity(this);
            commander.expansionBases.Remove(this);
            Destroy(gameObject);
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