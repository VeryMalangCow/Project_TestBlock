using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class PlayerController : NetworkBehaviour
{
    #region Variable

    [Header("### Components")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D col;
    [SerializeField] private PlayerVisuals visuals;

    [Header("### Input Actions")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction interactAction;
    private InputAction pointAction;

    [Header("### Move")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 50f;
    [SerializeField] private float jumpForce = 13f;

    [Header("### Dash")]
    [SerializeField] private float dashSpeed = 28f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 0.5f;
    private float dashTimeLeft;
    private float dashCooldownTimer;
    private float dashDirection;

    [Header("### Physics")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private float stepHeight = 1.1f; 
    [SerializeField] private float stepCheckDistance = 0.1f;

    [Header("### Interaction")]
    [SerializeField] private float interactRange = 6f;
    [SerializeField] private GameObject itemDropPrefab;
    private int selectedHotbarIndex = 0;
    public int SelectedHotbarIndex => selectedHotbarIndex;

    private Vector2 moveInput;
    private bool isGrounded;

    private PlayerData playerData;
    public PlayerData Data => playerData;

    public static PlayerController Local { get; private set; }

    // [Public Property] 바라보는 방향 노출
    public bool IsFlipped => isFlippedSync.Value;

    // [Static Settings] 아이템 던지기 힘 설정
    public static float DropThrowForce = 4f;   // 가로 던지는 힘
    public static float DropUpwardForce = 6f;  // 세로(위쪽) 던지는 힘

    private string debugStatus = "Initializing...";

    private InputAction interact01Action;
    private InputAction scrollAction;
    private InputAction[] hotbarActions;

    #endregion

    #region Network Sync Variables

    // 1. Movement Sync
    private NetworkVariable<Vector2> moveInputSync = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isFlippedSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isGroundedSync = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 2. State Sync
    private NetworkVariable<bool> isDashingSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> dashDirectionSync = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> jumpCountSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private int lastProcessedJumpCount;

    // 3. Equipment Sync
    private NetworkVariable<int> helmetIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> chestplateIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> leggingsIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> backpackIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> cloakIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 4. Visual Appearance Sync
    private NetworkVariable<Color> skinColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> eyeColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> hairColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> hairStyleSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 5. Inventory Delta Sync
    private NetworkList<PlayerInventorySlotData> inventorySlotsSync;

    #endregion

    #region Awake & Setup

    private void Awake()
    {
        InitializeInput();
        SetupPhysics();
        EnsureEventSystem();
        
        // Initialize NetworkList (Must be done in Awake or Declaration)
        inventorySlotsSync = new NetworkList<PlayerInventorySlotData>(
            null, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);
    }

    private void InitializeInput()
    {
        if (inputActions == null)
        {
            Debug.LogError("[PlayerController] InputActionAsset is missing!");
            return;
        }

        var playerMap = inputActions.FindActionMap("Player");
        if (playerMap != null)
        {
            moveAction = playerMap.FindAction("Move");
            jumpAction = playerMap.FindAction("Jump");
            dashAction = playerMap.FindAction("Dash");
            interactAction = playerMap.FindAction("Interact_00");
            interact01Action = playerMap.FindAction("Interact_01");
            scrollAction = playerMap.FindAction("ScrollWheel");
            pointAction = playerMap.FindAction("Point");

            moveAction?.Enable();
            jumpAction?.Enable();
            dashAction?.Enable();
            interactAction?.Enable();
            interact01Action?.Enable();
            scrollAction?.Enable();
            pointAction?.Enable();

            // Event-driven Input Handling
            jumpAction.performed += OnJumpPerformed;
            dashAction.performed += OnDashPerformed;
            scrollAction.performed += OnScrollPerformed;

            // Hotbar Actions Initialization (Event-driven)
            hotbarActions = new InputAction[10];
            for (int i = 0; i < 10; i++)
            {
                int index = i; // Closure check
                string actionName = $"Hotbar{(i == 9 ? 0 : i + 1)}";
                var action = playerMap.FindAction(actionName);
                if (action != null)
                {
                    action.performed += _ => { if (IsOwner) selectedHotbarIndex = index; };
                    action.Enable();
                    hotbarActions[i] = action;
                }
            }
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        
        if (jumpAction != null) jumpAction.performed -= OnJumpPerformed;
        if (dashAction != null) dashAction.performed -= OnDashPerformed;
        if (scrollAction != null) scrollAction.performed -= OnScrollPerformed;

        moveAction?.Disable();
        jumpAction?.Disable();
        dashAction?.Disable();
        interactAction?.Disable();
        interact01Action?.Disable();
        scrollAction?.Disable();
        pointAction?.Disable();

        if (hotbarActions != null)
        {
            foreach (var action in hotbarActions)
            {
                action?.Disable();
            }
        }
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || debugStatus != "READY") return;
        if (isGrounded && !isDashingSync.Value) jumpCountSync.Value++;
    }

    private void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || debugStatus != "READY") return;
        if (!isDashingSync.Value && dashCooldownTimer <= 0)
        {
            float dir = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : (isFlippedSync.Value ? -1f : 1f);
            TriggerDashServerRpc(dir);
            dashTimeLeft = dashDuration;
            dashCooldownTimer = dashCooldown;
        }
    }

    private void OnScrollPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;
        float scroll = ctx.ReadValue<Vector2>().y;
        if (Mathf.Abs(scroll) > 0.1f)
        {
            if (scroll > 0) selectedHotbarIndex = (selectedHotbarIndex - 1 + 10) % 10;
            else selectedHotbarIndex = (selectedHotbarIndex + 1) % 10;
        }
    }

    private void SetupPhysics()
    {
        int layer = LayerMask.NameToLayer("Ground");
        if (layer != -1)
        {
            groundLayer = (1 << layer);
            if (rb != null)
            {
                rb.includeLayers = groundLayer;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
        }
    }

    private void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    #endregion

    #region Network Lifecycle

    public override void OnNetworkSpawn()
    {
        if (IsOwner) Local = this;

        // Inventory Delta Sync Event
        inventorySlotsSync.OnListChanged += OnInventoryListChanged;

        helmetIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Heads", newVal); };
        chestplateIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Clothes", newVal); };
        leggingsIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Leggings", newVal); };
        backpackIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Backpacks", newVal); };
        cloakIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Cloaks", newVal); };

        skinColorSync.OnValueChanged += (oldVal, newVal) => { visuals.SetSkinColor(newVal); };
        eyeColorSync.OnValueChanged += (oldVal, newVal) => { visuals.SetEyeColor(newVal); };
        hairColorSync.OnValueChanged += (oldVal, newVal) => { visuals.SetHairColor(newVal); };
        hairStyleSync.OnValueChanged += (oldVal, newVal) => { visuals.SetHair(newVal); };

        StartCoroutine(InitPlayerCo());
    }

    private void OnInventoryListChanged(NetworkListEvent<PlayerInventorySlotData> changeEvent)
    {
        // [Delta Sync] 리스트의 특정 인덱스가 변하면 로컬 데이터에 즉시 반영
        if (playerData != null && playerData.inventory != null)
        {
            playerData.inventory.SetSlot(changeEvent.Index, changeEvent.Value);
        }
    }

    private IEnumerator InitPlayerCo()
    {
        debugStatus = "Waiting for Map...";
        
        if (rb != null)
        {
            rb.simulated = false;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0;
            rb.linearVelocity = Vector2.zero;
        }
        if (visuals != null) visuals.gameObject.SetActive(false);

        while (MapManager.Instance == null || !MapManager.Instance.IsMapReady())
        {
            yield return null;
        }

        if (IsOwner)
        {
            debugStatus = "Requesting Spawn...";
            RequestSpawnServerRpc();
            yield return new WaitForSeconds(0.5f);

            playerData = new PlayerData();

            // [Server] 인벤토리 동기화 리스트 초기화 (50슬롯)
            if (IsServer)
            {
                inventorySlotsSync.Clear();
                for (int i = 0; i < 50; i++) inventorySlotsSync.Add(new PlayerInventorySlotData(-1, 0));
            }

            // [Test] 초기 아이템 지급 (ID 0~5)
            // 서버 권한으로 아이템을 추가하고 네트워크 리스트에 동기화
            if (IsServer)
            {
                for (int i = 0; i <= 5; i++)
                {
                    ItemData item = ItemDataManager.Instance.GetItem(i);
                    if (item != null)
                    {
                        playerData.inventory.AddItem(i, item.maxStack);
                    }
                }
                SyncInventoryToNetwork();
            }

            UpdateAppearance(playerData.visual);
            UpdateEquipment(playerData.equipment);
        }

        if (!IsServer || IsOwner)
        {
            debugStatus = "Rendering Local Chunks...";
            int retry = 0;
            while (retry < 100) 
            {
                if (MapManager.Instance != null && MapManager.Instance.IsTerrainReadyAt(transform.position))
                {
                    yield return new WaitForFixedUpdate();
                    break;
                }
                
                if (IsOwner && MeshManager.Instance != null)
                {
                    int cx = Mathf.FloorToInt(transform.position.x / ChunkData.Size);
                    int cy = Mathf.FloorToInt(transform.position.y / ChunkData.Size);
                    MeshManager.Instance.ForceRenderChunk(cx, cy);
                }
                retry++;
                yield return new WaitForSeconds(0.1f);
            }
        }

        if (visuals != null)
        {
            visuals.gameObject.SetActive(true);
            visuals.Init();
            
            visuals.SetSkinColor(skinColorSync.Value);
            visuals.SetEyeColor(eyeColorSync.Value);
            visuals.SetHairColor(hairColorSync.Value);
            visuals.SetHair(hairStyleSync.Value);
            visuals.SetArmor("Heads", helmetIdSync.Value);
            visuals.SetArmor("Clothes", chestplateIdSync.Value);
            visuals.SetArmor("Leggings", leggingsIdSync.Value);
        }

        if (IsOwner && Camera.main != null)
        {
            CameraController cam = Camera.main.GetComponent<CameraController>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.SetTarget(transform);
            }
        }

        debugStatus = "READY";
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.gravityScale = 3f;
        }
        enabled = true;
    }

    /// <summary>
    /// [Server-Only] 현재 로컬 인벤토리 데이터를 NetworkList에 덮어씌워 클라이언트에 전파합니다.
    /// </summary>
    public void SyncInventoryToNetwork()
    {
        if (!IsServer || playerData == null) return;

        for (int i = 0; i < inventorySlotsSync.Count; i++)
        {
            var localSlot = playerData.inventory.GetSlot(i);
            if (!inventorySlotsSync[i].Equals(localSlot))
            {
                inventorySlotsSync[i] = localSlot;
            }
        }
    }

    [ServerRpc]
    public void RequestSyncInventoryServerRpc()
    {
        SyncInventoryToNetwork();
    }

    [ServerRpc]
    private void RequestSpawnServerRpc()
    {
        Vector2 spawnPos = MapManager.Instance.GetSurfacePosition(50f);
        ConfirmSpawnClientRpc(spawnPos);
    }

    [ClientRpc]
    private void ConfirmSpawnClientRpc(Vector2 pos)
    {
        if (IsOwner)
        {
            transform.position = pos;
            rb.position = pos;
            rb.linearVelocity = Vector2.zero;
            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport(pos, Quaternion.identity, transform.localScale);
            debugStatus = "Spawned at Surface";
        }
    }

    #endregion

    #region Update Loops
private void Update()
{
    if (!IsOwner)
    {
        if (visuals != null)
        {
            visuals.SetFlip(isFlippedSync.Value);
            // [Optimization] Non-owners calculate animation frames locally based on synced velocity and state
            visuals.UpdateVisuals(rb.linearVelocity.x, isGroundedSync.Value, isDashingSync.Value);
        }
        return;
    }

    HandleOwnerInput();
    UpdateTimers();
    UpdateVisuals();
}

private void FixedUpdate()
{
    if (debugStatus != "READY") return;

    if (IsOwner)
    {
        CheckGrounded();

        if (jumpCountSync.Value > lastProcessedJumpCount)
        {
            if (isGrounded) ApplyJumpPhysics();
            lastProcessedJumpCount = jumpCountSync.Value;
        }

        if (isDashingSync.Value)
        {
            HandleDash();
            HandleStepClimb(Vector2.zero);
        }
        else
        {
            HandleHorizontalMovement(moveInput);
            HandleStepClimb(moveInput);
        }
    }
}

#endregion

#region Visuals & Physics Logic

private void UpdateVisuals()
{
    if (visuals == null) return;

    if (Mathf.Abs(moveInput.x) > 0.01f && !isDashingSync.Value)
    {
        isFlippedSync.Value = moveInput.x < 0;
        visuals.SetFlip(isFlippedSync.Value);
    }

    // [Separation] Delegate animation frame calculation to PlayerVisuals
    visuals.UpdateVisuals(rb.linearVelocity.x, isGrounded, isDashingSync.Value);
}

private void UpdateTimers()
{
    if (IsOwner)
    {
        if (dashCooldownTimer > 0) dashCooldownTimer -= Time.deltaTime;
        if (isDashingSync.Value)
        {
            dashTimeLeft -= Time.deltaTime;
            if (dashTimeLeft <= 0) EndDashServerRpc();
        }
    }
}

private void HandleDash()
{
    rb.gravityScale = 0;
    rb.linearVelocity = new Vector2(dashDirectionSync.Value * dashSpeed, 0);
}

private void HandleStepClimb(Vector2 input)
{
    float dir = isDashingSync.Value ? dashDirectionSync.Value : (input.x != 0 ? Mathf.Sign(input.x) : 0);
    if (dir == 0) return;

    Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y + 0.1f);
    Vector2 direction = new Vector2(dir, 0);
    float distance = (col.size.x / 2f) + stepCheckDistance;

    RaycastHit2D hitLower = Physics2D.Raycast(origin, direction, distance, groundLayer);
    if (hitLower.collider != null)
    {
        Vector2 upperOrigin = origin + new Vector2(0, stepHeight);
        RaycastHit2D hitUpper = Physics2D.Raycast(upperOrigin, direction, distance, groundLayer);
        if (hitUpper.collider == null) rb.position += new Vector2(0, 0.25f);
    }
}

