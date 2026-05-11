using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 클라이언트 예측 기반의 채굴 시스템을 담당하는 컴포넌트입니다.
/// 여러 블록의 손상 상태를 독립적으로 기억하며, 각 블록은 마지막 타격 후 5초 뒤에 회복됩니다.
/// </summary>
public class PlayerMining : NetworkBehaviour
{
    private PlayerController controller;

    // --- Client Side States (Prediction) ---
    // 좌표별 (누적 데미지) 저장
    private Dictionary<Vector2Int, int> localDamagedBlocks = new Dictionary<Vector2Int, int>();
    // 좌표별 (마지막 타격 시간) 저장
    private Dictionary<Vector2Int, float> localLastHitTimes = new Dictionary<Vector2Int, float>();
    
    private bool isMining = false;
    private PickaxeProperty currentToolStats;

    private const float RESET_TIMEOUT = 5.0f; // 5초 동안 안 건드리면 해당 블록 리셋
    private float cleanupTimer = 0f;

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 1초마다 방치된 블록들 회복 체크 (최적화)
        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= 1.0f)
        {
            cleanupTimer = 0f;
            CheckBlockRecovery();
        }
    }

    /// <summary>
    /// PickaxeProperty.OnUseClient에서 곡괭이를 휘두를 때마다 1회 호출됩니다.
    /// </summary>
    public void ProcessMiningClient(PickaxeProperty stats, UseContext context)
    {
        if (!IsOwner) return;

        Vector2 mousePos = context.MouseWorldPos;
        Vector2Int targetPos = new Vector2Int(Mathf.FloorToInt(mousePos.x), Mathf.FloorToInt(mousePos.y));

        isMining = true;
        currentToolStats = stats;

        // 즉시 데미지(히트) 적용
        ApplyMiningHit(targetPos, stats);
    }

    public void StopMiningClient()
    {
        if (!IsOwner) return;
        isMining = false;
    }

    #region Client Local Logic

    private void ApplyMiningHit(Vector2Int pos, PickaxeProperty stats)
    {
        // 1. 블록 데이터 확인
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        
        // 2. 강도 체크 (로컬)
        if (stats.hardness < blockStats.hardness) return;

        // 3. 사거리 체크 (로컬)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > stats.rangeWidth || diffY > stats.rangeHeight) return;

        // 4. 데미지 누적
        if (!localDamagedBlocks.ContainsKey(pos)) localDamagedBlocks[pos] = 0;
        
        localDamagedBlocks[pos] += stats.power;
        localLastHitTimes[pos] = Time.time; // 타격 시간 갱신

        // 5. 비주얼 업데이트 (균열)
        float crackRatio = Mathf.Clamp01((float)localDamagedBlocks[pos] / blockStats.maxHealth);
        UpdateMiningVisuals(pos, crackRatio);

        // 6. 파괴 판정
        if (localDamagedBlocks[pos] >= blockStats.maxHealth)
        {
            CompleteMining(pos, stats);
        }
    }

    private void CompleteMining(Vector2Int pos, PickaxeProperty stats)
    {
        // 서버에 승인 요청
        RequestCompleteMiningServerRpc(pos, stats.power, stats.hardness);
        
        // 로컬 데이터 즉시 제거
        RemoveBlockData(pos);
    }

    private void CheckBlockRecovery()
    {
        if (localLastHitTimes.Count == 0) return;

        float currentTime = Time.time;
        // ToList()를 사용하여 순회 중 삭제 에러 방지
        var expiredBlocks = localLastHitTimes
            .Where(kvp => currentTime - kvp.Value > RESET_TIMEOUT)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var pos in expiredBlocks)
        {
            UpdateMiningVisuals(pos, 0); // 균열 제거
            RemoveBlockData(pos);
            // Debug.Log($"[Mining] Block at {pos} has recovered.");
        }
    }

    private void RemoveBlockData(Vector2Int pos)
    {
        localDamagedBlocks.Remove(pos);
        localLastHitTimes.Remove(pos);
    }

    private void UpdateMiningVisuals(Vector2Int pos, float ratio)
    {
        // TODO: MapManager나 시각 효과 매니저를 통해 해당 좌표의 균열 스프라이트 강도 조절
        // MapManager.Instance.SetBlockCrack(pos.x, pos.y, ratio);
    }

    #endregion

    #region Server Validation

    [Rpc(SendTo.Server)]
    private void RequestCompleteMiningServerRpc(Vector2Int pos, int toolPower, int toolHardness)
    {
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        if (toolHardness < blockStats.hardness) return;

        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > 15f || diffY > 15f) return; 

        MapManager.Instance.SetBlock(pos.x, pos.y, -1);
        SpawnDroppedBlock(block.id, pos.x, pos.y, controller);
    }

    private void SpawnDroppedBlock(int blockID, int x, int y, PlayerController player)
    {
        Vector3 spawnPos = new Vector3(x + 0.5f, y + 0.5f, 0);
        var itemDropPrefab = player.GetComponent<PlayerInteraction>().GetItemDropPrefab();
        if (itemDropPrefab == null) return;

        var netObj = NetworkObjectPoolManager.Instance.Spawn(itemDropPrefab, spawnPos, Quaternion.identity);
        if (netObj != null)
        {
            ItemController item = netObj.GetComponent<ItemController>();
            item.itemID.Value = blockID;
            item.stackCount.Value = 1;
            item.SetTargetPlayer(player);
        }
    }

    #endregion
}
