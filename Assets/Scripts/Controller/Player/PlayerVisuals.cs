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

    [Header("### Held Item Settings")]
    [SerializeField] private Vector2 baseHeldItemPos = new Vector2(0.25f, -0.125f); 
    [SerializeField] private Vector2[] heldItemFrameOffsets = new Vector2[11]; 

    [Header("### Animation Settings")]
    [SerializeField] private float walkAnimSpeedMultiplier = 2.5f;
    private float walkCycleTime;
    private int currentFrameIndex = -1;

    private MaterialPropertyBlock heldItemMPB;
    private int currentHeldItemID = -2;
    private bool isVisualsReady = false; // 시각화 준비 완료 여부

    #endregion

    #region Init

    public void Init()
    {
        // Load static body sprites
        SetBody();

        heldItemMPB = new MaterialPropertyBlock();
        if (ItemHeldCacheManager.Instance != null)
            ItemHeldCacheManager.Instance.OnHeldIconLoaded += HandleHeldIconLoaded;
            
        // 초기에는 렌더러를 꺼둠 (에디터 기본 이미지 노출 방지)
        if (heldItemRenderer != null) heldItemRenderer.enabled = false;
        
        isVisualsReady = true;

        // [New] 만약 Init 전에 ID가 설정되었다면 지금 즉시 시각화 시작
        if (currentHeldItemID >= 0)
        {
            ApplyHeldItemVisuals(currentHeldItemID);
        }
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

        // [New] 들고 있는 아이템의 위치를 프레임 오프셋에 맞춰 조정
        UpdateHeldItemTransform(frameIndex);
    }

    private void UpdateHeldItemTransform(int frameIndex)
    {
        if (heldItemRenderer == null || currentHeldItemID < 0) return;

        ItemData data = ItemDataManager.Instance.GetItem(currentHeldItemID);
        if (data == null) return;

        // [New] 타입별 레지스트리에서 설정값 가져오기
        var settings = HeldItemVisualRegistry.GetSettings(data.type);

        Vector2 animOffset = (frameIndex >= 0 && frameIndex < heldItemFrameOffsets.Length) 
            ? heldItemFrameOffsets[frameIndex] 
            : Vector2.zero;

        // 1. 기본 애니메이션 위치 계산
        float posX = (baseHeldItemPos.x + animOffset.x) * (IsFlipped ? -1f : 1f);
        float posY = baseHeldItemPos.y + animOffset.y;

        // 2. [Pivot Logic] 64x64 캔버스의 (0,0) 정렬 기준 계산
        // 캔버스의 왼쪽 아래가 (0,0)이므로, 피벗값만큼 오브젝트를 밀어줍니다.
        // 유니티 SpriteRenderer의 기본 피벗(Center) 기준(-2.0, -2.0)에서 오프셋 계산
        float pivotOffsetX = (settings.pivot.x - 32f) / 16f * (IsFlipped ? 1f : -1f);
        float pivotOffsetY = -(settings.pivot.y - 32f) / 16f;

        heldItemRenderer.transform.localPosition = new Vector3(posX - pivotOffsetX, posY + pivotOffsetY, -0.01f);
        
        // 3. [Rotation Logic] 
        float finalRot = settings.rotation * (IsFlipped ? -1f : 1f);
        heldItemRenderer.transform.localRotation = Quaternion.Euler(0, 0, finalRot);
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

        // [Fix] 들고 있는 아이템 렌더러에도 좌우 반전 적용
        if (heldItemRenderer != null)
        {
            heldItemRenderer.flipX = flipX;
            
            // 위치 및 회전 재계산
            UpdateHeldItemTransform(currentFrameIndex);
        }
    }

    public void SetHeldItem(int itemID)
    {
        if (heldItemRenderer == null) return;

        // [Key] 시각화 준비가 안 되었으면 무시 (READY 상태에서 호출될 것임)
        if (!isVisualsReady)
        {
            currentHeldItemID = itemID; 
            return;
        }

        if (itemID == currentHeldItemID && itemID != -1) 
        {
            return;
        }

        currentHeldItemID = itemID;

        if (itemID == -1)
        {
            heldItemRenderer.enabled = false;
        }
        else
        {
            // 아이템이 있으면 일단 내부 로직 수행 (로드가 완료되면 켜짐)
            ApplyHeldItemVisuals(itemID);
        }
    }

    private void ApplyHeldItemVisuals(int itemID)
    {
        if (heldItemRenderer == null || ItemHeldCacheManager.Instance == null) return;

        int sliceIdx = ItemHeldCacheManager.Instance.GetSlotIndex(itemID);
        
        // 1. 머티리얼 설정 및 텍스처 배열 유효성 확인
        if (heldItemRenderer.sharedMaterial == null || heldItemRenderer.sharedMaterial.shader.name != "World/ItemArrayBatch")
        {
            heldItemRenderer.sharedMaterial = ItemHeldCacheManager.Instance.ItemHeldMaterial;
        }

        if (heldItemMPB == null) heldItemMPB = new MaterialPropertyBlock();
        heldItemRenderer.GetPropertyBlock(heldItemMPB);
        
        // [Important] 텍스처 배열 주입
        if (ItemHeldCacheManager.Instance.ItemHeldMaterial != null)
        {
            Texture2DArray heldArray = (Texture2DArray)ItemHeldCacheManager.Instance.ItemHeldMaterial.GetTexture("_MainTexArray");
            if (heldArray != null) heldItemMPB.SetTexture("_MainTexArray", heldArray);
        }

        // 2. 슬라이스 인덱스 설정 (로딩 중(-1)이면 셰이더에서 투명 처리)
        heldItemMPB.SetFloat("_SliceIndex", (float)sliceIdx);
        heldItemRenderer.SetPropertyBlock(heldItemMPB);
        
        // [Key] 이미지가 로드되었을 때만(sliceIdx >= 0) 렌더러를 켬
        // 이로 인해 에디터의 빈 이미지가 노출되는 현상을 원천 차단함
        heldItemRenderer.enabled = (sliceIdx >= 0);
        
        UpdateHeldItemTransform(currentFrameIndex);
    }

    public bool IsFlipped { get; private set; }

    #endregion
}
