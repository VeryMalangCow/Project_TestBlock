using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("### Settings")]
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private Transform slotParent;
    [SerializeField] private GameObject inventoryPanel; 

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private bool isInitialized = false;

    private void Awake()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }

        // Initialize Input System
        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            if (playerMap != null)
            {
                inventoryAction = playerMap.FindAction("Inventory");
            }
        }
    }

    private void OnEnable()
    {
        inventoryAction?.Enable();
    }

    private void OnDisable()
    {
        inventoryAction?.Disable();
    }

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

        // Check for Inventory Toggle using new Input System
        if (inventoryAction != null && inventoryAction.WasPressedThisFrame())
        {
            ToggleInventory();
        }

        if (inventoryPanel != null && inventoryPanel.activeSelf)
        {
            RefreshUI();
        }
    }

    public void ToggleInventory()
    {
        if (inventoryPanel == null) return;

        bool newState = !inventoryPanel.activeSelf;
        inventoryPanel.SetActive(newState);

        if (newState)
        {
            RefreshUI();
        }
    }

    private void InitializeUI()
    {
        if (isInitialized) return;

        foreach (Transform child in slotParent)
        {
            if (child != null) Destroy(child.gameObject);
        }
        uiSlots.Clear();

        var data = PlayerController.Local.Data;
        if (data == null || data.inventory == null) return;

        int slotCount = data.inventory.slots.Length;
        for (int i = 0; i < slotCount; i++)
        {
            GameObject obj = Instantiate(slotPrefab, slotParent);
            InventorySlotUI slotUI = obj.GetComponent<InventorySlotUI>();
            if (slotUI != null)
            {
                slotUI.Init(i);
                uiSlots.Add(slotUI);
            }
        }

        isInitialized = true;
        Debug.Log($"[InventoryUI] {slotCount} slots initialized successfully.");
    }

    public void RefreshUI()
    {
        if (PlayerController.Local == null || !isInitialized) return;

        var inventory = PlayerController.Local.Data.inventory;
        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (i < inventory.slots.Length)
            {
                uiSlots[i].UpdateSlot(inventory.GetSlot(i));
            }
        }
    }
}
