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

    private readonly string[] skinParts = { "ArmBack", "Leg", "Body", "Head", "ArmFront" };

    public void SetBody()
    {
        // 1. Load Skin Parts
        foreach (string part in skinParts)
        {
            Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(part);
            if (sheet == null) continue;

            VisualLayer target = layers.Find(l => l.name.Equals(part, System.StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.currentSheet = sheet;
                target.SetSprite(0);
            }
        }

        // 2. Load Static Parts (Eye, Pupil)
        SetStaticPart("Eye", "Eye/Eye", 0);
        SetStaticPart("Pupil", "Pupil/Pupil", 0);
    }

    private void SetStaticPart(string layerName, string resourcePath, int id)
    {
        Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(resourcePath, id);
        if (sheet == null) return;

        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.currentSheet = sheet;
            target.SetSprite(0);
        }
    }

    public void SetHair(int styleIndex)
    {
        SetStaticPart("Hair", "Hair/Hair", styleIndex);
    }

    public void SetSkinColor(Color color)
    {
        foreach (string part in skinParts)
        {
            VisualLayer target = layers.Find(l => l.name.Equals(part, System.StringComparison.OrdinalIgnoreCase));
            if (target != null && target.renderer != null) target.renderer.color = color;
        }
    }

    public void SetEyeColor(Color color)
    {
        VisualLayer target = layers.Find(l => l.name.Equals("Pupil", System.StringComparison.OrdinalIgnoreCase));
        if (target != null && target.renderer != null) target.renderer.color = color;
    }

    public void SetHairColor(Color color)
    {
        VisualLayer target = layers.Find(l => l.name.Equals("Hair", System.StringComparison.OrdinalIgnoreCase));
        if (target != null && target.renderer != null) target.renderer.color = color;
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
