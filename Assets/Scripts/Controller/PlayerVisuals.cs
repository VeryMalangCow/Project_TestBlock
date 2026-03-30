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

    #endregion

    #region Init

    public void Init()
    {
        // Load static body sprites
        SetBody();
    }

    #endregion

    #region Armor Management

    public void SetBody()
    {
        Sprite[] sheet = ResourceManager.Instance.GetBodySprites();
        if (sheet == null) return;

        VisualLayer target = layers.Find(l => l.name.Equals("Body", System.StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.currentSheet = sheet;
            target.SetSprite(0);
        }
    }

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
            target.SetSprite(0);
        }
        else
        {
            Debug.LogWarning($"[PlayerVisuals] Could not find layer named: {layerName} in the Inspector list.");
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
        IsFlipped = flipX;
        foreach (var layer in layers)
        {
            if (layer.renderer != null)
            {
                layer.renderer.flipX = flipX;
            }
        }
    }

    public bool IsFlipped { get; private set; }

    #endregion
}
