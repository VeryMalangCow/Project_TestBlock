using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// 클라이언트 예측 기반의 채굴 시스템을 담당하는 컴포넌트입니다.
/// 스윙 즉시 데미지가 들어가는 방식으로 구현되어 반응성이 높습니다.
/// </summary>
public class PlayerMining : NetworkBehaviour
{
    private PlayerController controller;

    // --- Client Side States (Prediction) ---
    private Vector2Int currentTargetPos = new Vector2Int(-1, -1);
    private int currentProgress = 0;
    private int requiredProgress = 0;
    private bool isMining = false;
    private PickaxeProperty currentToolStats;

    // --- Server Side States (Authority) ---
    private Dictionary<Vector2Int, float> serverMiningStartTimes = new Dictionary<Vector2Int, float>();

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 버튼을 떼거나 타겟이 없어졌을 때의 정리 작업
        if (!isMining && currentTargetPos.x != -1)
        {
            ResetLocalMining();
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

        // 1. 타겟이 바뀌었거나 새로 시작하는 경우 초기화
        if (targetPos != currentTargetPos)
        {
            StartNewMining(targetPos, stats);
        }

        isMining = true;
        currentToolStats = stats;

        // 2. 즉시 데미지(히트) 적용
        ApplyMiningHit(stats);
    }

    public void StopMiningClient()
    {
        if (!IsOwner) return;
        isMining = false;
    }

    #region Client Prediction Logic

    private void StartNewMining(Vector2Int pos, PickaxeProperty stats)
    {
        currentTargetPos = pos;
        currentProgress = 0;
        
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive)
        {
            ResetLocalMining();
            return;
        }

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        
        // 강도 체크 (로컬 예측)
        if (stats.hardness < blockStats.hardness)
        {
            requiredProgress = int.MaxValue; 
            return;
        }

        requiredProgress = blockStats.maxHealth;
        
        // 서버에게 채굴 시작 알림 (검증용 타임스탬프 생성)
        NotifyStartMiningServerRpc(pos);
    }

    private void ApplyMiningHit(PickaxeProperty stats)
    {
        if (currentTargetPos.x == -1 || requiredProgress == int.MaxValue) return;

        // 사거리 체크 (로컬)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(currentTargetPos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(currentTargetPos.y + 0.5f - playerPos.y);

        if (diffX > stats.rangeWidth || diffY > stats.rangeHeight)
        {
            Debug.Log($"[Mining] Out of range! {diffX}, {diffY}");
            return;
        }

        // 스윙 즉시 파워만큼 진행도 추가
        currentProgress += stats.power;

        // 시각적 피드백 (균열 상태 업데이트)
        float crackRatio = Mathf.Clamp01((float)currentProgress / requiredProgress);
        UpdateMiningVisuals(currentTargetPos, crackRatio);

        Debug.Log($"[Mining] Hit! Progress: {currentProgress}/{requiredProgress}");

        // 완료 판정
        if (currentProgress >= requiredProgress)
        {
            CompleteMining();
        }
    }

    private void CompleteMining()
    {
        // 서버에 완료 승인 요청
        RequestCompleteMiningServerRpc(currentTargetPos, currentToolStats.power, currentToolStats.hardness, currentToolStats.speed);
        
        // 로컬 상태 즉시 초기화 (다음 타겟을 위해)
        ResetLocalMining();
    }

    private void ResetLocalMining()
    {
        if (currentTargetPos.x != -1)
        {
            UpdateMiningVisuals(currentTargetPos, 0); 
        }
        currentTargetPos = new Vector2Int(-1, -1);
        currentProgress = 0;
        requiredProgress = 0;
        isMining = false;
    }

    private void UpdateMiningVisuals(Vector2Int pos, float ratio)
    {
        // TODO: 블록 균열 이펙트 구현 시 활용
        // MapManager.Instance.SetBlockCrack(pos.x, pos.y, ratio);
    }

    #endregion

    #region Server Authority & Validation

    [Rpc(SendTo.Server)]
    private void NotifyStartMiningServerRpc(Vector2Int pos)
    {
        // 서버에서 해당 플레이어의 채굴 시작 시간 기록
        serverMiningStartTimes[pos] = Time.time;
    }

    [Rpc(SendTo.Server)]
    private void RequestCompleteMiningServerRpc(Vector2Int pos, int toolPower, int toolHardness, float toolSpeed)
    {
        // 1. 블록 존재 확인
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        // 2. 거리 검증 (서버 기준 최대 15칸 오차 허용)
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > 15f || diffY > 15f) return; 

        // 3. 시간 및 성능 검증 (Anti-Cheat)
        if (!serverMiningStartTimes.TryGetValue(pos, out float startTime)) return;

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        if (toolHardness < blockStats.hardness) return;

        // [New] 필요 타격 횟수 기반 최소 소요 시간 계산
        // 1방 컷이면 0초, 2방 컷이면 1회 스윙 간격만큼의 시간이 필요함
        int hitsNeeded = Mathf.CeilToInt(blockStats.maxHealth / (float)toolPower);
        float swingInterval = 1f / toolSpeed;
        float expectedMinTime = (hitsNeeded - 1) * swingInterval;

        float elapsedTime = Time.time - startTime;

        // 너무 빨리 완료 요청이 온 경우 무시 (약간의 네트워크 지연 보정 0.9f)
        if (elapsedTime < expectedMinTime * 0.9f) 
        {
            Debug.LogWarning($"[Anti-Cheat] Mining too fast! Elapsed: {elapsedTime}, Expected Min: {expectedMinTime}");
            return;
        }

        // 4. 최종 승인: 블록 파괴 및 드랍
        MapManager.Instance.SetBlock(pos.x, pos.y, -1);
        SpawnDroppedBlock(block.id, pos.x, pos.y, controller);
        
        serverMiningStartTimes.Remove(pos);
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
