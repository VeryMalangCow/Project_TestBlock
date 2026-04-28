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

    public static float DropThrowForce = 4f;
    public static float DropUpwardForce = 6f;

    public void Init(PlayerController ctrl, GameObject dropPrefab)
    {
        controller = ctrl;
        itemDropPrefab = dropPrefab;
    }

    public void UseItem(int buttonIndex, int selectedHotbarIndex, Vector2 mouseWorldPos)
    {
        if (controller.Data == null || controller.Data.inventory == null) return;
        
        PlayerInventorySlotData selectedSlot = controller.Data.inventory.GetSlot(selectedHotbarIndex);
        if (selectedSlot.IsEmpty) return;

        ItemData itemData = ItemDataManager.Instance.GetItem(selectedSlot.itemID);
        if (itemData == null) return;

        // Block Placement Logic (Left Click Only)
        if (itemData.type == ItemType.Block && buttonIndex == 0)
        {
            TryPlaceBlock(selectedHotbarIndex, selectedSlot.itemID, mouseWorldPos);
        }
    }

    private void TryPlaceBlock(int hotbarIndex, int itemID, Vector2 mouseWorldPos)
    {
        int wx = Mathf.FloorToInt(mouseWorldPos.x);
        int wy = Mathf.FloorToInt(mouseWorldPos.y);

        // 1. Distance Check (Horizontal 8, Vertical 6)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(mouseWorldPos.x - playerPos.x);
        float diffY = Mathf.Abs(mouseWorldPos.y - playerPos.y);

        if (diffX > 8f || diffY > 6f) return;

        // 2. Adjacency & Empty Check
        if (MapManager.Instance.IsBlockActive(wx, wy)) return;

        bool hasNeighbor = MapManager.Instance.IsBlockActive(wx + 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx - 1, wy) ||
                           MapManager.Instance.IsBlockActive(wx, wy + 1) ||
                           MapManager.Instance.IsBlockActive(wx, wy - 1);

        if (!hasNeighbor) return;

        // 3. Player Overlap Check (0.95x0.95 area to allow placement under feet)
        Vector2 checkPos = new Vector2(wx + 0.5f, wy + 0.5f);
        Collider2D playerCol = Physics2D.OverlapBox(checkPos, Vector2.one * 0.95f, 0f, LayerMask.GetMask("Player"));
        if (playerCol != null) return;

        // 4. Request Server to place block and consume item
        controller.PlaceBlockRpc(wx, wy, itemID, hotbarIndex);
    }

    public void HandleDropItem(int id, int count, float lookDir)
    {
        if (itemDropPrefab == null) 
        {
            Debug.LogError("[Interaction] itemDropPrefab is NULL!");
            return;
        }

        Debug.Log($"[Interaction-Server] Requesting Spawn for ItemID: {id}. IsServer: {Unity.Netcode.NetworkManager.Singleton.IsServer}");

        Vector3 spawnPos = transform.position + new Vector3(lookDir * 0.8f, 0.5f, 0);
        
        // 매니저 호출 전후 로그
        var netObj = NetworkObjectPoolManager.Instance.Spawn(itemDropPrefab, spawnPos, Quaternion.identity);
        
        if (netObj == null)
        {
            Debug.LogError("[Interaction-Server] Spawn failed! Manager returned NULL.");
            return;
        }

        Debug.Log($"[Interaction-Server] Spawn SUCCESS. NetObj: {netObj.name}, Hash: {netObj.PrefabIdHash}");
        
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
