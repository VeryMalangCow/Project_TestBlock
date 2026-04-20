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

    public void UseItem(int buttonIndex, int selectedHotbarIndex, Vector2 screenPos)
    {
        if (controller.Data == null || controller.Data.inventory == null) return;
        
        PlayerInventorySlotData selectedSlot = controller.Data.inventory.GetSlot(selectedHotbarIndex);
        if (selectedSlot.IsEmpty) return;

        // [Logic] 현재는 블록 설치 테스트만 구현됨
        if (selectedSlot.itemID >= 0)
        {
            UpdateBlock(selectedSlot.itemID, screenPos);
        }
    }

    private void UpdateBlock(int id, Vector2 screenPos)
    {
        if (MapManager.Instance == null || Camera.main == null) return;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
        if (Vector2.Distance(transform.position, worldPos) > interactRange) return;
        
        controller.UpdateBlockServerRpc(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y), id);
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
