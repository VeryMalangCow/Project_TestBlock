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
    
    // [Network Sync] 수동 위치 동기화용 변수
    private NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

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
    public static float PositionThreshold = 0.05f; // 이 값보다 많이 움직여야 네트워크 업데이트

    private float currentSpeed;
    private float cooldownTimer;
    private float searchTimer; 
    private PlayerController targetPlayer;
    private bool isAttracted;
    private bool isBeingPickedUp; 

    #endregion

    #region Lifecycle

    private UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> iconHandle;

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
            netPosition.Value = transform.position;
        }
        else
        {
            // 클라이언트는 서버의 현재 위치에서 시작
            transform.position = netPosition.Value;
        }
    }

    public override void OnNetworkDespawn()
    {
        ReleaseIcon();

        if (IsServer)
        {
            // 풀에 돌아갈 때 상태 초기화
            targetPlayer = null;
            isAttracted = false;
            isBeingPickedUp = false;
            cooldownTimer = 0;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy(); // 부모(NetworkBehaviour)의 정리 로직 실행
        ReleaseIcon();
    }

    private void ReleaseIcon()
    {
        if (iconHandle.IsValid())
        {
            UnityEngine.AddressableAssets.Addressables.Release(iconHandle);
        }
    }

    private void Update()
    {
        if (IsServer)
        {
            // 서버는 물리 연산 결과로 netPosition 업데이트 (FixedUpdate에서 수행)
        }
        else
        {
            // 클라이언트: 서버 위치로 부드럽게 보간 (Interpolation)
            transform.position = Vector3.Lerp(transform.position, netPosition.Value, Time.deltaTime * 15f);
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // 1. 버림/생성 초기 쿨다운 체크
        if (cooldownTimer > 0)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            SyncPositionToServer();
            return;
        }

        // 2. 이미 흡수 중인 경우
        if (isAttracted)
        {
            HandleAttraction();
        }
        else
        {
            // 3. 탐색 중인 경우
            searchTimer -= Time.fixedDeltaTime;
            if (searchTimer <= 0)
            {
                SearchForPlayer();
                searchTimer = SearchInterval;
            }
        }

        // 4. 위치 동기화
        SyncPositionToServer();
    }

    private void SyncPositionToServer()
    {
        if (Vector2.Distance(netPosition.Value, transform.position) > PositionThreshold)
        {
            netPosition.Value = transform.position;
        }
    }

    #endregion

    #region Core Logic (Server Only)

    private void SearchForPlayer()
    {
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
        }
    }

    private void HandleAttraction()
    {
        if (targetPlayer == null || !targetPlayer.IsSpawned)
        {
            ResetAttraction();
            return;
        }

        currentSpeed = Mathf.Min(currentSpeed + Acceleration * Time.fixedDeltaTime, MaxSpeed);
        Vector2 nextPos = Vector2.MoveTowards(rb.position, targetPlayer.transform.position, currentSpeed * Time.fixedDeltaTime);
        rb.MovePosition(nextPos);

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
        if (rb != null) rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // [Added] 안정성 강화
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

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!IsServer) return;

        // [New] Stay 상태에서도 지속적으로 획득 시도 (Enter를 놓쳤을 경우 대비)
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

        if (player.Data == null || player.Data.inventory == null)
        {
            // 아직 플레이어 데이터가 준비되지 않았을 수 있음
            return;
        }

        int initialCount = stackCount.Value;
        if (initialCount <= 0) return;

        isBeingPickedUp = true; 

        // [Logic] 아이템 추가 시도
        int remaining = player.Data.inventory.AddItem(itemID.Value, initialCount);

        if (remaining <= 0)
        {
            // 모두 획득 성공
            stackCount.Value = 0; 
            if (IsServer) GetComponent<NetworkObject>().Despawn();
        }
        else
        {
            // 일부만 획득했거나 인벤토리가 가득 참
            if (remaining < initialCount)
            {
                stackCount.Value = remaining;
                SetDropCooldown(true); // 튕겨나감
            }
            else
            {
                // 아예 못 얻은 경우 (가득 참)
                if (isAttracted) SetDropCooldown(true);
            }
            
            // 다시 먹을 수 있도록 플래그 해제
            isBeingPickedUp = false; 
        }
    }

    #endregion

    #region Visuals

    private void UpdateVisual(int id)
    {
        if (id < 0 || spriteRenderer == null) return;

        // [Optimized] 매번 어드레서블을 로드하는 대신 중앙 캐시 사용 (0프레임 로딩)
        Sprite icon = ItemDataManager.Instance.GetItemIcon(id);
        if (icon != null)
        {
            spriteRenderer.sprite = icon;
        }
        else
        {
            // 아직 로드되지 않은 경우 (드문 케이스)
            StartCoroutine(RetryUpdateVisual(id));
        }
    }

    private IEnumerator RetryUpdateVisual(int id)
    {
        int retries = 0;
        while (retries < 5)
        {
            yield return new WaitForSeconds(0.2f);
            Sprite icon = ItemDataManager.Instance.GetItemIcon(id);
            if (icon != null)
            {
                spriteRenderer.sprite = icon;
                yield break;
            }
            retries++;
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
