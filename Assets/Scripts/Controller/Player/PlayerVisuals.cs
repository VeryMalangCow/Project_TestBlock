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
    [SerializeField] private float pickaxeSwingOffset = 30f; 
    [SerializeField] private float rotationReturnSpeed = 15f; 

    public float BlockSwingOffset => blockSwingOffset;
    public float SwordSwingOffset => swordSwingOffset;
    public float PickaxeSwingOffset => pickaxeSwingOffset;
    
    private bool isUsingItem = false;
    private bool stopRequested = false; 
    private bool isStrokeAnimation = false; 
    private bool isFirstSwing = true; 
    private float targetBaseAngle = 0f;
    private float activeBaseAngle = 0f; // [New] 부드러운 에이밍을 위한 활성 베이스 각도
    private float currentSwingOffset = 0f;
    private float swingLerpTime = 0f;
    private int swingPhase = 0; 
    private float itemUseDuration = 0.2f;
    private float currentMaxSwingOffset = 15f;

    public float ActiveBaseAngle => activeBaseAngle; // [New] 외부(PlayerMovement 등)에서 참조 가능하도록 노출

    public void UpdateContinuousAim(float angle)
    {
        if (isUsingItem) targetBaseAngle = angle;
    }

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
            // 베이스 각도(에이밍) 부드럽게 추종 (Snap 방지의 핵심)
            float followSpeed = isStrokeAnimation ? rotationReturnSpeed : 100f; 
            activeBaseAngle = Mathf.LerpAngle(activeBaseAngle, targetBaseAngle, Time.deltaTime * followSpeed);

            // [Fix] 현재 swingLerpTime으로 먼저 오프셋 계산 (첫 프레임 0 보장)
            float t = 1f - Mathf.Pow(1f - swingLerpTime, 3f); 
            float startOffset, endOffset;

            if (isStrokeAnimation && isFirstSwing)
            {
                startOffset = currentMaxSwingOffset;
                endOffset = -currentMaxSwingOffset;
            }
            else
            {
                bool isForward = (swingPhase % 2 == 0);
                startOffset = isForward ? currentMaxSwingOffset : -currentMaxSwingOffset;
                endOffset = isForward ? -currentMaxSwingOffset : currentMaxSwingOffset;
            }
            
            currentSwingOffset = Mathf.Lerp(startOffset, endOffset, t);
            
            // [Fix] 정면 0도 시스템에 맞추기 위해 렌더링 시점에만 +90 적용
            finalRotation = activeBaseAngle + currentSwingOffset + 90f;

            // 계산 후 시간 갱신
            swingLerpTime += Time.deltaTime / itemUseDuration;

            // [Surgical Fix] 한 루프 완료 체크
            if (swingLerpTime >= 1f) 
            { 
                // 도구류(Stroke)는 스스로 다음 루프를 돌지 않고, 오직 StartItemUseAnimation 신호를 대기함
                if (isStrokeAnimation)
                {
                    isUsingItem = false;
                    swingPhase++; 
                    isFirstSwing = false; 

                    // [Fix] 사이클이 끝나면 즉시 아이템 트랜스폼을 갱신하여 대기 자세 피봇으로 복귀 보장
                    UpdateHeldItemTransform();

                    if (stopRequested)
                    {
                        stopRequested = false;
                    }
                }
                else
                {
                    // 무기류는 기존 루프 유지
                    swingLerpTime = 0f; 
                    swingPhase++; 
                    isFirstSwing = false; 
                }
            }
        }
        else
        {
            activeBaseAngle = 0f;
            currentSwingOffset = 0f;
            finalRotation = 0f;
        }

        targetRoot.localRotation = Quaternion.Euler(0, 0, finalRotation);
    }

    public void StartItemUseAnimation(float targetAngle, float duration, float maxOffset = 15f, bool isStroke = false)
    {
        // [New Surgical Fix] 어떤 상황에서도 도구 여부 플래그를 먼저 갱신하여 상태 꼬임 방지
        isStrokeAnimation = isStroke;

        // [Key] 이미 사용 중인 도구류는 각도 목표만 업데이트 (Snap 방지)
        if (isStroke && isUsingItem)
        {
            targetBaseAngle = targetAngle;
            activeBaseAngle = targetAngle;

            // [Surgical Fix] 재입력 시 즉시 진행도를 0으로 리셋하여 타격 시점 동기화
            swingLerpTime = 0f;
            swingPhase++;
            isFirstSwing = false;

            itemUseDuration = duration;
            currentMaxSwingOffset = maxOffset;
            stopRequested = false; 
            return;
        }
        
        // 무기류 혹은 멈춰있던 도구류 시작
        if (!isStroke && isUsingItem && targetBaseAngle == targetAngle) return;

        // [Fix] 처음 시작할 때 에이밍 각도를 즉시 스냅하여 반응성 개선
        activeBaseAngle = targetAngle;

        isUsingItem = true;
        stopRequested = false;
        isStrokeAnimation = isStroke;
        
        // [Surgical Fix] 도구류가 멈춰있다가 다시 시작할 때, 이전 방향의 반대부터 시작하도록 보장
        if (isStroke)
        {
            if (swingPhase == 0) isFirstSwing = true;
            // swingPhase는 Update에서 이미 증가되었으므로 그대로 사용
        }
        else
        {
            isFirstSwing = true; 
            swingPhase = 0;
        }

        targetBaseAngle = targetAngle;
        itemUseDuration = duration;
        currentMaxSwingOffset = maxOffset;

        swingLerpTime = 0f;

        UpdateHeldItemTransform();
    }

    public void StopItemUseAnimation()
    {
        if (!isUsingItem) return;

        if (isStrokeAnimation)
        {
            // 도구류: 진행 중인 휘두르기를 마저 끝내고 멈추도록 설정
            stopRequested = true;
        }
        else
        {
            // 무기류: 원래대로 즉시 중단
            isUsingItem = false;
            stopRequested = false;
        }

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
    }

    public void SetFlip(bool flipX)
    {
        if (IsFlipped != flipX)
        {
            // [Note] 로컬 각도를 유지하면 localScale.x 반전에 의해 자동으로 새로운 앞방향을 가리키게 됨
        }

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
