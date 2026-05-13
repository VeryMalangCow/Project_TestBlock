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
    private struct BlockDamageData
    {
        public int currentDamage;
        public float lastHitTime;

        public BlockDamageData(int damage, float time)
        {
            currentDamage = damage;
            lastHitTime = time;
        }
    }

    // --- Events (Decoupling Visuals) ---
    public delegate void BlockDamageHandler(Vector2Int pos, float crackRatio, BlockMaterial material);
    public event BlockDamageHandler OnBlockDamageDealt;

    private PlayerController controller;

    // --- Client Side States (Prediction) ---
    // 좌표별 손상 데이터 저장 (데미지 + 마지막 타격 시간)
    private Dictionary<Vector2Int, BlockDamageData> localDamagedBlocks = new Dictionary<Vector2Int, BlockDamageData>();
    private List<Vector2Int> keysToRemove = new List<Vector2Int>(); // GC 방지를 위한 재사용 리스트
    
    private bool isMining = false;
    private PickaxeProperty currentToolStats;

    private const float RESET_TIMEOUT = 5.0f; // 5초 동안 안 건드리면 해당 블록 리셋
    private float cleanupTimer = 0f;

    // --- Server Side States ---
    private double lastMiningTime = 0; // 서버측 속도 제한용

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
        if (!localDamagedBlocks.TryGetValue(pos, out BlockDamageData data))
        {
            data = new BlockDamageData(0, Time.time);
        }
        
        data.currentDamage += stats.power;
        data.lastHitTime = Time.time; // 타격 시간 갱신
        localDamagedBlocks[pos] = data;

        // [New] 즉시 로컬 타격 이펙트 재생 (서버 통신 없음)
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayHitFX((Vector2)(Vector2)pos + new Vector2(0.5f, 0.5f), block.id);
        }

        // 5. 비주얼 업데이트 (균열)
        float crackRatio = Mathf.Clamp01((float)data.currentDamage / blockStats.maxHealth);
        UpdateMiningVisuals(pos, crackRatio);

        // 6. 파괴 판정
        if (data.currentDamage >= blockStats.maxHealth)
        {
            CompleteMining(pos, stats);
        }
    }

    private void CompleteMining(Vector2Int pos, PickaxeProperty stats)
    {
        // 서버에 승인 요청
        RequestCompleteMiningServerRpc(pos, stats.power, stats.hardness, stats.speed);
        
        // 로컬 데이터 즉시 제거
        RemoveBlockData(pos);
    }

    private void CheckBlockRecovery()
    {
        if (localDamagedBlocks.Count == 0) return;

        float currentTime = Time.time;
        keysToRemove.Clear();

        foreach (var kvp in localDamagedBlocks)
        {
            if (currentTime - kvp.Value.lastHitTime > RESET_TIMEOUT)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var pos in keysToRemove)
        {
            UpdateMiningVisuals(pos, 0); // 균열 제거
            RemoveBlockData(pos);
        }
    }

    private void RemoveBlockData(Vector2Int pos)
    {
        localDamagedBlocks.Remove(pos);
    }

    private void UpdateMiningVisuals(Vector2Int pos, float ratio)
    {
        // 블록의 재질 정보를 가져옴 (사운드/파티클 구분용)
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        var stats = MapManager.Instance.GetBlockStats(block.id);

        // 이벤트를 발행하여 비주얼/사운드 시스템이 반응하게 함 (관심사 분리)
        OnBlockDamageDealt?.Invoke(pos, ratio, stats.material);
        
        // (참고) MapManager.Instance.SetBlockCrack(pos.x, pos.y, ratio) 등의 직접 호출도 가능하지만,
        // 이벤트를 통하는 것이 확장성 면에서 유리함.
    }

    #endregion

    #region Server Validation

    [Rpc(SendTo.Server)]
    private void RequestCompleteMiningServerRpc(Vector2Int pos, int toolPower, int toolHardness, float toolSpeed)
    {
        // 1. 블록 존재 여부 확인
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        // 2. 서버측 속도 제한 (Rate Limiting)
        // 블록을 파괴하는 데 필요한 최소 타격 횟수 계산
        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        int requiredHits = Mathf.CeilToInt((float)blockStats.maxHealth / toolPower);
        
        // 곡괭이 속도에 기반한 최소 소요 시간 (0.8은 네트워크 지연 등을 고려한 여유 계수)
        float minTimeRequired = (requiredHits / toolSpeed) * 0.8f;

        double currentTime = NetworkManager.Singleton.ServerTime.Time;
        if (currentTime - lastMiningTime < minTimeRequired)
        {
            // Debug.LogWarning($"[Security] Mining too fast! Player: {OwnerClientId}");
            return;
        }

        // 3. 강도 체크
        if (toolHardness < blockStats.hardness) return;

        // 4. 사거리 체크
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > 15f || diffY > 15f) return; 

        // 5. 파괴 승인
        lastMiningTime = currentTime;
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
