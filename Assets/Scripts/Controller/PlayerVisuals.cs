using UnityEngine;
using System.Collections.Generic;

public class PlayerVisuals : MonoBehaviour
{
    #region Variable

    [System.Serializable]
    public class VisualLayer
    {
        public string name;
        public SpriteRenderer renderer;
        public Sprite[] currentSheet;

        public void SetSprite(int index)
        {
            if (currentSheet != null && index >= 0 && index < currentSheet.Length)
            {
                renderer.sprite = currentSheet[index];
            }
        }
    }

    [Header("### Layers")]
    [SerializeField] private List<VisualLayer> layers = new List<VisualLayer>();
    
    private GameObject visualsContainer;

    #endregion

    #region Init

    public void Init()
    {
        // Create or find container
        visualsContainer = transform.Find("Visuals")?.gameObject;
        if (visualsContainer == null)
        {
            visualsContainer = new GameObject("Visuals");
            visualsContainer.transform.SetParent(this.transform);
            visualsContainer.transform.localPosition = Vector3.zero;
        }

        // Setup default layers if empty
        if (layers.Count == 0)
        {
            AddLayer("Backpack");
            AddLayer("Body");
            AddLayer("Cloth");
            AddLayer("Cloak");
            AddLayer("Head");
        }
        else
        {
            // Ensure renderers are linked
            foreach (var layer in layers)
            {
                Transform child = visualsContainer.transform.Find(layer.name);
                if (child != null) layer.renderer = child.GetComponent<SpriteRenderer>();
            }
        }
    }

    private void AddLayer(string layerName)
    {
        GameObject go = new GameObject(layerName);
        go.transform.SetParent(visualsContainer.transform);
        go.transform.localPosition = Vector3.zero;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();

        layers.Add(new VisualLayer 
        { 
            name = layerName, 
            renderer = sr
        });
    }

    #endregion

    #region Armor Management

    public void SetArmor(string category, int id)
    {
        Sprite[] sheet = ResourceManager.Instance.GetArmorSprites(category, id);
        if (sheet == null) return;

        // Standardize layer name mapping (Same logic as ResourceManager)
        string layerName = category;
        if (category.Equals("Clothes", System.StringComparison.OrdinalIgnoreCase)) layerName = "Cloth";
        else if (category.EndsWith("s", System.StringComparison.OrdinalIgnoreCase)) layerName = category.Substring(0, category.Length - 1);
        
        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.currentSheet = sheet;
            // Set default idle sprite (frame 0)
            target.SetSprite(0);
        }
        else
        {
            Debug.LogWarning($"[PlayerVisuals] Could not find layer named: {layerName} for category: {category}");
        }
    }

    #endregion

    #region Sync

    public void SyncAnimation(int frameIndex)
    {
        foreach (var layer in layers)
        {
            layer.SetSprite(frameIndex);
        }
    }

    public void SetFlip(bool flipX)
    {
        foreach (var layer in layers)
        {
            if (layer.renderer != null)
            {
                layer.renderer.flipX = flipX;
            }
        }
    }

    #endregion
}
