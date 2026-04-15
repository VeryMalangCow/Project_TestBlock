using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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
    [SerializeField] private GameObject ghostSlotPanel; // 배경을 포함한 슬롯 전체 오브젝트
    [SerializeField] private Image ghostIcon;       
    [SerializeField] private TextMeshProUGUI ghostStackText; 

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;
    private InputAction modifierAction; 

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

    private void Awake()
    {
        // Ghost UI 초기화
        if (ghostSlotPanel != null)
        {
            // 드래그 슬롯이 클릭을 방해하지 않도록 설정
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
            }
        }
    }

    private void OnEnable()
    {
        inventoryAction?.Enable();
        modifierAction?.Enable();
    }

    private void OnDisable()
    {
        inventoryAction?.Disable();
        modifierAction?.Disable();
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
        if (inventoryAction != null && inventoryAction.WasPressedThisFrame()) ToggleInventory();
        HandleGhostIconFollow();
        RefreshUI();
    }

    private void HandleGhostIconFollow()
    {
        if (draggingItemData == null || ghostSlotPanel == null) return;
        ghostSlotPanel.transform.position = Mouse.current.position.ReadValue();
    }

    public void ToggleInventory()
    {
        if (isAnimating) return;
        isInventoryOpen = !isInventoryOpen;
        if (!isInventoryOpen && draggingItemData != null) CancelDragging();
        StopAllCoroutines();
        StartCoroutine(ToggleAnimationRoutine(isInventoryOpen));
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
            // [수정] 드래그 중인 원본 슬롯을 더 이상 숨기지 않음
            // 이제 인벤토리 데이터의 실제 상태(남은 수량)를 그대로 표시함
            if (i < inventory.slots.Length)
            {
                uiSlots[i].UpdateSlot(inventory.GetSlot(i));
            }
        }
    }

    #region Left Click Logic

    public void OnSlotClicked(int index)
    {
        if (!isInventoryOpen) return;
        if (PlayerController.Local == null || PlayerController.Local.Data == null) return;
        
        var inventory = PlayerController.Local.Data.inventory;
        var clickedSlot = inventory.GetSlot(index);
        if (clickedSlot == null) return;

        bool isModifierPressed = modifierAction != null && modifierAction.IsPressed();

        if (draggingItemData == null)
        {
            if (!clickedSlot.IsEmpty)
            {
                int amountToPick = isModifierPressed ? Mathf.CeilToInt(clickedSlot.stackCount / 2.0f) : clickedSlot.stackCount;

                draggingSlotIndex = index;
                draggingItemData = new PlayerInventorySlotData(clickedSlot.itemID, amountToPick);
                
                clickedSlot.stackCount -= amountToPick;
                if (clickedSlot.stackCount <= 0) clickedSlot.Clear();

                UpdateGhostUI();
            }
        }
        else
        {
            if (clickedSlot.IsEmpty)
            {
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                ClearDragging();
            }
            else if (clickedSlot.itemID == draggingItemData.itemID)
            {
                ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                int max = data != null ? data.maxStack : 999;
                
                int canAdd = max - clickedSlot.stackCount;
                int toAdd = Mathf.Min(canAdd, draggingItemData.stackCount);
                
                clickedSlot.stackCount += toAdd;
                draggingItemData.stackCount -= toAdd;
                
                if (draggingItemData.stackCount <= 0) ClearDragging();
                else UpdateGhostUI();
            }
            else
            {
                int tempID = clickedSlot.itemID;
                int tempCount = clickedSlot.stackCount;
                
                clickedSlot.itemID = draggingItemData.itemID;
                clickedSlot.stackCount = draggingItemData.stackCount;
                
                draggingItemData.itemID = tempID;
                draggingItemData.stackCount = tempCount;
                
                UpdateGhostUI();
            }
        }
    }

    #endregion

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
            ghostSlotPanel.SetActive(true);
            ItemData data = ItemDataManager.Instance.GetItem(draggingItemData.itemID);
            if (data != null)
            {
                if (ghostIcon != null) ghostIcon.sprite = data.icon;
                if (ghostStackText != null) 
                    ghostStackText.text = draggingItemData.stackCount > 1 ? draggingItemData.stackCount.ToString() : "";
            }
        }
    }

    private void ClearDragging()
    {
        draggingSlotIndex = -1;
        draggingItemData = null;
        if (ghostSlotPanel != null) ghostSlotPanel.SetActive(false);
    }

    private void CancelDragging()
    {
        if (draggingItemData != null)
        {
            var inventory = PlayerController.Local.Data.inventory;
            inventory.AddItem(draggingItemData.itemID, draggingItemData.stackCount);
        }
        ClearDragging();
    }

    #endregion
}
