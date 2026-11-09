using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem; 

public class RTSController : MonoBehaviour
{
    private Vector3 startMousePosScreen;
    private Vector2 startMousePosWorld;
    private Vector3 currentMousePosScreen; 
    
    private bool isDragging = false;
    
    public List<TroopAI> selectedTroops = new List<TroopAI>();
    private Dictionary<TroopAI, Color> originalColors = new Dictionary<TroopAI, Color>();

    void Update()
    {
        if (Mouse.current == null) return; 

        currentMousePosScreen = Mouse.current.position.ReadValue();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Begin drag selection and clear current selection.
            startMousePosScreen = currentMousePosScreen;
            startMousePosWorld = Camera.main.ScreenToWorldPoint(startMousePosScreen);
            isDragging = true;
            DeselectAll(); 
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            // Finalize selection when the drag ends.
            isDragging = false;
            SelectUnitsInBox();
        }

        if (Mouse.current.rightButton.wasPressedThisFrame && selectedTroops.Count > 0)
        {
            // Issue a move/command to the current selection.
            Vector2 targetPos = Camera.main.ScreenToWorldPoint(currentMousePosScreen);
            foreach (TroopAI troop in selectedTroops)
            {
                troop.SetPlayerCommand(targetPos);
            }
        }
    }

    void SelectUnitsInBox()
    {
        // Box-select troops in world space; use a small radius for click selection.
        Vector2 endMousePosWorld = Camera.main.ScreenToWorldPoint(currentMousePosScreen);
        
        Vector2 min = Vector2.Min(startMousePosWorld, endMousePosWorld);
        Vector2 max = Vector2.Max(startMousePosWorld, endMousePosWorld);
        Rect worldRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);

        TroopAI[] allTroops = FindObjectsByType<TroopAI>(FindObjectsSortMode.None);
        foreach (TroopAI troop in allTroops)
        {
            if (worldRect.size.magnitude < 0.2f) 
            {
                if (Vector2.Distance(troop.Position, startMousePosWorld) < 0.5f)
                {
                    SelectUnit(troop);
                    break; 
                }
            }
            else if (worldRect.Contains(troop.Position)) 
            {
                SelectUnit(troop);
            }
        }
    }

    void SelectUnit(TroopAI troop)
    {
        if (!troop.isSelected) 
        {
            SpriteRenderer sr = troop.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                originalColors[troop] = sr.color; 
                sr.color = Color.green; 
            }
            
            troop.isSelected = true;
            selectedTroops.Add(troop);
        }
    }

    void DeselectAll()
    {
        foreach (TroopAI troop in selectedTroops)
        {
            if (troop != null)
            {
                troop.isSelected = false;
                SpriteRenderer sr = troop.GetComponent<SpriteRenderer>();
                
                if (sr != null && originalColors.ContainsKey(troop))
                {
                    sr.color = originalColors[troop]; 
                }
            }
        }
        selectedTroops.Clear();
        originalColors.Clear(); 
    }

    void OnGUI()
    {
        if (isDragging)
        {
            // Render the drag-selection rectangle.
            Rect rect = GetScreenRect(startMousePosScreen, currentMousePosScreen);
            
            GUI.color = new Color(0.2f, 0.8f, 0.2f, 0.3f); 
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            
            GUI.color = new Color(0.2f, 0.8f, 0.2f, 0.9f); 
            DrawScreenRectBorder(rect, 2f);
            
            GUI.color = Color.white; 
        }
    }

    Rect GetScreenRect(Vector3 screenPosition1, Vector3 screenPosition2)
    {
        screenPosition1.y = Screen.height - screenPosition1.y;
        screenPosition2.y = Screen.height - screenPosition2.y;

        var topLeft = Vector3.Min(screenPosition1, screenPosition2);
        var bottomRight = Vector3.Max(screenPosition1, screenPosition2);

        return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }
    
    void DrawScreenRectBorder(Rect rect, float thickness)
    {
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
    }
}