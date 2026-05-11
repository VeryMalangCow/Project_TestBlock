using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// 클라이언트 예측 기반의 채굴 시스템을 담당하는 컴포넌트입니다.
/// 모든 진행도와 회복 타이머는 클라이언트 로컬에서 관리됩니다.
/// </summary>
public class PlayerMining : NetworkBehaviour
{
    private PlayerController controller;

    // --- Client Side States (Prediction) ---
    private Vector2Int currentTargetPos = new Vector2Int(-1, -1);
    private int currentProgress = 0;
    private int requiredProgress = 0;
    private bool isMining = false;
    private float lastHitTime = 0f;
    private PickaxeProperty currentToolStats;

    private const float RESET_TIMEOUT = 5.0f; // 5초 동안 안 건드리면 로컬 리셋

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 1. 회복 타이머 체크 (클라이언트 로컬)
        if (currentTargetPos.x != -1 && Time.time - lastHitTime > RESET_TIMEOUT)
        {
            ResetLocalMining();
        }

        // 2. 버튼을 뗐을 때의 상태 관리
        if (!isMining && currentTargetPos.x != -1)
        {
            // 타겟은 유지하되(타이머를 위해), 채굴 중인 상태만 해제
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

        // 1. 새로운 블록을 타격하는 경우 초기화
        if (targetPos != currentTargetPos)
        {
            StartNewMining(targetPos, stats);
        }

        isMining = true;
        currentToolStats = stats;
        lastHitTime = Time.time; // 타격 시간 갱신

        // 2. 즉시 데미지(히트) 적용
        ApplyMiningHit(stats);
    }

    public void StopMiningClient()
    {
        if (!IsOwner) return;
        isMining = false;
    }

    #region Client Local Logic

    private void StartNewMining(Vector2Int pos, PickaxeProperty stats)
    {
        // 이전 블록 균열 제거
        if (currentTargetPos.x != -1) UpdateMiningVisuals(currentTargetPos, 0);

        currentTargetPos = pos;
        currentProgress = 0;
        
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive)
        {
            currentTargetPos = new Vector2Int(-1, -1);
            return;
        }

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        
        // 강도 체크 (로컬)
        if (stats.hardness < blockStats.hardness)
        {
            requiredProgress = int.MaxValue; 
            return;
        }

        requiredProgress = blockStats.maxHealth;
    }

    private void ApplyMiningHit(PickaxeProperty stats)
    {
        if (currentTargetPos.x == -1 || requiredProgress == int.MaxValue) return;

        // 사거리 체크 (로컬)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(currentTargetPos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(currentTargetPos.y + 0.5f - playerPos.y);

        if (diffX > stats.rangeWidth || diffY > stats.rangeHeight) return;

        // 데미지 누적
        currentProgress += stats.power;

        // 비주얼 업데이트 (균열)
        float crackRatio = Mathf.Clamp01((float)currentProgress / requiredProgress);
        UpdateMiningVisuals(currentTargetPos, crackRatio);

        // 파괴 판정
        if (currentProgress >= requiredProgress)
        {
            CompleteMining();
        }
    }

    private void CompleteMining()
    {
        // 서버에 승인 요청 (최소한의 검증 정보만 전송)
        RequestCompleteMiningServerRpc(currentTargetPos, currentToolStats.power, currentToolStats.hardness);
        ResetLocalMining();
    }

    private void ResetLocalMining()
    {
        if (currentTargetPos.x != -1) UpdateMiningVisuals(currentTargetPos, 0);
        currentTargetPos = new Vector2Int(-1, -1);
        currentProgress = 0;
        requiredProgress = 0;
        isMining = false;
    }

    private void UpdateMiningVisuals(Vector2Int pos, float ratio)
    {
        // TODO: MapManager.Instance.SetBlockCrack(pos.x, pos.y, ratio);
    }

    #endregion

    #region Server Validation

    [Rpc(SendTo.Server)]
    private void RequestCompleteMiningServerRpc(Vector2Int pos, int toolPower, int toolHardness)
    {
        // 1. 블록 존재 및 데이터 확인
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        var blockStats = MapManager.Instance.GetBlockStats(block.id);

        // 2. 성능 검증 (강도 체크)
        if (toolHardness < blockStats.hardness) return;

        // 3. 거리 검증 (서버 기준 오차 범위 허용)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > 15f || diffY > 15f) return; 

        // 4. 최종 승인: 월드 업데이트 및 아이템 생성
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
