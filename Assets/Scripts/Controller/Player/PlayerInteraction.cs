using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 플레이어의 아이템 사용, 블록 설치 및 버리기 상호작용을 담당하는 컴포넌트입니다.
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    private PlayerController controller;

    [Header("### Interaction Settings")]
    [SerializeField] private float interactRange = 6f;
    [SerializeField] private GameObject itemDropPrefab;
    [SerializeField] private SpriteRenderer previewRenderer;

    // --- New Action Cache ---
    private IUsable currentLeftAction = new NullUsable();
    private IUsable currentRightAction = new NullUsable();

    public static float DropThrowForce = 4f;
    public static float DropUpwardForce = 6f;

    public void Init(PlayerController ctrl, GameObject dropPrefab)
    {
        controller = ctrl;
        itemDropPrefab = dropPrefab;

        if (previewRenderer == null)
        {
            GameObject go = new GameObject("PlacementPreview");
            go.transform.SetParent(transform);
            previewRenderer = go.AddComponent<SpriteRenderer>();
            previewRenderer.sortingOrder = 100;
            go.SetActive(false);
        }
    }

    public void SetCurrentItem(ItemData data)
    {
        if (data == null)
        {
            currentLeftAction = new NullUsable();
            currentRightAction = new NullUsable();
        }
        else
        {
            currentLeftAction = data.LeftAction;
            currentRightAction = data.RightAction;
        }
    }

    public void UpdatePlacementPreview(int itemID, Vector2 mouseWorldPos, bool isPointerOverUI)
    {
        if (previewRenderer == null) return;

        ItemData itemData = ItemDataManager.Instance.GetItem(itemID);
        if (itemData == null || itemData.type != ItemType.Block || isPointerOverUI)
        {
            if (previewRenderer.gameObject.activeSelf) previewRenderer.gameObject.SetActive(false);
            return;
        }

        int wx = Mathf.FloorToInt(mouseWorldPos.x);
        int wy = Mathf.FloorToInt(mouseWorldPos.y);

        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(wx + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(wy + 0.5f - playerPos.y);

        if (diffX > 8.5f || diffY > 6.5f)
        {
            if (previewRenderer.gameObject.activeSelf) previewRenderer.gameObject.SetActive(false);
            return;
        }

        if (!previewRenderer.gameObject.activeSelf) previewRenderer.gameObject.SetActive(true);
        previewRenderer.transform.position = new Vector3(wx + 0.5f, wy + 0.5f, 0);
    }

    public void UseItem(int buttonIndex, int selectedHotbarIndex, Vector2 mouseWorldPos)
    {
        if (controller.Data == null || controller.Data.inventory == null) return;
        
        PlayerInventorySlotData selectedSlot = controller.Data.inventory.GetSlot(selectedHotbarIndex);
        if (selectedSlot.IsEmpty) return;

        ItemData itemData = ItemDataManager.Instance.GetItem(selectedSlot.itemID);
        if (itemData == null) return;

        // 장비류 특수 처리 (우클릭 시 즉시 장착) - 이건 시스템 성격상 유지하거나 Property로 뺄 수 있음
        if (buttonIndex == 1 && IsEquipmentType(itemData.type))
        {
            controller.QuickEquipRpc(selectedHotbarIndex);
            return;
        }

        // Context 생성
        UseContext context = new UseContext
        {
            Player = controller,
            MouseWorldPos = mouseWorldPos,
            HotbarIndex = selectedHotbarIndex,
            ItemID = selectedSlot.itemID,
            ButtonIndex = buttonIndex
        };

        // 인터페이스 기반 실행 (If/Switch 제거)
        if (buttonIndex == 0)
        {
            currentLeftAction.OnUseClient(context);
            controller.ExecuteUsableRpc(0, selectedHotbarIndex, mouseWorldPos);
        }
        else if (buttonIndex == 1)
        {
            currentRightAction.OnUseClient(context);
            controller.ExecuteUsableRpc(1, selectedHotbarIndex, mouseWorldPos);
        }
    }

    private bool IsEquipmentType(ItemType type)
    {
        return type == ItemType.Helmet || type == ItemType.Chestplate || 
               type == ItemType.Leggings || type == ItemType.Boots || 
               type == ItemType.Jetbag;
    }

    // --- Server Side Helpers (Used by RPCs) ---
    
    public void ExecuteServerAction(int buttonIndex, UseContext context)
    {
        // 서버에서도 해당 아이템의 Action을 찾아 실행
        ItemData itemData = ItemDataManager.Instance.GetItem(context.ItemID);
        if (itemData == null) return;

        IUsable action = (buttonIndex == 0) ? itemData.LeftAction : itemData.RightAction;
        action.OnUseServer(context);

        // [Legacy] 블록 설치는 아직 전용 로직이 필요함 (좌표 검증 등)
        if (itemData.type == ItemType.Block)
        {
            TryPlaceBlockOnServer(context.HotbarIndex, context.ItemID, context.MouseWorldPos);
        }
    }

    private void TryPlaceBlockOnServer(int hotbarIndex, int itemID, Vector2 mouseWorldPos)
    {
        int wx = Mathf.FloorToInt(mouseWorldPos.x);
        int wy = Mathf.FloorToInt(mouseWorldPos.y);

        Vector2 playerPos = transform.position;
        if (Mathf.Abs(wx + 0.5f - playerPos.x) > 8.5f || Mathf.Abs(wy + 0.5f - playerPos.y) > 6.5f) return;
        if (MapManager.Instance.IsBlockActive(wx, wy)) return;

        bool hasNeighbor = MapManager.Instance.IsBlockActive(wx + 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx - 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx, wy + 1) ||
                           MapManager.Instance.IsBlockActive(wx, wy - 1);

        if (!hasNeighbor) return;

        Vector2 checkPos = new Vector2(wx + 0.5f, wy + 0.5f);
        if (Physics2D.OverlapBox(checkPos, Vector2.one * 0.95f, 0f, LayerMask.GetMask("Player")) != null) return;

        if (controller.Data.inventory.RemoveItemFromSlot(hotbarIndex, 1))
        {
            MapManager.Instance.SetBlock(wx, wy, itemID);
            controller.SyncInventoryToNetwork();
        }
    }

    public void HandleDropItem(int id, int count, float lookDir)
    {
        if (itemDropPrefab == null) return;

        Vector3 spawnPos = transform.position + new Vector3(lookDir * 0.8f, 0.5f, 0);
        var netObj = NetworkObjectPoolManager.Instance.Spawn(itemDropPrefab, spawnPos, Quaternion.identity);
        if (netObj == null) return;

        ItemController item = netObj.GetComponent<ItemController>();
        item.itemID.Value = id;
        item.stackCount.Value = count;

        Vector2 throwForce = new Vector2(lookDir * DropThrowForce, DropUpwardForce);
        if (netObj.TryGetComponent<Rigidbody2D>(out var rb))
        {
            rb.linearVelocity = throwForce;
        }
        item.SetDropCooldown(false);
    }
}
