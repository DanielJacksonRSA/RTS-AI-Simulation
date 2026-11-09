using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class TurretAI : MonoBehaviour, ITargetable
{
    public Vector2 Position => transform.position;
    
    public int FactionID => factionID;
    public float Health => health;

    [Header("Turret Stats")]
    public int factionID;
    public float maxHealth = 200f;
    public float health = 200f;
    
    [Header("Combat Stats")]
    public float attackRange = 3f; 
    public float attackDamage = 25f;
    public float attackCooldown = 0.8f;
    private float lastAttackTime = 0f;

    [Header("Repair Throttle settings")]
    private float nextAllowedRepairTime = 0f;
    public float repairThrottlingInterval = 1.0f; // Throttle repairs to once per second.

    public SpriteRenderer fillSprite;
    private LineRenderer laserLine; 
    private SimulationManager simManager;

    public void Initialize(SimulationManager sim, int faction)
    {
        simManager = sim;
        factionID = faction;
        health = maxHealth;
        
        if (fillSprite == null) fillSprite = GetComponent<SpriteRenderer>();
        UpdateHealthVisual();
        
        laserLine = gameObject.AddComponent<LineRenderer>();
        laserLine.startWidth = 0.05f;
        laserLine.endWidth = 0.05f;
        laserLine.positionCount = 2;
        laserLine.enabled = false;
        
        Color laserColor = Color.white;
        if (factionID == 0) laserColor = Color.blue;
        else if (factionID == 1) laserColor = Color.red;
        else if (factionID == 2) laserColor = Color.green;

        laserLine.startColor = laserColor;
        laserLine.endColor = laserColor;
        
        // Ensure a valid material for the line renderer.
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null) laserLine.material = new Material(spriteShader);

        simManager.RegisterEntity(this);
    }

    void Update()
    {
        ScanAndFire();
    }

    private void ScanAndFire()
    {
        if (Time.time < lastAttackTime + attackCooldown) return;

        Rect searchArea = new Rect(Position.x - attackRange, Position.y - attackRange, attackRange * 2, attackRange * 2);
        List<IEntity> nearby = new List<IEntity>();
        simManager.quadTree.Query(searchArea, nearby);

        ITargetable bestTarget = null;
        float closestDist = float.MaxValue;

        foreach (var entity in nearby)
        {
            if (entity is ITargetable t && t.FactionID != this.factionID && t.Health > 0)
            {
                float dist = Vector2.Distance(Position, t.Position);
                if (dist <= attackRange && dist < closestDist)
                {
                    closestDist = dist;
                    bestTarget = t;
                }
            }
        }

        if (bestTarget != null)
        {
            Vector2 aimDir = (bestTarget.Position - Position).normalized;
            float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);

            bestTarget.TakeDamage(attackDamage);
            lastAttackTime = Time.time;
            
            StartCoroutine(ShootLaser(bestTarget.Position));
        }
    }
    
    private IEnumerator ShootLaser(Vector2 targetPos)
    {
        laserLine.enabled = true;
        laserLine.SetPosition(0, Position);
        laserLine.SetPosition(1, targetPos);
        yield return new WaitForSeconds(0.1f); 
        laserLine.enabled = false;
    }

    // Reject repairs while within the cooldown window.
    public bool Repair(float amount)
    {
        if (Time.time < nextAllowedRepairTime) 
        {
            return false;
        }

        health = Mathf.Min(maxHealth, health + amount);
        UpdateHealthVisual();

        // Lock repairs until the throttling window passes.
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