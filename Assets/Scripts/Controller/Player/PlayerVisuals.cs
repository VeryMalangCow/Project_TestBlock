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

        // 2. Load Static Parts (Eye, Pupil - Always Visible)
        SetStaticPart("Eye", "Eye/Eye", 0);
        SetStaticPart("Pupil", "Pupil/Pupil", 0);
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
        SetStaticPart("Hair", "Hair/Hair", styleIndex);
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

    public void SetArmor(string category, int id)
    {
        // Standardize layer name mapping
        string layerName = category;
        if (category.Equals("Clothes", System.StringComparison.OrdinalIgnoreCase)) layerName = "Chestplate";
        else if (category.Equals("Heads", System.StringComparison.OrdinalIgnoreCase)) layerName = "Helmet";
        else if (category.EndsWith("s", System.StringComparison.OrdinalIgnoreCase)) layerName = category.Substring(0, category.Length - 1);
        
        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        if (id == -1)
        {
            target.currentSheet = null;
            if (target.renderer != null) target.renderer.sprite = null;
        }
        else
        {
            Sprite[] sheet = ResourceManager.Instance.GetArmorSprites(category, id);
            if (sheet != null)
            {
                target.currentSheet = sheet;
                target.SetSprite(0);
            }
        }

        if (layerName.Equals("Helmet", System.StringComparison.OrdinalIgnoreCase))
        {
            UpdateHairVisibility();
        }
    }

    private void UpdateHairVisibility()
    {
        VisualLayer helmetLayer = layers.Find(l => l.name.Equals("Helmet", System.StringComparison.OrdinalIgnoreCase));
        VisualLayer hairLayer = layers.Find(l => l.name.Equals("Hair", System.StringComparison.OrdinalIgnoreCase));

        if (hairLayer != null && hairLayer.renderer != null)
        {
            // Hide hair if helmet is equipped (id != -1)
            bool hasHelmet = helmetLayer != null && helmetLayer.currentSheet != null;
            hairLayer.renderer.enabled = !hasHelmet;
        }
    }

    #endregion

    #region Animation & Sync

    /// <summary>
    /// 속도와 상태 정보를 바탕으로 로컬 환경에서 애니메이션 프레임을 계산하여 재생합니다.
    /// 네트워크 변수를 쓰지 않고 각 클라이언트에서 개별적으로 연산하므로 트래픽이 절약됩니다.
    /// </summary>
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
