using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("### Settings")]
    [SerializeField] private Transform hotbarParent; 
    [SerializeField] private Transform mainInventoryParent; 
    
    [Header("### UI Panels")]
    [SerializeField] private GameObject[] alwaysOnObjects; 
    [SerializeField] private GameObject[] toggleObjects;   

    [Header("### Animation Settings")]
    [SerializeField] private float animationDuration = 0.2f; 
    [SerializeField] private float yOffset = -50f;          

    [Header("### Drag & Drop UI (Ghost Slot)")]
    [SerializeField] private GameObject ghostSlotPanel; 
    [SerializeField] private Image ghostIcon;       
    [SerializeField] private TextMeshProUGUI ghostStackText; 

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;
    private InputAction modifierAction; 
    private InputAction interact00Action;
    private InputAction interact01Action;
    private InputAction pointAction;

    [Header("### UI Interaction")]
    [SerializeField] private GraphicRaycaster raycaster;
    [SerializeField] private EventSystem eventSystem;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private List<CanvasGroup> toggleCanvasGroups = new List<CanvasGroup>();
    private List<RectTransform> toggleRects = new List<RectTransform>();

    private bool isInitialized = false;
    private const int HOTBAR_COUNT = 10; 
    private bool isInventoryOpen = false;
    private bool isAnimating = false;

    // 드래그 상태 데이터
    private int draggingSlotIndex = -1;
    private PlayerInventorySlotData draggingItemData = null;
    private AsyncOperationHandle<Sprite> ghostIconHandle;
    private int currentGhostItemID = -2; // -1이 공백이므로 -2로 초기화

    private void Awake()
    {
        // Raycaster 및 EventSystem 자동 할당 (없을 경우)
        if (raycaster == null) raycaster = GetComponentInParent<GraphicRaycaster>();
        if (eventSystem == null) eventSystem = FindAnyObjectByType<EventSystem>();

        // Ghost UI 초기화
        if (ghostSlotPanel != null)
        {
            if (ghostSlotPanel.TryGetComponent<CanvasGroup>(out var cg))
            {
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            ghostSlotPanel.SetActive(false);
        }

        if (alwaysOnObjects != null)
        {
            foreach (var obj in alwaysOnObjects) if (obj != null) obj.SetActive(true);
        }

        if (toggleObjects != null)
        {
            foreach (var obj in toggleObjects)
            {
                if (obj == null) continue;
                if (!obj.TryGetComponent<CanvasGroup>(out var cg)) cg = obj.AddComponent<CanvasGroup>();
                toggleCanvasGroups.Add(cg);
                if (obj.transform is RectTransform rt) toggleRects.Add(rt);

                obj.SetActive(true);
                cg.alpha = 0;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                
                if (obj.transform is RectTransform initialRt)
                {
                    initialRt.anchoredPosition = new Vector2(initialRt.anchoredPosition.x, yOffset);
                }
            }
        }

        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap != null)
            {
                inventoryAction = playerMap.FindAction("Inventory");
                modifierAction = playerMap.FindAction("Modifier"); 
                interact00Action = playerMap.FindAction("Interact_00");
                interact01Action = playerMap.FindAction("Interact_01");
                pointAction = playerMap.FindAction("Point");
            }
        }
    }

    private void OnEnable()
    {
        inventoryAction?.Enable();
        modifierAction?.Enable();
        interact00Action?.Enable();
        interact01Action?.Enable();
        pointAction?.Enable();
    }

    private void OnDisable()
    {
        inventoryAction?.Disable();
        modifierAction?.Disable();
        interact00Action?.Disable();
        interact01Action?.Disable();
        pointAction?.Disable();
    }

    private void Start()
    {
        if (gameObject.activeInHierarchy) StartCoroutine(InitializeRoutine());
    }

    private IEnumerator InitializeRoutine()
    {
        while (PlayerController.Local == null || PlayerController.Local.Data == null) yield return null;
        InitializeUI();
    }

    private void Update()
    {
        if (!isInitialized) return;
        
        // 인벤토리 토글
        if (inventoryAction != null && inventoryAction.WasPressedThisFrame()) ToggleInventory();
        
        // 고스트 아이콘 위치 업데이트
        HandleGhostIconFollow();

        // UI 상호작용 및 갱신 (인벤토리가 열려 있을 때만)
        if (isInventoryOpen)
        {
            if (interact00Action != null && interact00Action.WasPressedThisFrame()) HandleInputInteraction(0);
            else if (interact01Action != null && interact01Action.WasPressedThisFrame()) HandleInputInteraction(1);
            
            // 열려 있을 때만 데이터 갱신
            RefreshUI();
        }
    }

    private void HandleGhostIconFollow()
    {
        if (draggingItemData == null || ghostSlotPanel == null || pointAction == null) return;
        ghostSlotPanel.transform.position = pointAction.ReadValue<Vector2>();
    }

    private void HandleInputInteraction(int buttonIndex)
    {
        if (raycaster == null || pointAction == null) return;

        // PointerEventData를 수동으로 생성 (InputSystem 좌표 사용)
        PointerEventData eventData = new PointerEventData(eventSystem);
        eventData.position = pointAction.ReadValue<Vector2>();

        List<RaycastResult> results = new List<RaycastResult>();
        raycaster.Raycast(eventData, results);

        bool hitSlot = false;
        foreach (var result in results)
        {
            if (result.gameObject.TryGetComponent<InventorySlotUI>(out var slotUI))
            {
                OnSlotClicked(slotUI.SlotIndex, buttonIndex);
                hitSlot = true;
                break;
            }
        }

        // [Logic] UI 밖 공간(빈 공간) 처리
        if (!hitSlot)
        {
            // 우클릭(Interact_01)이고 아이템을 들고 있다면 버리기
            if (buttonIndex == 1 && draggingItemData != null)
            {
                RequestDropItem();
            }
        }
    }

    public void ToggleInventory()
    {
        if (isAnimating) return;
        isInventoryOpen = !isInventoryOpen;

        // [Logic] 인벤토리를 닫을 때 아이템을 들고 있다면 버리기
        if (!isInventoryOpen && draggingItemData != null)
        {
            RequestDropItem();
        }

        StopAllCoroutines();
        StartCoroutine(ToggleAnimationRoutine(isInventoryOpen));
    }

    private void RequestDropItem()
    {
        if (draggingItemData == null || PlayerController.Local == null) return;

        // [Fix] 현재 바라보는 방향을 계산하여 RPC와 함께 전달
        float lookDir = PlayerController.Local.IsFlipped ? -1f : 1f;

        // 서버에 아이템 버리기 요청
        PlayerController.Local.DropItemServerRpc(draggingItemData.itemID, draggingItemData.stackCount, lookDir);
        
        // 로컬 상태 초기화
        ClearDragging();
    }

    private IEnumerator ToggleAnimationRoutine(bool isOpen)
    {
        isAnimating = true;
        float elapsedTime = 0f;
        float startAlpha = isOpen ? 0f : 1f;
        float endAlpha = isOpen ? 1f : 0f;
        float startY = isOpen ? yOffset : 0f;
        float endY = isOpen ? 0f : yOffset;

        UpdateSlotsVisibility(isOpen);

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            float curveT = 1f - Mathf.Pow(1f - t, 3f); 
            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, curveT);
            float currentY = Mathf.Lerp(startY, endY, curveT);

            for (int i = 0; i < toggleCanvasGroups.Count; i++)
            {
                if (toggleCanvasGroups[i] != null) toggleCanvasGroups[i].alpha = currentAlpha;
                if (i < toggleRects.Count && toggleRects[i] != null)
                    toggleRects[i].anchoredPosition = new Vector2(toggleRects[i].anchoredPosition.x, currentY);
            }
            yield return null;
        }

        for (int i = 0; i < toggleCanvasGroups.Count; i++)
        {
            if (toggleCanvasGroups[i] != null)
            {
                toggleCanvasGroups[i].alpha = endAlpha;
                toggleCanvasGroups[i].interactable = isOpen;
                toggleCanvasGroups[i].blocksRaycasts = isOpen;
            }
            if (i < toggleRects.Count && toggleRects[i] != null)
                toggleRects[i].anchoredPosition = new Vector2(toggleRects[i].anchoredPosition.x, endY);
        }
        isAnimating = false;
    }

    private void InitializeUI()
    {
        if (isInitialized) return;
        uiSlots.Clear();
        CollectSlotsFromParent(hotbarParent);
        CollectSlotsFromParent(mainInventoryParent);
        isInitialized = true;
    }

    private void CollectSlotsFromParent(Transform parent)
    {
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).TryGetComponent<InventorySlotUI>(out var slotUI))
            {
                int currentIndex = uiSlots.Count;
                uiSlots.Add(slotUI);
                slotUI.Init(this, currentIndex); 
                slotUI.gameObject.SetActive(currentIndex < HOTBAR_COUNT);
            }
        }
    }

    private void UpdateSlotsVisibility(bool showAll)
    {
        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (i >= HOTBAR_COUNT) uiSlots[i].gameObject.SetActive(showAll);
        }
    }

    public void RefreshUI()
    {
        if (PlayerController.Local == null || PlayerController.Local.Data == null || !isInitialized) return;

        var inventory = PlayerController.Local.Data.inventory;
        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (i < inventory.slots.Length)
            {
                uiSlots[i].UpdateSlot(inventory.GetSlot(i));
            }
        }
    }

    #region Interaction Logic

    public void OnSlotClicked(int index, int buttonIndex)
    {
        if (!isInventoryOpen) return;
        if (PlayerController.Local == null || PlayerController.Local.Data == null) return;
        
        var inventory = PlayerController.Local.Data.inventory;
        var clickedSlot = inventory.GetSlot(index);
        if (clickedSlot == null) return;

        bool isModifierPressed = modifierAction != null && modifierAction.IsPressed();

        if (buttonIndex == 0) // Interact_00 (Left Click)
        {
            HandleInteract00(clickedSlot, isModifierPressed);
        }
        else if (buttonIndex == 1) // Interact_01 (Right Click)
        {
            HandleInteract01(clickedSlot, isModifierPressed);
        }

        UpdateGhostUI();
    }

    private void HandleInteract00(PlayerInventorySlotData clickedSlot, bool isModifierPressed)
    {
        if (draggingItemData == null)
        {
            if (!clickedSlot.IsEmpty)
            {
                // [Logic] 전체 들기 또는 절반 들기 (Modifier)
                int amountToPick = isModifierPressed ? Mathf.CeilToInt(clickedSlot.stackCount / 2.0f) : clickedSlot.stackCount;
                draggingItemData = new PlayerInventorySlotData(clickedSlot.itemID, amountToPick);
                
                clickedSlot.stackCount -= amountToPick;
                if (clickedSlot.stackCount <= 0) clickedSlot.Clear();
            }
        }
        else
        {
            if (clickedSlot.IsEmpty)
            {
                // [Logic] 빈 슬롯에 전부 내려놓기
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                ClearDragging();
            }
            else if (clickedSlot.itemID == draggingItemData.itemID)
            {
                // [Logic] 같은 아이템일 경우 스택 합치기
                ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                int max = data != null ? data.maxStack : 999;
                
                int canAdd = max - clickedSlot.stackCount;
                int toAdd = Mathf.Min(canAdd, draggingItemData.stackCount);
                
                clickedSlot.stackCount += toAdd;
                draggingItemData.stackCount -= toAdd;
                
                if (draggingItemData.stackCount <= 0) ClearDragging();
            }
            else
            {
                // [Logic] 다른 아이템일 경우 교체 (Swap)
                int tempID = clickedSlot.itemID;
                int tempCount = clickedSlot.stackCount;
                
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                
                draggingItemData.itemID = tempID;
                draggingItemData.stackCount = tempCount;
            }
        }
    }

    private void HandleInteract01(PlayerInventorySlotData clickedSlot, bool isModifierPressed)
    {
        if (draggingItemData == null)
        {
            if (!clickedSlot.IsEmpty)
            {
                // [Logic] 1개 또는 10개(Modifier) 들어올리기
                int amountToPick = isModifierPressed ? Mathf.Min(10, clickedSlot.stackCount) : 1;
                draggingItemData = new PlayerInventorySlotData(clickedSlot.itemID, amountToPick);
                
                clickedSlot.stackCount -= amountToPick;
                if (clickedSlot.stackCount <= 0) clickedSlot.Clear();
            }
        }
        else
        {
            if (clickedSlot.IsEmpty)
            {
                // [Logic] 빈 슬롯에 전부 내려놓기 (User Requirement: Drop All)
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                ClearDragging();
            }
            else if (clickedSlot.itemID == draggingItemData.itemID)
            {
                // [Logic] 같은 아이템일 경우 1개 또는 10개(Modifier) 더 들어올리기
                ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                int max = data != null ? data.maxStack : 999;

                int amountToPick = isModifierPressed ? Mathf.Min(10, clickedSlot.stackCount) : 1;
                
                // 고스트 슬롯(들고 있는 아이템)이 더 받을 수 있는지 확인
                int canPick = max - draggingItemData.stackCount;
                int actualPick = Mathf.Min(amountToPick, canPick);

                if (actualPick > 0)
                {
                    draggingItemData.stackCount += actualPick;
                    clickedSlot.stackCount -= actualPick;
                    if (clickedSlot.stackCount <= 0) clickedSlot.Clear();
                }
            }
            else
            {
                // [Logic] 다른 아이템일 경우 교체
                int tempID = clickedSlot.itemID;
                int tempCount = clickedSlot.stackCount;
                
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                
                draggingItemData.itemID = tempID;
                draggingItemData.stackCount = tempCount;
            }
        }
    }

    #endregion

    private void OnDestroy()
    {
        ReleaseGhostIcon();
    }

    private void ReleaseGhostIcon()
    {
        if (ghostIconHandle.IsValid())
        {
            Addressables.Release(ghostIconHandle);
        }
    }

    #region Helper Methods

    private void UpdateGhostUI()
    {
        if (draggingItemData == null || draggingItemData.IsEmpty)
        {
            ClearDragging();
            return;
        }

        if (ghostSlotPanel != null)
        {
            // [Fix] 위치 동기화
            if (pointAction != null) ghostSlotPanel.transform.position = pointAction.ReadValue<Vector2>();
            ghostSlotPanel.SetActive(true);

            // [ID 캐싱] 아이템이 바뀌었을 때만 중앙 캐시에서 아이콘 가져오기
            if (draggingItemData.itemID != currentGhostItemID)
            {
                currentGhostItemID = draggingItemData.itemID;
                Sprite icon = ItemDataManager.Instance.GetItemIcon(currentGhostItemID);
                if (ghostIcon != null)
                {
                    ghostIcon.sprite = icon;
                    ghostIcon.enabled = (icon != null);
                }
            }

            if (ghostStackText != null) 
                ghostStackText.text = draggingItemData.stackCount > 1 ? draggingItemData.stackCount.ToString() : "";
        }
    }

    private void ClearDragging()
    {
        currentGhostItemID = -2;
        draggingSlotIndex = -1;
        draggingItemData = null;
        if (ghostSlotPanel != null) ghostSlotPanel.SetActive(false);
    }

    private void CancelDragging()
    {
        // 이제 인벤토리를 닫을 때 바닥에 버리므로 CancelDragging은 직접 버리기 로직으로 대체됨
        RequestDropItem();
    }

    #endregion
}
