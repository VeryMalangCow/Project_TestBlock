using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ItemController : NetworkBehaviour
{
    #region Variables

    [Header("### Item Data")]
    public NetworkVariable<int> itemID = new NetworkVariable<int>(-1);
    public NetworkVariable<int> stackCount = new NetworkVariable<int>(0);

    [Header("### References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Rigidbody2D rb;

    // [Static Settings] 메모리 최적화를 위한 공통 설정값
    public static float SearchRadius = 5f;
    public static float SearchInterval = 0.2f;
    public static float InitialSpeed = 10f;
    public static float Acceleration = 30f;
    public static float MaxSpeed = 20f;
    public static float DropCooldownTime = 2f;

    private float currentSpeed;
    private float cooldownTimer;
    private float searchTimer; 
    private PlayerController targetPlayer;
    private bool isAttracted;
    private bool isBeingPickedUp; 

    #endregion

    #region Lifecycle

    public override void OnNetworkSpawn()
    {
        // 서버/클라이언트 공통: 아이템 ID가 결정되면 스프라이트 업데이트
        itemID.OnValueChanged += (oldVal, newVal) => UpdateVisual(newVal);
        if (itemID.Value != -1) UpdateVisual(itemID.Value);

        if (IsServer)
        {
            // 여러 아이템의 탐색 시점을 분산 (static 값 참조)
            searchTimer = Random.Range(0, SearchInterval);
            isBeingPickedUp = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            // 풀에 돌아갈 때 상태 초기화
            targetPlayer = null;
            isAttracted = false;
            isBeingPickedUp = false;
            cooldownTimer = 0;
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        // 1. 버림/생성 초기 쿨다운 체크
        if (cooldownTimer > 0)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        // 2. 이미 흡수 중인 경우: 매 프레임 부드럽게 이동
        if (isAttracted)
        {
            HandleAttraction();
        }
        else
        {
            // 3. 탐색 중인 경우: 설정된 주기마다 효율적으로 확인
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0)
            {
                SearchForPlayer();
                searchTimer = SearchInterval;
            }
        }
    }

    #endregion

    #region Core Logic (Server Only)

    private void SearchForPlayer()
    {
        // [Fix] Player 레이어만 콕 집어서 탐색 (static 값 참조)
        int playerLayer = LayerMask.GetMask("Player");
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, SearchRadius, playerLayer);
        
        PlayerController closest = null;
        float minDistance = float.MaxValue;

        foreach (var hit in hitColliders)
        {
            if (hit.attachedRigidbody != null && hit.attachedRigidbody.CompareTag("Player"))
            {
                if (hit.attachedRigidbody.TryGetComponent<PlayerController>(out var player))
                {
                    float dist = Vector2.Distance(transform.position, hit.transform.position);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closest = player;
                    }
                }
            }
        }

        if (closest != null)
        {
            targetPlayer = closest;
            isAttracted = true;
            currentSpeed = InitialSpeed;
            
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            
            Debug.Log($"[ItemController] Started attracting to player: {closest.OwnerClientId}");
        }
    }

    private void HandleAttraction()
    {
        if (targetPlayer == null || !targetPlayer.IsSpawned)
        {
            ResetAttraction();
            return;
        }

        // 플레이어 방향으로 가속 이동 (static 값 참조)
        currentSpeed = Mathf.Min(currentSpeed + Acceleration * Time.deltaTime, MaxSpeed);
        transform.position = Vector3.MoveTowards(transform.position, targetPlayer.transform.position, currentSpeed * Time.deltaTime);

        // 만약 타겟이 너무 멀어지면 (탐색 범위 1.5배 이상) 다시 탐색
        if (Vector2.Distance(transform.position, targetPlayer.transform.position) > SearchRadius * 1.5f)
        {
            ResetAttraction();
        }
    }

    private void ResetAttraction()
    {
        isAttracted = false;
        targetPlayer = null;
        rb.bodyType = RigidbodyType2D.Dynamic;
    }

    public void SetDropCooldown(bool applyBounce = false)
    {
        cooldownTimer = DropCooldownTime;
        ResetAttraction();
        
        if (applyBounce)
        {
            rb.linearVelocity = Vector2.up * 5f + new Vector2(Random.Range(-2f, 2f), 0);
        }
    }

    #endregion

    #region Pickup Logic (Collision)

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;

        if (collision.attachedRigidbody != null && collision.attachedRigidbody.CompareTag("Player"))
        {
            if (collision.attachedRigidbody.TryGetComponent<PlayerController>(out var player))
            {
                TryPickup(player);
            }
        }
    }

    private void TryPickup(PlayerController player)
    {
        if (cooldownTimer > 0 || isBeingPickedUp) return;

        if (player.Data == null || player.Data.inventory == null) return;

        int initialCount = stackCount.Value;
        if (initialCount <= 0) return;

        isBeingPickedUp = true; 

        int remaining = player.Data.inventory.AddItem(itemID.Value, initialCount);

        if (remaining <= 0)
        {
            stackCount.Value = 0; 
            GetComponent<NetworkObject>().Despawn();
        }
        else
        {
            if (remaining < initialCount)
            {
                stackCount.Value = remaining;
                SetDropCooldown(true); 
            }
            else
            {
                if (isAttracted) SetDropCooldown(true);
            }
            isBeingPickedUp = false; 
        }
    }

    #endregion

    #region Visuals

    private void UpdateVisual(int id)
    {
        ItemData data = ItemDataManager.Instance.GetItem(id);
        if (data != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = data.icon;
        }
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, SearchRadius);
    }

    #endregion
}
