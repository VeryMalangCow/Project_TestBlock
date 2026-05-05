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
    [SerializeField] private SpriteRenderer heldItemRenderer; 
    [SerializeField] private Transform upperBodyContainer; 

    [Header("### Held Item Settings")]
    [SerializeField] private Vector2[] upperBodyPositions = new Vector2[11]; 

    [Header("### Animation Settings")]
    [SerializeField] private float walkAnimSpeedMultiplier = 2.5f;
    private float walkCycleTime;
    private int currentFrameIndex = -1;

    [Header("### Item Use Animation")]
    [SerializeField] private Transform itemUseRotationRoot; 
    [SerializeField] private float blockSwingOffset = 15f; 
    [SerializeField] private float swordSwingOffset = 60f; 
    [SerializeField] private float rotationReturnSpeed = 15f; 

    public float BlockSwingOffset => blockSwingOffset;
    public float SwordSwingOffset => swordSwingOffset;
    
    private bool isUsingItem = false;
    private float targetBaseAngle = 0f;
    private float currentSwingOffset = 0f;
    private float swingLerpTime = 0f;
    private int swingPhase = 0; 
    private float itemUseDuration = 0.2f;
    private float currentMaxSwingOffset = 15f;

    private MaterialPropertyBlock heldItemMPB;
    private int currentHeldItemID = -2;
    private bool isVisualsReady = false; 

    public bool IsFlipped { get; private set; }

    #endregion

    #region Init

    public void Init()
    {
        SetBody();
        heldItemMPB = new MaterialPropertyBlock();
        if (ItemHeldCacheManager.Instance != null)
            ItemHeldCacheManager.Instance.OnHeldIconLoaded += HandleHeldIconLoaded;
            
        if (heldItemRenderer != null) heldItemRenderer.enabled = false;
        isVisualsReady = true;

        if (currentHeldItemID >= 0) ApplyHeldItemVisuals(currentHeldItemID);
    }

    private void OnDestroy()
    {
        isVisualsReady = false;
        if (ItemHeldCacheManager.Instance != null)
            ItemHeldCacheManager.Instance.OnHeldIconLoaded -= HandleHeldIconLoaded;
    }

    private void HandleHeldIconLoaded(int loadedID)
    {
        if (currentHeldItemID == loadedID) ApplyHeldItemVisuals(loadedID);
    }

    #endregion

    #region Armor Management

    private readonly string[] skinParts = { "ArmBack", "Leg", "Body", "Head", "ArmFront" };

    public void SetBody()
    {
        foreach (string part in skinParts)
        {
            Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(part, 0);
            if (sheet == null) continue;

            VisualLayer target = layers.Find(l => l.name.Equals(part, System.StringComparison.OrdinalIgnoreCase));
            if (target != null) { target.currentSheet = sheet; target.SetSprite(0); }
        }

        SetStaticPart("Eye", "Eye", 0);
        SetStaticPart("Pupil", "Pupil", 0);
        SetHair(0); 

        SetArmor("Clothes", -1);
        SetArmor("Leggings", -1);
    }

    private void SetStaticPart(string layerName, string resourcePath, int id)
    {
        VisualLayer target = layers.Find(l => l.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        if (id == -1) { target.currentSheet = null; if (target.renderer != null) target.renderer.sprite = null; return; }

        Sprite[] sheet = ResourceManager.Instance.GetBodyPartSprites(resourcePath, id);
        if (sheet != null) { target.currentSheet = sheet; target.SetSprite(0); }
    }

    public void SetHair(int styleIndex) { SetStaticPart("Hair", "Hair", styleIndex); UpdateHairVisibility(); }

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
        if (category.Equals("Clothes", System.StringComparison.OrdinalIgnoreCase) || 
            category.Equals("Chestplate", System.StringComparison.OrdinalIgnoreCase))
        {
            SetChestplate(typeID); return;
        }

        string layerName = category;
        if (category.Equals("Heads", System.StringComparison.OrdinalIgnoreCase)) layerName = "Helmet";
        else if (category.Equals("Jetbag", System.StringComparison.OrdinalIgnoreCase)) layerName = "Jetbag";
        else if (category.Equals("Boots", System.StringComparison.OrdinalIgnoreCase)) layerName = "Boots";
        else if (category.Equals("Leggings", System.StringComparison.OrdinalIgnoreCase)) layerName = "Leggings";
        
        ApplyArmorToLayer(layerName, category, typeID);
        if (layerName.Equals("Helmet", System.StringComparison.OrdinalIgnoreCase)) UpdateHairVisibility();
    }

    private void SetChestplate(int typeID)
    {
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
            bool hasBase = category.Contains("Chestplate") || category.Equals("Leggings", System.StringComparison.OrdinalIgnoreCase);
            if (hasBase) sheet = ResourceManager.Instance.GetArmorSprites(category, "Base");
        }
        else sheet = ResourceManager.Instance.GetArmorSprites(category, typeID);

        target.currentSheet = sheet;
        if (sheet == null && target.renderer != null) target.renderer.sprite = null;
        else { int frame = IsUpperBodyLayer(layerName) ? 0 : Mathf.Max(0, currentFrameIndex); target.SetSprite(frame); }
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
        UpdateItemUseAnimation();

        int targetFrame = 0;
        if (isDashing) targetFrame = 10;
        else if (!isGrounded) targetFrame = 9;
        else
        {
            float absVelocityX = Mathf.Abs(horizontalVelocity);
            bool isReverse = false;
            if (absVelocityX > 0.1f) 
            {
                // 뒷걸음질 체크 (보는 방향과 이동 방향이 반대인 경우)
                bool isMovingLeft = horizontalVelocity < 0;
                if (isMovingLeft != IsFlipped) isReverse = true;

                float speed = absVelocityX * walkAnimSpeedMultiplier * Time.deltaTime;
                if (isReverse) walkCycleTime -= speed;
                else walkCycleTime += speed;

                // 순환 처리
                if (walkCycleTime < 0) walkCycleTime += 8f;
                targetFrame = 1 + (Mathf.FloorToInt(walkCycleTime) % 8); 
            }
            else { targetFrame = 0; walkCycleTime = 0; }
        }

        if (currentFrameIndex != targetFrame) { currentFrameIndex = targetFrame; SyncAnimation(currentFrameIndex); }
    }

    private void UpdateItemUseAnimation()
    {
        Transform targetRoot = itemUseRotationRoot != null ? itemUseRotationRoot : upperBodyContainer;
        if (targetRoot == null) return;

        float finalRotation = 0f;

        if (isUsingItem)
        {
            swingLerpTime += Time.deltaTime / itemUseDuration;
            if (swingLerpTime >= 1f) { swingLerpTime = 0f; swingPhase = 1 - swingPhase; }

            float startOffset = (swingPhase == 0) ? -currentMaxSwingOffset : currentMaxSwingOffset;
            float endOffset = (swingPhase == 0) ? currentMaxSwingOffset : -currentMaxSwingOffset;
            
            // [New] 인터페이스 기반의 애니메이션 곡선 선택
            // 현재는 모든 휘두르기에 역동적인 Ease-Out 적용
            float t = 1f - Mathf.Pow(1f - swingLerpTime, 3f); 

            currentSwingOffset = Mathf.Lerp(startOffset, endOffset, t);

            finalRotation = targetBaseAngle + currentSwingOffset;
        }
        else
        {
            // [Fix] 즉시 복귀 및 아이템 기본 각도 복구
            currentSwingOffset = 0f;
            finalRotation = 0f; 
        }

        targetRoot.localRotation = Quaternion.Euler(0, 0, finalRotation);
    }

    public void StartItemUseAnimation(float targetAngle, float duration, float maxOffset = 15f)
    {
        if (isUsingItem && targetBaseAngle == targetAngle) return; // 이미 같은 각도로 사용 중이면 무시
        
        isUsingItem = true;
        targetBaseAngle = targetAngle;
        itemUseDuration = duration;
        currentMaxSwingOffset = maxOffset;

        // [Fix] 애니메이션 시작 시점에 딱 한 번 비주얼(각도/위치) 갱신
        UpdateHeldItemTransform();
    }

    public void StopItemUseAnimation()
    {
        if (!isUsingItem) return;
        isUsingItem = false;

        // [Fix] 애니메이션 종료 시점에 딱 한 번 기본 상태로 복구
        UpdateHeldItemTransform();
    }

    public void SyncAnimation(int frameIndex)
    {
        foreach (var layer in layers)
        {
            if (IsUpperBodyLayer(layer.name)) layer.SetSprite(0);
            else layer.SetSprite(frameIndex);
        }

        if (upperBodyContainer != null)
        {
            Vector2 animOffset = (frameIndex >= 0 && frameIndex < upperBodyPositions.Length) ? upperBodyPositions[frameIndex] : Vector2.zero;
            upperBodyContainer.localPosition = new Vector3(animOffset.x, animOffset.y, 0);
        }
    }

    private bool IsUpperBodyLayer(string layerName)
    {
        return !(layerName.Equals("Leg", System.StringComparison.OrdinalIgnoreCase) || 
                 layerName.Equals("Leggings", System.StringComparison.OrdinalIgnoreCase) || 
                 layerName.Equals("Boots", System.StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateHeldItemTransform()
    {
        if (heldItemRenderer == null || currentHeldItemID < 0) return;

        ItemData data = ItemDataManager.Instance.GetItem(currentHeldItemID);
        if (data == null) return;

        // [New] ScriptableObject 데이터베이스에서 설정 가져오기
        var db = ItemDataManager.Instance.HeldVisualDatabase;
        if (db == null) return;

        // [Fix] weaponStats 제거에 따른 수정 (기본값 None 사용, 필요시 속성에서 가져오도록 확장 가능)
        var settings = db.GetSettings(data.type, WeaponType.None);

        // 상태에 따라 데이터 선택
        Vector2 targetPivot = isUsingItem ? settings.usePivot : settings.pivot;
        float targetRotation = isUsingItem ? settings.useRotation : settings.rotation;

        // 피봇 좌표 변환
        float px = (targetPivot.x - 32f) / 16f;
        float py = (targetPivot.y - 32f) / 16f;

        // 위치와 회전 즉시 적용
        heldItemRenderer.transform.localPosition = new Vector3(-px, -py, -0.01f);
        heldItemRenderer.transform.localRotation = Quaternion.Euler(0, 0, targetRotation);
    }    public void SetFlip(bool flipX)
    {
        IsFlipped = flipX;
        transform.localScale = new Vector3(flipX ? -1f : 1f, 1f, 1f);
        foreach (var layer in layers) if (layer.renderer != null) layer.renderer.flipX = false;
        if (heldItemRenderer != null) heldItemRenderer.flipX = false;
    }

    public void SetHeldItem(int itemID)
    {
        if (heldItemRenderer == null) return;
        if (!isVisualsReady) { currentHeldItemID = itemID; return; }
        if (itemID == currentHeldItemID && itemID != -1) return;

        currentHeldItemID = itemID;
        if (itemID == -1) heldItemRenderer.enabled = false;
        else ApplyHeldItemVisuals(itemID);
    }

    private void ApplyHeldItemVisuals(int itemID)
    {
        if (heldItemRenderer == null || ItemHeldCacheManager.Instance == null) return;
        int sliceIdx = ItemHeldCacheManager.Instance.GetSlotIndex(itemID);
        
        if (heldItemRenderer.sharedMaterial == null || heldItemRenderer.sharedMaterial.shader.name != "World/ItemArrayBatch")
            heldItemRenderer.sharedMaterial = ItemHeldCacheManager.Instance.ItemHeldMaterial;

        if (heldItemMPB == null) heldItemMPB = new MaterialPropertyBlock();
        heldItemRenderer.GetPropertyBlock(heldItemMPB);
        
        if (ItemHeldCacheManager.Instance.ItemHeldMaterial != null)
        {
            Texture2DArray heldArray = (Texture2DArray)ItemHeldCacheManager.Instance.ItemHeldMaterial.GetTexture("_MainTexArray");
            if (heldArray != null) heldItemMPB.SetTexture("_MainTexArray", heldArray);
        }

        heldItemMPB.SetFloat("_SliceIndex", (float)sliceIdx);
        heldItemRenderer.SetPropertyBlock(heldItemMPB);
        heldItemRenderer.enabled = (sliceIdx >= 0);
        UpdateHeldItemTransform();
    }

    #endregion
}
