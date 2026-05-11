using UnityEngine;

public class PlayerMining : MonoBehaviour
{
    private PlayerController controller;

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    public void PerformPickaxe(PickaxeProperty stats, UseContext context)
    {
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;

        Vector2 mousePos = context.MouseWorldPos;
        int wx = Mathf.FloorToInt(mousePos.x);
        int wy = Mathf.FloorToInt(mousePos.y);

        Debug.Log($"[Mining] PerformPickaxe called at ({wx}, {wy}). Pickaxe Power: {stats.power}, Hardness: {stats.hardness}");

        // 1. 사거리 체크 (사각형 범위 체크)
        Vector2 playerPos = (Vector2)transform.position;
        float diffX = Mathf.Abs(wx + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(wy + 0.5f - playerPos.y);

        if (diffX > stats.rangeWidth || diffY > stats.rangeHeight)
        {
            Debug.Log($"[Mining] 사거리가 너무 멉니다! (X차이: {diffX}/{stats.rangeWidth}, Y차이: {diffY}/{stats.rangeHeight})");
            return;
        }

        // 2. 블록 존재 확인
        var block = MapManager.Instance.GetBlock(wx, wy);
        if (!block.isActive)
        {
            Debug.Log($"[Mining] 해당 위치에 활성화된 블록이 없습니다.");
            return;
        }

        // 3. 강도(Hardness) 체크
        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        if (stats.hardness < blockStats.hardness)
        {
            Debug.Log($"[Mining] 강도가 부족합니다! (필요: {blockStats.hardness}, 보유: {stats.hardness})");
            return;
        }

        // 4. 데미지 적용
        float currentHealth = MapManager.Instance.GetBlockHealth(wx, wy);
        Debug.Log($"[Mining] 블록 데미지 적용 전 체력: {currentHealth}");
        MapManager.Instance.DamageBlock(wx, wy, stats.power);

        // 5. 파괴 확인 및 아이템 드랍
        float newHealth = MapManager.Instance.GetBlockHealth(wx, wy);
        Debug.Log($"[Mining] 블록 데미지 적용 후 체력: {newHealth}");
        if (currentHealth > 0 && newHealth <= 0)
        {
            Debug.Log($"[Mining] 블록 파괴 성공! 아이템 드랍: {block.id}");
            SpawnDroppedBlock(block.id, wx, wy, context.Player);
        }
    }

    private void SpawnDroppedBlock(int blockID, int x, int y, PlayerController player)
    {
        // 블록 위치 중앙에서 스폰
        Vector3 spawnPos = new Vector3(x + 0.5f, y + 0.5f, 0);
        
        // 아이템 스폰 (기존의 PlayerInteraction.HandleDropItem 로직 활용 가능)
        // 여기서는 직접 NetworkObjectPoolManager 호출
        var itemDropPrefab = player.GetComponent<PlayerInteraction>().GetItemDropPrefab();
        if (itemDropPrefab == null) return;

        var netObj = NetworkObjectPoolManager.Instance.Spawn(itemDropPrefab, spawnPos, Quaternion.identity);
        if (netObj != null)
        {
            ItemController item = netObj.GetComponent<ItemController>();
            item.itemID.Value = blockID;
            item.stackCount.Value = 1;
            
            // [Key] 즉시 흡수 설정
            item.SetTargetPlayer(player);
        }
    }
}
