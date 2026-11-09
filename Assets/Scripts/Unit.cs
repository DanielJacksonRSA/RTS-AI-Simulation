using UnityEngine;

// Shared interface for spatial queries.
public interface IEntity
{
    Vector2 Position { get; }
}

public interface ITargetable : IEntity
{
    int FactionID { get; }
    float Health { get; }
    void TakeDamage(float amount);
}

[RequireComponent(typeof(Rigidbody2D))] 
public class Unit : MonoBehaviour, ITargetable
{
    public Vector2 Position => transform.position;
    
    public int FactionID => factionID;
    public float Health => health;
    
    [Header("Base Unit Stats")]
    public int factionID;
    public float moveSpeed = 3f;
    
    [Header("Health & Visuals")]
    public float maxHealth = 100f;
    public float health = 100f;
    public SpriteRenderer fillSprite;

    [Header("Combat Stats")]
    public float attackRange = 0.5f; 
    public float attackDamage = 15f;
    public float attackCooldown = 1.2f;
    protected float lastAttackTime = 0f;

    protected SimulationManager simManager;
    protected FactionCommander commander;
    protected Rigidbody2D rb;

    private Vector2 evasionDirection = Vector2.zero;
    private float evasionTimer = 0f;
    
    protected bool avoidOtherUnits = false; 
    protected bool avoidBase = false; 

    protected float whiskerRadius = 0.15f;
    protected float whiskerLength = 0.5f;

    public virtual void Initialize(SimulationManager sim, FactionCommander cmd, int faction)
    {
        simManager = sim;
        commander = cmd;
        factionID = faction;
        health = maxHealth;
        rb = GetComponent<Rigidbody2D>(); 
        simManager.RegisterEntity(this); 
        
        if (fillSprite == null) fillSprite = GetComponent<SpriteRenderer>();
        UpdateHealthVisual();
    }

    protected void MoveTowards(Vector2 target)
    {
        if (rb == null) return;
        if (Vector2.Distance(rb.position, target) < 0.1f) 
        {
            rb.linearVelocity = Vector2.zero; 
            return;
        }
        ApplySteering((target - rb.position).normalized);
    }

    protected void MoveInDirection(Vector2 direction)
    {
        ApplySteering(direction.normalized);
    }

    private void ApplySteering(Vector2 desiredDirection)
    {
        if (rb == null) return;
        
        if (desiredDirection == Vector2.zero) 
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 finalDirection = desiredDirection;

        if (avoidOtherUnits)
        {
            RaycastHit2D[] unitHits = Physics2D.CircleCastAll(rb.position, 0.2f, desiredDirection, 0.25f);
            Vector2 separationPush = Vector2.zero;

            foreach (var hit in unitHits)
            {
                if (hit.collider != null && hit.collider.gameObject != this.gameObject && hit.collider.GetComponent<Unit>() != null)
                {
                    Vector2 awayFromOther = (rb.position - (Vector2)hit.collider.transform.position);
                    Vector2 tangentSlide = new Vector2(-awayFromOther.y, awayFromOther.x);
                    separationPush += (awayFromOther.normalized + (tangentSlide * 0.5f)).normalized;
                }
            }

            if (separationPush != Vector2.zero)
            {
                finalDirection = (finalDirection + (separationPush.normalized * 0.7f)).normalized;
            }
        }

        if (avoidBase && commander != null)
        {
            Collider2D baseCol = commander.GetComponent<Collider2D>();
            if (baseCol != null)
            {
                Vector2 edgePoint = baseCol.ClosestPoint(rb.position);
                float distToEdge = Vector2.Distance(rb.position, edgePoint);

                if (distToEdge < 0.3f)
                {
                    Vector2 pushDir = (rb.position - edgePoint).normalized;
                    if (pushDir == Vector2.zero) pushDir = (rb.position - (Vector2)commander.transform.position).normalized;

                    float pushStrength = Mathf.Clamp01(1.0f - (distToEdge / 0.3f)) * 3.0f;
                    finalDirection = (finalDirection + (pushDir * pushStrength)).normalized;
                }
            }
        }

        if (evasionTimer > 0) evasionTimer -= Time.deltaTime;
        RaycastHit2D[] wallHits = Physics2D.CircleCastAll(rb.position, whiskerRadius, finalDirection, whiskerLength);

        foreach (RaycastHit2D hit in wallHits)
        {
            bool isObstacle = hit.collider != null && 
                              !hit.collider.isTrigger && 
                              hit.collider.GetComponent<Unit>() == null && 
                              !hit.collider.CompareTag("Resource");

            if (isObstacle)
            {
                Vector2 wallNormal = hit.normal;
                Vector2 slideA = new Vector2(-wallNormal.y, wallNormal.x);
                Vector2 slideB = new Vector2(wallNormal.y, -wallNormal.x);

                if (evasionTimer > 0)
                {
                    if (Vector2.Dot(slideA, evasionDirection) > 0) evasionDirection = slideA;
                    else evasionDirection = slideB;
                }
                else
                {
                    float dotA = Vector2.Dot(slideA, finalDirection);
                    float dotB = Vector2.Dot(slideB, finalDirection);
                    if (Mathf.Abs(dotA - dotB) < 0.05f) dotA += 0.1f; 

                    if (dotA > dotB) evasionDirection = slideA;
                    else evasionDirection = slideB;
                }

                evasionDirection = (evasionDirection + (wallNormal * 0.5f)).normalized;
                evasionTimer = 0.15f; 
                finalDirection = evasionDirection;
                break; 
            }
        }

        rb.linearVelocity = finalDirection * moveSpeed;

        if (finalDirection.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    public virtual void TakeDamage(float amount)
    {
        health -= amount;
        UpdateHealthVisual();
        if (health <= 0) Die();
    }
    
    protected void UpdateHealthVisual()
    {
        if (fillSprite != null)
        {
            Color c = fillSprite.color;
            c.a = Mathf.Clamp01(health / maxHealth);
            fillSprite.color = c;
        }
    }

    protected virtual void Die()
    {
        simManager.RemoveEntity(this); 
        Destroy(gameObject);
    }
}