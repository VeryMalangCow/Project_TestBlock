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
    private Dictionary<Vector2Int, BlockDamageData> localDamagedBlocks = new Dictionary<Vector2Int, BlockDamageData>();
    private Dictionary<Vector2Int, float> pendingBlocks = new Dictionary<Vector2Int, float>(); // 좌표, 요청 시간
    private List<Vector2Int> keysToRemove = new List<Vector2Int>();
    
    private bool isMining = false;
    private PickaxeProperty currentToolStats;

    private const float RESET_TIMEOUT = 5.0f; 
    private const float PENDING_RETRY_TIMEOUT = 0.5f; // 서버 응답이 0.5초간 없으면 재타격 허용
    private float cleanupTimer = 0f;

    // --- Server Side States ---
    private double miningBudget = 0; 
    private double lastBudgetTime = 0;
    private const double MAX_BUDGET = 2.0; 

    public void Init(PlayerController ctrl)
    {
        controller = ctrl;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            // [Fix] 서버 시작 시 채굴 예산을 가득 채워 첫 타격 거절 방지
            miningBudget = MAX_BUDGET;
            lastBudgetTime = NetworkManager.Singleton.ServerTime.Time;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= 1.0f)
        {
            cleanupTimer = 0f;
            CheckBlockRecovery();
        }
    }

    public void ProcessMiningClient(PickaxeProperty stats, UseContext context)
    {
        if (!IsOwner) return;

        Vector2 mousePos = context.MouseWorldPos;
        Vector2Int targetPos = new Vector2Int(Mathf.FloorToInt(mousePos.x), Mathf.FloorToInt(mousePos.y));

        isMining = true;
        currentToolStats = stats;

        // [Fix] Pending 체크는 ApplyMiningHit 내부에서 타임아웃과 함께 수행하여 데드락 방지
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
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        
        // 1. 이미 파괴된 블록이면 대기 목록에서 즉시 제거
        if (!block.isActive) 
        {
            if (pendingBlocks.ContainsKey(pos)) pendingBlocks.Remove(pos);
            return;
        }

        // 2. 서버 승인 대기 중인지 확인 (타임아웃 로직 포함)
        if (pendingBlocks.TryGetValue(pos, out float requestTime))
        {
            if (Time.time - requestTime < PENDING_RETRY_TIMEOUT) return;
            else pendingBlocks.Remove(pos); // 0.5초 경과 시 서버 거절로 간주하고 재시도 허용
        }

        var blockStats = MapManager.Instance.GetBlockStats(block.id);

        // 3. 사거리 체크
        Vector2 playerPos = transform.position;
        float diffX = Mathf.Abs(pos.x + 0.5f - playerPos.x);
        float diffY = Mathf.Abs(pos.y + 0.5f - playerPos.y);
        if (diffX > stats.rangeWidth || diffY > stats.rangeHeight) return;

        // 4. 이펙트 및 사운드 재생 (데미지와 상관없이 사거리 내면 항상 재생)
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayHitFX((Vector2)(Vector2)pos + new Vector2(0.5f, 0.5f), block.id);
        }

        // 5. 강도 체크 (강도가 부족하면 데미지는 주지 않음)
        if (stats.hardness < blockStats.hardness) return;

        if (!localDamagedBlocks.TryGetValue(pos, out BlockDamageData data))
        {
            data = new BlockDamageData(0, Time.time);
        }
        
        data.currentDamage += stats.power;
        data.lastHitTime = Time.time;
        localDamagedBlocks[pos] = data;

        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayHitFX((Vector2)(Vector2)pos + new Vector2(0.5f, 0.5f), block.id);
        }

        float crackRatio = Mathf.Clamp01((float)data.currentDamage / blockStats.maxHealth);
        UpdateMiningVisuals(pos, crackRatio);

        if (data.currentDamage >= blockStats.maxHealth)
        {
            CompleteMining(pos, stats);
        }
    }

    private void CompleteMining(Vector2Int pos, PickaxeProperty stats)
    {
        if (pendingBlocks.ContainsKey(pos)) return;

        pendingBlocks.Add(pos, Time.time); 
        RequestCompleteMiningServerRpc(pos, stats.power, stats.hardness, stats.speed);
    }

    private void CheckBlockRecovery()
    {
        float currentTime = Time.time;
        keysToRemove.Clear();

        foreach (var kvp in localDamagedBlocks)
        {
            bool isTimedOut = (currentTime - kvp.Value.lastHitTime > RESET_TIMEOUT);
            bool isAlreadyBroken = !MapManager.Instance.IsBlockActive(kvp.Key.x, kvp.Key.y);

            if (isTimedOut || isAlreadyBroken) keysToRemove.Add(kvp.Key);
        }

        foreach (var pos in keysToRemove)
        {
            if (MapManager.Instance.IsBlockActive(pos.x, pos.y))
                UpdateMiningVisuals(pos, 0);
            
            localDamagedBlocks.Remove(pos);
            if (pendingBlocks.ContainsKey(pos)) pendingBlocks.Remove(pos);
        }

        // 대기 목록 중 이미 파괴된 블록 정리
        var brokenKeys = pendingBlocks.Keys.Where(pos => !MapManager.Instance.IsBlockActive(pos.x, pos.y)).ToList();
        foreach (var key in brokenKeys) pendingBlocks.Remove(key);
    }

    private void UpdateMiningVisuals(Vector2Int pos, float ratio)
    {
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive && ratio > 0) return;

        var stats = MapManager.Instance.GetBlockStats(block.id);
        OnBlockDamageDealt?.Invoke(pos, ratio, stats.material);
    }

    #endregion

    #region Server Validation

    [Rpc(SendTo.Server)]
    private void RequestCompleteMiningServerRpc(Vector2Int pos, int toolPower, int toolHardness, float toolSpeed)
    {
        var block = MapManager.Instance.GetBlock(pos.x, pos.y);
        if (!block.isActive) return;

        var blockStats = MapManager.Instance.GetBlockStats(block.id);
        int requiredHits = Mathf.CeilToInt((float)blockStats.maxHealth / toolPower);
        float timeToMine = (requiredHits / toolSpeed);

        double currentTime = NetworkManager.Singleton.ServerTime.Time;
        if (lastBudgetTime == 0) lastBudgetTime = currentTime;

        double deltaTime = currentTime - lastBudgetTime;
        miningBudget = System.Math.Min(MAX_BUDGET, miningBudget + deltaTime);
        lastBudgetTime = currentTime;

        double cost = timeToMine * 0.85f;

        if (miningBudget < cost) return;

        miningBudget -= cost;

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
