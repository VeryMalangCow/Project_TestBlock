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

    [Header("### Animation Settings")]
    [SerializeField] private float walkAnimSpeedMultiplier = 2.5f;
    private float walkCycleTime;
    private int currentFrameIndex = -1;

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
        // 1. Load Skin Parts (Always Visible)
        foreach (string part in skinParts)
        {
            Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(part, 0);
            if (sheet == null) continue;

            VisualLayer target = layers.Find(l => l.name.Equals(part, System.StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.currentSheet = sheet;
                target.SetSprite(0);
            }
        }

        // 2. Load Static Parts (Eye, Pupil, Hair - Always Visible)
        SetStaticPart("Eye", "Eye", 0);
        SetStaticPart("Pupil", "Pupil", 0);
        SetHair(0); 

        // 3. Load Initial Equipment Bases (Clothes, Leggings)
        SetArmor("Clothes", -1);
        SetArmor("Leggings", -1);
    }

    private void SetStaticPart(string layerName, string resourcePath, int id)
    {
        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        if (id == -1)
        {
            target.currentSheet = null;
            if (target.renderer != null) target.renderer.sprite = null;
            return;
        }

        Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(resourcePath, id);
        if (sheet != null)
        {
            target.currentSheet = sheet;
            target.SetSprite(0);
        }
    }

    public void SetHair(int styleIndex)
    {
        SetStaticPart("Hair", "Hair", styleIndex);
        UpdateHairVisibility();
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

    public void SetArmor(string category, int typeID)
    {
        // Chestplate is special: 3 parts (Body, ArmFront, ArmBack)
        if (category.Equals("Clothes", System.StringComparison.OrdinalIgnoreCase) || 
            category.Equals("Chestplate", System.StringComparison.OrdinalIgnoreCase))
        {
            SetChestplate(typeID);
            return;
        }

        // Regular equipment mapping
        string layerName = category;
        
        // [Fix] 명칭 변환 예외 처리 (Heads -> Helmet) 및 복수형 유지
        if (category.Equals("Heads", System.StringComparison.OrdinalIgnoreCase)) 
            layerName = "Helmet";
        else if (category.Equals("Jetbag", System.StringComparison.OrdinalIgnoreCase))
            layerName = "Jetbag";
        else if (category.Equals("Boots", System.StringComparison.OrdinalIgnoreCase))
            layerName = "Boots";
        else if (category.Equals("Leggings", System.StringComparison.OrdinalIgnoreCase))
            layerName = "Leggings";
        
        ApplyArmorToLayer(layerName, category, typeID);

        if (layerName.Equals("Helmet", System.StringComparison.OrdinalIgnoreCase))
        {
            UpdateHairVisibility();
        }
    }

    private void SetChestplate(int typeID)
    {
        // 3-part set loading
        ApplyArmorToLayer("Chestplate", "Chestplate", typeID);
        ApplyArmorToLayer("ChestplateArmFront", "ChestplateArmFront", typeID);
        ApplyArmorToLayer("ChestplateArmBack", "ChestplateArmBack", typeID);
    }

    private void ApplyArmorToLayer(string layerName, string category, int typeID)
    {
        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        Sprite[] sheet = null;
        if (typeID == -1)
        {
            // [Fix] Base 이미지가 존재하는 파츠만 로딩 시도 (Chestplate, Leggings 관련)
            bool hasBase = category.Contains("Chestplate") || category.Equals("Leggings", System.StringComparison.OrdinalIgnoreCase);
            
            if (hasBase)
            {
                sheet = ResourceManager.Instance.GetArmorSprites(category, "Base");
            }
            // Base가 없는 파츠(Helmet, Boots 등)는 sheet가 null인 상태로 유지되어 스프라이트가 비워짐
        }
        else
        {
            sheet = ResourceManager.Instance.GetArmorSprites(category, typeID);
        }

        target.currentSheet = sheet;
        if (sheet == null && target.renderer != null) target.renderer.sprite = null;
        else target.SetSprite(Mathf.Max(0, currentFrameIndex));
    }

    private void UpdateHairVisibility()
    {
        VisualLayer helmetLayer = layers.Find(l => l.name.Equals("Helmet", System.StringComparison.OrdinalIgnoreCase));
        VisualLayer hairLayer = layers.Find(l => l.name.Equals("Hair", System.StringComparison.OrdinalIgnoreCase));

        if (hairLayer != null && hairLayer.renderer != null)
        {
            bool hasHelmet = helmetLayer != null && helmetLayer.currentSheet != null;
            hairLayer.renderer.enabled = !hasHelmet;
        }
    }

    #endregion

    #region Animation & Sync

    public void UpdateVisuals(float horizontalVelocity, bool isGrounded, bool isDashing)
    {
        int targetFrame = 0;

        if (isDashing)
        {
            targetFrame = 10;
        }
        else if (!isGrounded)
        {
            targetFrame = 9;
        }
        else
        {
            float absVelocityX = Mathf.Abs(horizontalVelocity);
            if (absVelocityX > 0.1f)
            {
                walkCycleTime += Time.deltaTime * absVelocityX * walkAnimSpeedMultiplier;
                targetFrame = 1 + (Mathf.FloorToInt(walkCycleTime) % 8);
            }
            else
            {
                targetFrame = 0;
                walkCycleTime = 0;
            }
        }

        if (currentFrameIndex != targetFrame)
        {
            currentFrameIndex = targetFrame;
            SyncAnimation(currentFrameIndex);
        }
    }

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
