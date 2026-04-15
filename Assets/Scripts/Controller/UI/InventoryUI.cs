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

    [Header("### Drag & Drop UI")]
    [SerializeField] private Image ghostIcon;       
    [SerializeField] private TextMeshProUGUI ghostStackText; 

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private List<CanvasGroup> toggleCanvasGroups = new List<CanvasGroup>();
    private List<RectTransform> toggleRects = new List<RectTransform>();

    private bool isInitialized = false;
    private const int HOTBAR_COUNT = 10; 
    private bool isInventoryOpen = false;
    private bool isAnimating = false;

    private int draggingSlotIndex = -1;
    private PlayerInventorySlotData draggingItemData = null;

    private void Awake()
    {
        if (ghostIcon != null)
        {
            ghostIcon.raycastTarget = false; 
            ghostIcon.gameObject.SetActive(false);
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
            if (playerMap != null) inventoryAction = playerMap.FindAction("Inventory");
        }
    }

    private void OnEnable() => inventoryAction?.Enable();
    private void OnDisable() => inventoryAction?.Disable();

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
        if (draggingItemData == null || ghostIcon == null) return;
        ghostIcon.rectTransform.position = Mouse.current.position.ReadValue();
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
            if (i == draggingSlotIndex) uiSlots[i].UpdateSlot(null);
            else if (i < inventory.slots.Length) uiSlots[i].UpdateSlot(inventory.GetSlot(i));
        }
    }

    public void OnSlotClicked(int index)
    {
        if (PlayerController.Local == null || PlayerController.Local.Data == null) return;
        var inventory = PlayerController.Local.Data.inventory;
        var clickedSlot = inventory.GetSlot(index);
        if (clickedSlot == null) return;

        if (draggingItemData == null)
        {
            if (!clickedSlot.IsEmpty)
            {
                draggingSlotIndex = index;
                draggingItemData = new PlayerInventorySlotData(clickedSlot.itemID, clickedSlot.stackCount);
                clickedSlot.Clear();
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
                int addAmount = Mathf.Min(canAdd, draggingItemData.stackCount);
                clickedSlot.stackCount += addAmount;
                draggingItemData.stackCount -= addAmount;
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

    private void UpdateGhostUI()
    {
        if (draggingItemData == null || draggingItemData.IsEmpty)
        {
            ClearDragging();
            return;
        }

        if (ghostIcon != null)
        {
            ghostIcon.gameObject.SetActive(true);
            ItemData data = ItemDataManager.Instance.GetItem(draggingItemData.itemID);
            if (data != null)
            {
                ghostIcon.sprite = data.icon;
                if (ghostStackText != null) ghostStackText.text = draggingItemData.stackCount > 1 ? draggingItemData.stackCount.ToString() : "";
            }
        }
    }

    private void ClearDragging()
    {
        draggingSlotIndex = -1;
        draggingItemData = null;
        if (ghostIcon != null) ghostIcon.gameObject.SetActive(false);
    }

    private void CancelDragging()
    {
        if (draggingItemData != null && draggingSlotIndex != -1)
        {
            var inventory = PlayerController.Local.Data.inventory;
            var originalSlot = inventory.GetSlot(draggingSlotIndex);
            if (originalSlot != null && originalSlot.IsEmpty)
            {
                originalSlot.itemID = draggingItemData.itemID;
                originalSlot.stackCount = draggingItemData.stackCount;
            }
            else inventory.AddItem(draggingItemData.itemID, draggingItemData.stackCount);
        }
        ClearDragging();
    }
}
