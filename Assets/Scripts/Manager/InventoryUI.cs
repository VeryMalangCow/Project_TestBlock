using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("### Settings")]
    [SerializeField] private Transform hotbarParent; // 0~9번 슬롯 부모
    [SerializeField] private Transform mainInventoryParent; // 나머지 슬롯 부모
    [SerializeField] private GameObject inventoryPanel; // 가방이 열렸을 때 보일 패널

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private bool isInitialized = false;
    private const int HOTBAR_COUNT = 10; // 항상 보여줄 슬롯 수

    private void Awake()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }

        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap != null)
            {
                inventoryAction = playerMap.FindAction("Inventory");
            }
        }
    }

    private void OnEnable() => inventoryAction?.Enable();
    private void OnDisable() => inventoryAction?.Disable();

    private void Start()
    {
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(InitializeRoutine());
        }
    }

    private IEnumerator InitializeRoutine()
    {
        while (PlayerController.Local == null || PlayerController.Local.Data == null)
        {
            yield return null;
        }

        InitializeUI();
    }

    private void Update()
    {
        if (!isInitialized) return;

        if (inventoryAction != null && inventoryAction.WasPressedThisFrame())
        {
            ToggleInventory();
        }

        RefreshUI();
    }

    public void ToggleInventory()
    {
        if (inventoryPanel == null) return;
        bool newState = !inventoryPanel.activeSelf;
        inventoryPanel.SetActive(newState);

        UpdateSlotsVisibility(newState);
    }

    private void InitializeUI()
    {
        if (isInitialized) return;

        uiSlots.Clear();

        // 1. Hotbar 슬롯 수집 (첫 번째 부모)
        CollectSlotsFromParent(hotbarParent);

        // 2. Main Inventory 슬롯 수집 (두 번째 부모)
        CollectSlotsFromParent(mainInventoryParent);

        isInitialized = true;
        Debug.Log($"[InventoryUI] {uiSlots.Count} slots connected from two parents.");
    }

    private void CollectSlotsFromParent(Transform parent)
    {
        if (parent == null) return;

        int childCount = parent.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            InventorySlotUI slotUI = child.GetComponent<InventorySlotUI>();
            
            if (slotUI != null)
            {
                int currentIndex = uiSlots.Count;
                uiSlots.Add(slotUI);
                slotUI.Init(currentIndex);
                
                // 0~9번(핫바)은 항상 활성화, 나머지는 초기 상태에서 비활성화
                // (일반적으로 hotbarParent의 자식들이 먼저 들어가게 됨)
                child.gameObject.SetActive(currentIndex < HOTBAR_COUNT);
            }
        }
    }

    private void UpdateSlotsVisibility(bool showAll)
    {
        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (i >= HOTBAR_COUNT) 
            {
                uiSlots[i].gameObject.SetActive(showAll);
            }
        }
    }

    public void RefreshUI()
    {
        if (PlayerController.Local == null || !isInitialized) return;

        var inventory = PlayerController.Local.Data.inventory;
        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (uiSlots[i].gameObject.activeSelf && i < inventory.slots.Length)
            {
                uiSlots[i].UpdateSlot(inventory.GetSlot(i));
            }
        }
    }
}