private void CheckGrounded()
{
    if (col == null) return;
    float boxWidth = col.bounds.size.x * 0.85f;
    float boxHeight = groundCheckRadius;
    Vector2 boxCenter = new Vector2(col.bounds.center.x, col.bounds.min.y - (boxHeight / 2f));
    isGrounded = Physics2D.OverlapBox(boxCenter, new Vector2(boxWidth, boxHeight), 0f, groundLayer);

    // Sync grounded state to other clients for local animation calculation
    if (isGroundedSync.Value != isGrounded) isGroundedSync.Value = isGrounded;
}
    private void HandleHorizontalMovement(Vector2 input)
    {
        rb.gravityScale = 3f;
        float targetSpeed = input.x * moveSpeed;
        float currentSpeed = rb.linearVelocity.x;
        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? acceleration : deceleration;
        float newX = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    private void ApplyJumpPhysics()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

    #endregion

    #region Input Handling

    private void HandleOwnerInput()
    {
        if (!IsOwner || moveAction == null) return;
        moveInput = moveAction.ReadValue<Vector2>();
        moveInputSync.Value = moveInput; 
    }

    [ServerRpc] private void TriggerDashServerRpc(float direction) { isDashingSync.Value = true; dashDirectionSync.Value = direction; }
    [ServerRpc] private void EndDashServerRpc() { isDashingSync.Value = false; }

    #endregion

    #region Data Update (Visual & Equipment)

    public void UpdateAppearance(PlayerVisualData visualData)
    {
        if (!IsOwner) return;
        Color sCol, eCol, hCol;
        if (ColorUtility.TryParseHtmlString(visualData.skinColorHex, out sCol)) skinColorSync.Value = sCol;
        if (ColorUtility.TryParseHtmlString(visualData.eyeColorHex, out eCol)) eyeColorSync.Value = eCol;
        if (ColorUtility.TryParseHtmlString(visualData.hairColorHex, out hCol)) hairColorSync.Value = hCol;
        hairStyleSync.Value = visualData.hairStyleIndex;
    }

    public void UpdateEquipment(PlayerEquipmentData equipmentData)
    {
        if (!IsOwner) return;
        helmetIdSync.Value = equipmentData.helmetIndex;
        chestplateIdSync.Value = equipmentData.chestplateIndex;
        leggingsIdSync.Value = equipmentData.leggingsIndex;
    }

    #endregion

    #region Interaction

    private void HandleInteraction()
    {
        // Continuous interaction logic if needed in the future
    }

    private void UseItem(int buttonIndex)
    {
        if (playerData == null || playerData.inventory == null) return;
        PlayerInventorySlotData selectedSlot = playerData.inventory.GetSlot(selectedHotbarIndex);
        if (selectedSlot.IsEmpty) return;

        // [Logic] Item interaction framework (Block placement as test)
        if (selectedSlot.itemID >= 0)
        {
            Vector2 screenPos = pointAction.ReadValue<Vector2>();
            UpdateBlock(selectedSlot.itemID, screenPos);
        }
    }

    private void UpdateBlock(int id, Vector2 screenPos)
    {
        if (MapManager.Instance == null || Camera.main == null) return;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
        if (Vector2.Distance(transform.position, worldPos) > interactRange) return;
        UpdateBlockServerRpc(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y), id);
    }

    [ServerRpc] private void UpdateBlockServerRpc(int x, int y, int id) { MapManager.Instance.SetBlock(x, y, id); }

    [ServerRpc]
    public void DropItemServerRpc(int id, int count, float lookDir)
    {
        if (itemDropPrefab == null)
        {
            Debug.LogError("[PlayerController] itemDropPrefab is not assigned!");
            return;
        }

        // 1. 플레이어 본체 위치에서 바라보는 방향으로 살짝 앞에서 생성 (겹침 방지)
        Vector3 spawnPos = transform.position + new Vector3(lookDir * 0.8f, 0.5f, 0);
        NetworkObject netObj = NetworkObjectPoolManager.Instance.Spawn(itemDropPrefab, spawnPos, Quaternion.identity);
        
        // 2. 데이터 설정
        ItemController item = netObj.GetComponent<ItemController>();
        item.itemID.Value = id;
        item.stackCount.Value = count;

        // 3. 던지는 연출 (인자로 받은 방향 사용)
        Vector2 throwForce = new Vector2(lookDir * DropThrowForce, DropUpwardForce);
        
        // ItemController 내부에 Rigidbody2D가 있으므로 힘 전달
        if (netObj.TryGetComponent<Rigidbody2D>(out var rb))
        {
            rb.linearVelocity = throwForce;
        }

        // 4. 아이템 쿨다운 활성화 (자석 기능 2초 정지, 튕기기 효과는 꺼짐)
        item.SetDropCooldown(false);
    }

    #endregion

    #region Debug GUI

    private void OnGUI()
    {
        if (!IsOwner) return;
        GUI.color = Color.black;
        GUILayout.BeginArea(new Rect(15, 15, 300, 250));
        GUILayout.Label($"<b>[PLAYER STATUS]</b>");
        GUILayout.Label($"OwnerID: {OwnerClientId}");
        GUILayout.Label($"Position: {transform.position}");
        GUILayout.Label($"Status: {debugStatus}");
        GUILayout.Label($"IsGrounded: {isGrounded}");
        GUILayout.Label($"JumpCount: {jumpCountSync.Value}");
        GUILayout.Label($"Dashing: {isDashingSync.Value}");
        GUILayout.EndArea();
    }

    #endregion
}
