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

    [Header("### Physics & Movement")]
    [SerializeField] private float searchRadius = 5f;
    [SerializeField] private float searchInterval = 0.2f; // 탐색 주기 (초)
    [SerializeField] private float initialSpeed = 2f;
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float maxSpeed = 25f;
    [SerializeField] private float dropCooldownTime = 2f;

    private float currentSpeed;
    private float cooldownTimer;
    private float searchTimer; // 탐색용 타이머
    private PlayerController targetPlayer;
    private bool isAttracted;

    #endregion

    #region Lifecycle

    public override void OnNetworkSpawn()
    {
        // 서버/클라이언트 공통: 아이템 ID가 결정되면 스프라이트 업데이트
        itemID.OnValueChanged += (oldVal, newVal) => UpdateVisual(newVal);
        if (itemID.Value != -1) UpdateVisual(itemID.Value);

        if (IsServer)
        {
            // 처음 생성 시 또는 버려졌을 때 쿨다운 설정
            SetDropCooldown();
            searchTimer = Random.Range(0, searchInterval); // 여러 아이템의 탐색 시점을 분산
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
                searchTimer = searchInterval;
            }
        }
    }

    #endregion

    #region Core Logic (Server Only)

    private void SearchForPlayer()
    {
        // 주변 플레이어 탐색
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, searchRadius);
        PlayerController closest = null;
        float minDistance = float.MaxValue;

        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player") && hit.TryGetComponent<PlayerController>(out var player))
            {
                float dist = Vector2.Distance(transform.position, hit.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = player;
                }
            }
        }

        if (closest != null)
        {
            targetPlayer = closest;
            isAttracted = true;
            currentSpeed = initialSpeed;
            
            // 흡수 시작 시 물리 엔진 영향 최소화 (사용자의 레이어 설정을 보조하기 위해)
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void HandleAttraction()
    {
        if (targetPlayer == null || !targetPlayer.IsSpawned)
        {
            ResetAttraction();
            return;
        }

        // 플레이어 방향으로 가속 이동
        currentSpeed = Mathf.Min(currentSpeed + acceleration * Time.deltaTime, maxSpeed);
        transform.position = Vector3.MoveTowards(transform.position, targetPlayer.transform.position, currentSpeed * Time.deltaTime);

        // 만약 타겟이 너무 멀어지면 (탐색 범위 1.5배 이상) 다시 탐색
        if (Vector2.Distance(transform.position, targetPlayer.transform.position) > searchRadius * 1.5f)
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

    public void SetDropCooldown()
    {
        cooldownTimer = dropCooldownTime;
        ResetAttraction();
        
        // 버려질 때 약간의 튀어오름 효과 (서버에서 물리 적용)
        rb.linearVelocity = Vector2.up * 5f + new Vector2(Random.Range(-2f, 2f), 0);
    }

    #endregion

    #region Pickup Logic (Collision)

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;

        // [Fix] 자식 콜라이더가 충돌해도 부모의 Rigidbody2D(본체)를 통해 플레이어 판별
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
        if (player.Data == null || player.Data.inventory == null) return;

        // 인벤토리에 추가 시도 (NGO의 서버 권한으로 처리)
        int initialCount = stackCount.Value;
        int remaining = player.Data.inventory.AddItem(itemID.Value, initialCount);

        if (remaining == 0)
        {
            // 전부 획득 완료
            GetComponent<NetworkObject>().Despawn();
        }
        else if (remaining < initialCount)
        {
            // 일부만 획득 (공간 부족)
            stackCount.Value = remaining;
            SetDropCooldown(); // 남은 거 버림 처리
        }
        else
        {
            // 하나도 못 먹음 (인벤토리 꽉 참)
            if (isAttracted) SetDropCooldown(); // 흡수 중이었다면 튕겨나감
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
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }

    #endregion
}
