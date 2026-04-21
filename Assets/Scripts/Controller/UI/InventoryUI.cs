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
    #region Variables & Settings

    [Header("### Settings")]
    [SerializeField] private Transform hotbarParent; 
    [SerializeField] private Transform mainInventoryParent; 
    [SerializeField] private Transform equipmentParent; 

    [Header("### UI Panels")]
    [SerializeField] private GameObject[] alwaysOnObjects; 
    [SerializeField] private GameObject[] toggleObjects;   

    [Header("### Animation Settings")]
    [SerializeField] private float animationDuration = 0.2f; 
    [SerializeField] private float yOffset = -50f;          

    [Header("### Selection Settings")]
    [SerializeField] private RectTransform selectionIndicator;
    [SerializeField] private float selectionSmoothSpeed = 15f;
    private Vector2 indicatorVelocity;

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
    private InputAction[] hotbarActions;

    [Header("### UI Interaction")]
    [SerializeField] private GraphicRaycaster raycaster;
    [SerializeField] private EventSystem eventSystem;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private List<InventorySlotUI> equipmentSlots = new List<InventorySlotUI>(); 
    private List<CanvasGroup> toggleCanvasGroups = new List<CanvasGroup>();
    private List<RectTransform> toggleRects = new List<RectTransform>();

    private bool isInitialized = false;
    private const int HOTBAR_COUNT = 10; 
    private bool isInventoryOpen = false;
    private bool isAnimating = false;
    private bool shouldSnapSelection = false;

    private AsyncOperationHandle<Sprite> ghostIconHandle;
    private int currentGhostItemID = -2;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (raycaster == null) raycaster = GetComponentInParent<GraphicRaycaster>();
        if (eventSystem == null) eventSystem = FindAnyObjectByType<EventSystem>();

        if (ghostSlotPanel != null)
        {
            if (ghostSlotPanel.TryGetComponent<CanvasGroup>(out var cg))
            {
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            ghostSlotPanel.SetActive(false);
        }

        if (alwaysOnObjects != null) foreach (var obj in alwaysOnObjects) if (obj != null) obj.SetActive(true);

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
                
                if (obj.transform is RectTransform initialRt) initialRt.anchoredPosition = new Vector2(initialRt.anchoredPosition.x, yOffset);
            }
        }

        InitializeInputActions();
    }

    private void OnEnable()
    {
        if (inventoryAction != null) { inventoryAction.performed += OnInventoryToggle; inventoryAction.Enable(); }
        if (interact00Action != null) { interact00Action.performed += OnInteract00Performed; interact00Action.Enable(); }
        if (interact01Action != null) { interact01Action.performed += OnInteract01Performed; interact01Action.Enable(); }

        if (hotbarActions != null)
        {
            foreach (var action in hotbarActions)
            {
                if (action != null) { action.performed += OnHotbarKeyPressed; action.Enable(); }
            }
        }

        modifierAction?.Enable();
        pointAction?.Enable();
    }

    private void OnDisable()
    {
        if (inventoryAction != null) { inventoryAction.performed -= OnInventoryToggle; inventoryAction.Disable(); }
        if (interact00Action != null) { interact00Action.performed -= OnInteract00Performed; interact00Action.Disable(); }
        if (interact01Action != null) { interact01Action.performed -= OnInteract01Performed; interact01Action.Disable(); }

        if (hotbarActions != null)
        {
            foreach (var action in hotbarActions)
            {
                if (action != null) { action.performed -= OnHotbarKeyPressed; action.Disable(); }
            }
        }

        modifierAction?.Disable();
        pointAction?.Disable();
    }

    private void Start()
    {
        if (gameObject.activeInHierarchy) StartCoroutine(InitializeRoutine());
    }

    private void Update()
    {
        if (!isInitialized) return;
        UpdateGhostUI(); 
        UpdateSelectionIndicator();
    }

    private void OnDestroy()
    {
        if (PlayerController.Local != null && PlayerController.Local.Data != null)
            PlayerController.Local.Data.inventory.OnInventoryChanged -= OnInventoryDataChanged;
        ReleaseGhostIcon();
    }

    #endregion

    #region Input Actions & Callbacks

    private void InitializeInputActions()
    {
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

                hotbarActions = new InputAction[10];
                for (int i = 0; i < 10; i++)
                {
                    string actionName = $"Hotbar{(i == 9 ? 0 : i + 1)}";
                    hotbarActions[i] = playerMap.FindAction(actionName);
                }
            }
        }
    }

    private void OnInventoryToggle(InputAction.CallbackContext ctx) => ToggleInventory();
    private void OnInteract00Performed(InputAction.CallbackContext ctx) { if (isInventoryOpen) HandleInputInteraction(0); }
    private void OnInteract01Performed(InputAction.CallbackContext ctx) { if (isInventoryOpen) HandleInputInteraction(1); }
    private void OnHotbarKeyPressed(InputAction.CallbackContext ctx) => shouldSnapSelection = true;

    #endregion

    #region Initialization & Core Logic

    private IEnumerator InitializeRoutine()
    {
        while (PlayerController.Local == null || PlayerController.Local.Data == null) yield return null;
        InitializeUI();

        PlayerController.Local.Data.inventory.OnInventoryChanged += OnInventoryDataChanged;
    }

    private void InitializeUI()
    {
        if (isInitialized) return;
        uiSlots.Clear();
        equipmentSlots.Clear();
        CollectSlotsFromParent(hotbarParent, uiSlots, false);
        CollectSlotsFromParent(mainInventoryParent, uiSlots, false);
        CollectSlotsFromParent(equipmentParent, equipmentSlots, true);
        isInitialized = true;
        
        RefreshUI();
        RefreshEquipmentUI();
    }

    private void CollectSlotsFromParent(Transform parent, List<InventorySlotUI> list, bool isEquip)
    {
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).TryGetComponent<InventorySlotUI>(out var slotUI))
            {
                int index = list.Count;
                list.Add(slotUI);
                slotUI.Init(this, index); 
                if (!isEquip) slotUI.gameObject.SetActive(index < HOTBAR_COUNT);
            }
        }
    }

    private void OnInventoryDataChanged(int index, PlayerInventorySlotData data)
    {
        if (index >= 0 && index < uiSlots.Count) uiSlots[index].UpdateSlot(data);
    }

    #endregion

    #region UI Updates (Refresh/Ghost/Indicator)

    public void RefreshUI(int count = -1)
    {
        if (PlayerController.Local == null || PlayerController.Local.Data == null || !isInitialized) return;
        var inventory = PlayerController.Local.Data.inventory;
        int loopCount = (count == -1) ? uiSlots.Count : Mathf.Min(count, uiSlots.Count);
        for (int i = 0; i < loopCount; i++) if (i < inventory.slots.Length) uiSlots[i].UpdateSlot(inventory.GetSlot(i));
    }

    public void RefreshEquipmentUI()
    {
        if (PlayerController.Local == null || PlayerController.Local.Data == null || !isInitialized) return;
        var equipment = PlayerController.Local.Data.equipment;
        
        for (int i = 0; i < equipmentSlots.Count; i++)
        {
            ItemType type = equipmentSlots[i].TargetType;
            int typeID = equipment.GetEquipment(type);
            int itemID = ItemDataManager.Instance.FindItemIDByType(type, typeID);
            equipmentSlots[i].UpdateSlot(new PlayerInventorySlotData(itemID, itemID >= 0 ? 1 : 0));
        }
    }

    private void UpdateGhostUI()
    {
        if (PlayerController.Local == null) return;
        
        var ghostData = PlayerController.Local.GhostItem;

        if (ghostData.IsEmpty) 
        { 
            if (ghostSlotPanel != null && ghostSlotPanel.activeSelf) ghostSlotPanel.SetActive(false); 
            currentGhostItemID = -2;
            return; 
        }
        
        if (ghostSlotPanel != null)
        {
            if (pointAction != null) ghostSlotPanel.transform.position = pointAction.ReadValue<Vector2>();
            if (!ghostSlotPanel.activeSelf) ghostSlotPanel.SetActive(true);
            
            if (ghostData.itemID != currentGhostItemID)
            {
                currentGhostItemID = ghostData.itemID;
                Sprite icon = ItemDataManager.Instance.GetItemIcon(currentGhostItemID);
                if (ghostIcon != null) { ghostIcon.sprite = icon; ghostIcon.enabled = (icon != null); }
            }
            if (ghostStackText != null) ghostStackText.text = ghostData.stackCount > 1 ? ghostData.stackCount.ToString() : "";
        }
    }

    private void UpdateSelectionIndicator()
    {
        if (selectionIndicator == null || PlayerController.Local == null) return;

        int selectedIdx = PlayerController.Local.SelectedHotbarIndex;
        if (selectedIdx < 0 || selectedIdx >= uiSlots.Count) return;

        RectTransform targetSlotRect = uiSlots[selectedIdx].GetComponent<RectTransform>();
        if (targetSlotRect != null)
        {
            Vector2 targetPos = targetSlotRect.anchoredPosition;

            if (shouldSnapSelection)
            {
                selectionIndicator.anchoredPosition = targetPos;
                indicatorVelocity = Vector2.zero;
                shouldSnapSelection = false; 
            }
            else
            {
                selectionIndicator.anchoredPosition = Vector2.SmoothDamp(selectionIndicator.anchoredPosition, targetPos, ref indicatorVelocity, 1f / selectionSmoothSpeed);
            }
        }
    }

    #endregion

    #region Interaction Logic

    private void HandleInputInteraction(int buttonIndex)
    {
        if (raycaster == null || pointAction == null) return;

        PointerEventData eventData = new PointerEventData(eventSystem);
        eventData.position = pointAction.ReadValue<Vector2>();

        List<RaycastResult> results = new List<RaycastResult>();
        raycaster.Raycast(eventData, results);

        bool hitSlot = false;
        foreach (var result in results)
        {
            if (result.gameObject.TryGetComponent<InventorySlotUI>(out var slotUI))
            {
                bool isEquipSlot = equipmentSlots.Contains(slotUI);
                if (isEquipSlot) OnEquipmentSlotClicked(equipmentSlots.IndexOf(slotUI), buttonIndex);
                else OnSlotClicked(slotUI.SlotIndex, buttonIndex);
                
                hitSlot = true;
                break;
            }
        }

        if (!hitSlot && PlayerController.Local != null && !PlayerController.Local.GhostItem.IsEmpty) 
        {
            RequestDropItem();
        }
    }

    public void OnSlotClicked(int index, int buttonIndex)
    {
        if (!isInventoryOpen || PlayerController.Local == null) return;
        
        bool isModifierPressed = modifierAction != null && modifierAction.IsPressed();
        PlayerController.Local.InteractSlotServerRpc(index, buttonIndex, isModifierPressed);
    }

    public void OnEquipmentSlotClicked(int index, int buttonIndex)
    {
        if (!isInventoryOpen || PlayerController.Local == null || index < 0 || index >= equipmentSlots.Count) return;

        bool isModifierPressed = modifierAction != null && modifierAction.IsPressed();
        PlayerController.Local.InteractEquipmentSlotServerRpc(equipmentSlots[index].TargetType, buttonIndex, isModifierPressed);
    }

    private void RequestDropItem()
    {
        if (PlayerController.Local == null) return;
        
        var ghostData = PlayerController.Local.GhostItem;
        if (ghostData.IsEmpty) return;

        Debug.Log($"[InventoryUI-Client] Requesting Drop: ItemID {ghostData.itemID}, Count {ghostData.stackCount}");
        
        float lookDir = PlayerController.Local.IsFlipped ? -1f : 1f;
        PlayerController.Local.DropItemServerRpc(ghostData.itemID, ghostData.stackCount, lookDir);
    }

    #endregion

    #region Animation & Visibility

    public void ToggleInventory()
    {
        if (isAnimating) return;
        isInventoryOpen = !isInventoryOpen;
        
        if (!isInventoryOpen && PlayerController.Local != null && !PlayerController.Local.GhostItem.IsEmpty) 
            RequestDropItem();
            
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

    private void UpdateSlotsVisibility(bool showAll)
    {
        for (int i = 0; i < uiSlots.Count; i++) if (i >= HOTBAR_COUNT) uiSlots[i].gameObject.SetActive(showAll);
    }

    #endregion

    #region Helpers

    private void ReleaseGhostIcon() { if (ghostIconHandle.IsValid()) Addressables.Release(ghostIconHandle); }

    #endregion
}
