using System.Collections.Generic;
using UnityEngine;

public class QuadTree
{
    private Rect bounds;
    private int capacity;
    private List<IEntity> entities;
    private bool isSplit = false;

    private QuadTree topLeft;
    private QuadTree topRight;
    private QuadTree bottomLeft;
    private QuadTree bottomRight;

    public QuadTree(Rect bounds, int capacity)
    {
        this.bounds = bounds;
        this.capacity = capacity;
        this.entities = new List<IEntity>();
    }

    // Treat Unity-destroyed objects as invalid to avoid stale references.
    private bool IsValid(IEntity entity)
    {
        if (entity == null) return false;
        if (entity is UnityEngine.Object unityObj && unityObj == null) return false;
        return true;
    }

    public bool Insert(IEntity entity)
    {
        // Ignore invalid or out-of-bounds entities.
        if (!IsValid(entity)) return false;
        if (!bounds.Contains(entity.Position)) return false;

        if (entities.Count < capacity && !isSplit)
        {
            entities.Add(entity);
            return true;
        }

        if (!isSplit) Subdivide();

        return topLeft.Insert(entity) || topRight.Insert(entity) || 
               bottomLeft.Insert(entity) || bottomRight.Insert(entity);
    }

    private void Subdivide()
    {
        float subWidth = bounds.width / 2f;
        float subHeight = bounds.height / 2f;
        float x = bounds.x;
        float y = bounds.y;

        topLeft = new QuadTree(new Rect(x, y + subHeight, subWidth, subHeight), capacity);
        topRight = new QuadTree(new Rect(x + subWidth, y + subHeight, subWidth, subHeight), capacity);
        bottomLeft = new QuadTree(new Rect(x, y, subWidth, subHeight), capacity);
        bottomRight = new QuadTree(new Rect(x + subWidth, y, subWidth, subHeight), capacity);

        isSplit = true;

        foreach (var entity in entities)
        {
            if (IsValid(entity))
            {
                _ = topLeft.Insert(entity) || topRight.Insert(entity) || 
                    bottomLeft.Insert(entity) || bottomRight.Insert(entity);
            }
        }
        entities.Clear();
    }

    public void Query(Rect range, List<IEntity> found)
    {
        if (!bounds.Overlaps(range)) return;

        foreach (var entity in entities)
        {
            // Guard against stale references before accessing Position.
            if (!IsValid(entity)) continue;
            
            if (range.Contains(entity.Position)) found.Add(entity);
        }

        if (isSplit)
        {
            topLeft.Query(range, found);
            topRight.Query(range, found);
            bottomLeft.Query(range, found);
            bottomRight.Query(range, found);
        }
    }

    public void DrawDebug(int depth = 0)
    {
        if (depth == 0) Gizmos.color = Color.green;
        else if (depth == 1) Gizmos.color = Color.cyan;
        else if (depth == 2) Gizmos.color = Color.yellow;
        else Gizmos.color = Color.red;

        Gizmos.DrawWireCube(bounds.center, bounds.size);

        if (isSplit)
        {
            topLeft.DrawDebug(depth + 1);
            topRight.DrawDebug(depth + 1);
            bottomLeft.DrawDebug(depth + 1);
            bottomRight.DrawDebug(depth + 1);
        }
    }
}