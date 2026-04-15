using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    [Header("### Settings")]
    [SerializeField] private Transform hotbarParent; // 0~9번 슬롯 부모
    [SerializeField] private Transform mainInventoryParent; // 나머지 슬롯 부모
    
    [Header("### UI Panels")]
    [SerializeField] private GameObject[] alwaysOnObjects; // 항상 켜져 있을 오브젝트들
    [SerializeField] private GameObject[] toggleObjects;   // 가방 열 때만 켜질 오브젝트들

    [Header("### Animation Settings")]
    [SerializeField] private float animationDuration = 0.2f; // 애니메이션 속도
    [SerializeField] private float yOffset = -50f;          // 꺼져있을 때의 Y 오프셋 값

    [Header("### Input")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction inventoryAction;

    private List<InventorySlotUI> uiSlots = new List<InventorySlotUI>();
    private List<CanvasGroup> toggleCanvasGroups = new List<CanvasGroup>();
    private List<RectTransform> toggleRects = new List<RectTransform>();

    private bool isInitialized = false;
    private const int HOTBAR_COUNT = 10; // 항상 보여줄 슬롯 수
    private bool isInventoryOpen = false;
    private bool isAnimating = false;

    private void Awake()
    {
        // 항상 켜져 있어야 하는 오브젝트들 활성화
        if (alwaysOnObjects != null)
        {
            foreach (var obj in alwaysOnObjects)
            {
                if (obj != null) obj.SetActive(true);
            }
        }

        // 토글 대상 오브젝트 초기화 (CanvasGroup, RectTransform 캐싱 및 초기 상태 설정)
        if (toggleObjects != null)
        {
            foreach (var obj in toggleObjects)
            {
                if (obj == null) continue;

                // CanvasGroup이 없으면 추가
                if (!obj.TryGetComponent<CanvasGroup>(out var cg))
                {
                    cg = obj.AddComponent<CanvasGroup>();
                }
                toggleCanvasGroups.Add(cg);

                // RectTransform 캐싱
                if (obj.transform is RectTransform rt)
                {
                    toggleRects.Add(rt);
                }

                // 초기 상태: 꺼짐 (Alpha 0, Y 오프셋)
                obj.SetActive(true); // 코루틴 제어를 위해 액티브는 켜두고 Alpha로 조절
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
        if (isAnimating) return; // 애니메이션 중에는 입력 무시

        isInventoryOpen = !isInventoryOpen;
        StopAllCoroutines(); // 기존 애니메이션 중단 (필요 시)
        StartCoroutine(ToggleAnimationRoutine(isInventoryOpen));
    }

    private IEnumerator ToggleAnimationRoutine(bool isOpen)
    {
        isAnimating = true;

        float elapsedTime = 0f;
        
        // 시작 값 설정
        float startAlpha = isOpen ? 0f : 1f;
        float endAlpha = isOpen ? 1f : 0f;
        float startY = isOpen ? yOffset : 0f;
        float endY = isOpen ? 0f : yOffset;

        // 슬롯 가시성 업데이트 (애니메이션 시작 시 즉시 처리하거나 필요에 따라 조절)
        UpdateSlotsVisibility(isOpen);

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            
            // 부드러운 보간 (Ease Out)
            float curveT = 1f - Mathf.Pow(1f - t, 3f); 

            float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, curveT);
            float currentY = Mathf.Lerp(startY, endY, curveT);

            for (int i = 0; i < toggleCanvasGroups.Count; i++)
            {
                if (toggleCanvasGroups[i] != null)
                {
                    toggleCanvasGroups[i].alpha = currentAlpha;
                }
                if (i < toggleRects.Count && toggleRects[i] != null)
                {
                    toggleRects[i].anchoredPosition = new Vector2(toggleRects[i].anchoredPosition.x, currentY);
                }
            }

            yield return null;
        }

        // 최종 값 확정 및 상호작용 설정
        for (int i = 0; i < toggleCanvasGroups.Count; i++)
        {
            if (toggleCanvasGroups[i] != null)
            {
                toggleCanvasGroups[i].alpha = endAlpha;
                toggleCanvasGroups[i].interactable = isOpen;
                toggleCanvasGroups[i].blocksRaycasts = isOpen;
            }
            if (i < toggleRects.Count && toggleRects[i] != null)
            {
                toggleRects[i].anchoredPosition = new Vector2(toggleRects[i].anchoredPosition.x, endY);
            }
        }

        isAnimating = false;
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
                child.gameObject.SetActive(currentIndex < HOTBAR_COUNT);
            }
        }
    }

    private void UpdateSlotsVisibility(bool showAll)
    {
        // 애니메이션 도중에도 슬롯의 Active 상태는 적절히 관리되어야 함
        // 여기서는 논리적인 상태만 제어
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
            // 슬롯이 속한 부모가 꺼져있을 수 있으므로 activeInHierarchy 체크는 생략하거나
            // 혹은 상위 CanvasGroup의 alpha를 체크하는 방식으로 갈 수 있음.
            // 여기서는 데이터 업데이트만 수행
            if (i < inventory.slots.Length)
            {
                uiSlots[i].UpdateSlot(inventory.GetSlot(i));
            }
        }
    }
}
