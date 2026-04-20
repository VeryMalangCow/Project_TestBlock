using System.Collections;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// 플레이어의 네트워크 상태, 입력 초기화 및 컴포넌트 조율을 담당하는 중앙 허브 클래스입니다.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    #region Variable & Components

    [Header("### Components")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D col;
    [SerializeField] private PlayerVisuals visuals;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInteraction interaction;

    [Header("### Input Actions")]
    [SerializeField] private InputActionAsset inputActions;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction interactAction;
    private InputAction interact01Action;
    private InputAction scrollAction;
    private InputAction pointAction;
    private InputAction[] hotbarActions;

    [Header("### Resources")]
    [SerializeField] private GameObject itemDropPrefab;

    private int selectedHotbarIndex = 0;
    public int SelectedHotbarIndex => selectedHotbarIndex;
    public PlayerData Data => playerData;
    public static PlayerController Local { get; private set; }
    public bool IsFlipped => isFlippedSync.Value;
    public bool IsDashing => isDashingSync.Value;
    public float DashDirection => dashDirectionSync.Value;

    private Vector2 moveInput;
    private PlayerData playerData;
    private string debugStatus = "Initializing...";

    // [New] Animation Proxy Data
    private Vector3 lastPosition;
    private float proxySpeedX;

    #endregion

    #region Network Sync Variables

    // Movement & State Sync
    private NetworkVariable<Vector2> moveInputSync = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isFlippedSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isGroundedSync = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isDashingSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<float> dashDirectionSync = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> jumpCountSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private int lastProcessedJumpCount;

    // Appearance & Equipment Sync
    private NetworkVariable<int> helmetIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> chestplateIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> leggingsIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> bootsIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> jetbagIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> skinColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> eyeColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> hairColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> hairStyleSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkList<PlayerInventorySlotData> inventorySlotsSync;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeInput();
        SetupComponents();
        EnsureEventSystem();
        
        inventorySlotsSync = new NetworkList<PlayerInventorySlotData>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    private void SetupComponents()
    {
        int layer = LayerMask.NameToLayer("Ground");
        LayerMask groundMask = (layer != -1) ? (1 << layer) : 0;

        if (rb != null)
        {
            rb.includeLayers = groundMask;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // 컴포넌트 초기화
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (interaction == null) interaction = GetComponent<PlayerInteraction>();

        movement?.Init(this, rb, col, groundMask);
        interaction?.Init(this, itemDropPrefab);
    }

    private void InitializeInput()
    {
        if (inputActions == null) return;

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

            jumpAction.performed += OnJumpPerformed;
            dashAction.performed += OnDashPerformed;
            scrollAction.performed += OnScrollPerformed;
            interactAction.performed += OnInteract00Performed;
            interact01Action.performed += OnInteract01Performed;

            hotbarActions = new InputAction[10];
            for (int i = 0; i < 10; i++)
            {
                int index = i;
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

        // [Fix] 오직 로컬 플레이어 객체가 파괴될 때만 입력 시스템을 정리합니다.
        // 클라이언트(Proxy) 객체가 파괴될 때 공유 에셋인 InputActions를 꺼버리는 것을 방지합니다.
        if (IsOwner)
        {
            if (Local == this) Local = null;

            if (jumpAction != null) jumpAction.performed -= OnJumpPerformed;
            if (dashAction != null) dashAction.performed -= OnDashPerformed;
            if (scrollAction != null) scrollAction.performed -= OnScrollPerformed;
            if (interactAction != null) interactAction.performed -= OnInteract00Performed;
            if (interact01Action != null) interact01Action.performed -= OnInteract01Performed;
            
            moveAction?.Disable();
            jumpAction?.Disable();
            dashAction?.Disable();
            interactAction?.Disable();
            interact01Action?.Disable();
            scrollAction?.Disable();
            pointAction?.Disable();

            if (hotbarActions != null) foreach (var action in hotbarActions) action?.Disable();
        }
    }

    #endregion

    #region Input Callbacks

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || debugStatus != "READY") return;
        if (movement.IsGrounded && !isDashingSync.Value) jumpCountSync.Value++;
    }

    private void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || debugStatus != "READY") return;
        if (!isDashingSync.Value && movement.DashCooldownTimer <= 0)
        {
            float dir = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : (isFlippedSync.Value ? -1f : 1f);
            
            // [Fix] Owner updates variables directly for instant physical response
            dashDirectionSync.Value = dir;
            isDashingSync.Value = true;
            
            movement.StartDash(dir);
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

    #endregion

    #region Network Logic

    public override void OnNetworkSpawn()
    {
        // [Data Initialization] 모든 객체는 데이터를 가져야 동기화 가능
        playerData = new PlayerData();

        if (IsOwner) Local = this;
        
        // [Sync Event Subscription]
        inventorySlotsSync.OnListChanged += OnInventoryListChanged;
        
        // [Important] NetworkList는 스폰 시점에 이미 데이터가 있을 수 있으므로 초기 수동 동기화 수행
        if (!IsServer && inventorySlotsSync.Count > 0)
        {
            for (int i = 0; i < inventorySlotsSync.Count; i++)
            {
                playerData.inventory.SetSlot(i, inventorySlotsSync[i]);
            }
        }

        helmetIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetArmor("Helmet", newVal);
        chestplateIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetArmor("Chestplate", newVal);
        leggingsIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetArmor("Leggings", newVal);
        bootsIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetArmor("Boots", newVal);
        jetbagIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetArmor("Jetbag", newVal);

        skinColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetSkinColor(newVal);
        eyeColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetEyeColor(newVal);
        hairColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetHairColor(newVal);
        hairStyleSync.OnValueChanged += (oldVal, newVal) => visuals.SetHair(newVal);

        StartCoroutine(InitPlayerCo());
    }

    private void OnInventoryListChanged(NetworkListEvent<PlayerInventorySlotData> changeEvent)
    {
        if (playerData == null || playerData.inventory == null) return;

        switch (changeEvent.Type)
        {
            case NetworkListEvent<PlayerInventorySlotData>.EventType.Add:
            case NetworkListEvent<PlayerInventorySlotData>.EventType.Value:
                playerData.inventory.SetSlot(changeEvent.Index, changeEvent.Value);
                break;
            case NetworkListEvent<PlayerInventorySlotData>.EventType.Clear:
                // Clear 시 로컬 인벤토리도 초기화 로직이 필요할 수 있음
                break;
            // 필요한 다른 타입들(Remove 등) 처리...
        }
    }

    private IEnumerator InitPlayerCo()
    {
        debugStatus = "Waiting for Map...";
        if (rb != null) { rb.simulated = false; rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0; }
        if (visuals != null) visuals.gameObject.SetActive(false);

        // [Common] 데이터베이스 및 맵 준비 대기 (모든 클라이언트 공통)
        while (MapManager.Instance == null || !MapManager.Instance.IsMapReady()) yield return null;
        
        int dbRetry = 0;
        while (ItemDataManager.Instance.GetItem(9) == null && dbRetry < 20)
        {
            yield return new WaitForSeconds(0.1f);
            dbRetry++;
        }

        // [Server-Authoritative Data Initialization]
        if (IsServer)
        {
            debugStatus = "Initializing Data (Server)...";
            
            // 인벤토리 리스트 초기화 (서버가 리스트를 채우면 클라이언트로 전송됨)
            inventorySlotsSync.Clear();
            for (int i = 0; i < 50; i++) 
            {
                inventorySlotsSync.Add(new PlayerInventorySlotData(-1, 0));
            }

            // 테스트용 초기 아이템 지급
            for (int i = 0; i <= 9; i++)
            {
                ItemData item = ItemDataManager.Instance.GetItem(i);
                if (item != null) 
                {
                    playerData.inventory.AddItem(i, item.maxStack);
                }
            }
            
            // 네트워크 리스트에 최종 반영
            for (int i = 0; i < inventorySlotsSync.Count; i++)
            {
                inventorySlotsSync[i] = playerData.inventory.GetSlot(i);
            }
        }

        // [Owner-Specific Logic]
        if (IsOwner)
        {
            debugStatus = "Requesting Spawn...";
            RequestSpawnServerRpc();
            
            while (debugStatus == "Requesting Spawn...") yield return null;

            if (MeshManager.Instance != null) MeshManager.Instance.SetTarget(transform);

            debugStatus = "Building Terrain...";
            float terrainWaitStartTime = Time.time;
            while (!MapManager.Instance.IsTerrainReadyAt(rb.position) && Time.time - terrainWaitStartTime < 5f)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // [Important] 본인의 데이터(커스터마이징 등)를 네트워크 변수에 반영합니다.
            // NetworkVariable이 OwnerWrite 권한이므로, 반드시 Owner가 직접 써야 합니다.
            UpdateAppearance(playerData.visual);
            UpdateEquipment(playerData.equipment);
            
            // 데이터가 모두 준비된 후 UI 갱신 강제
            InventoryUI ui = Object.FindAnyObjectByType<InventoryUI>();
            if (ui != null) ui.RefreshUI();
        }

        // [Visual Initialization]
        if (visuals != null)
        {
            visuals.gameObject.SetActive(true);
            visuals.Init();
            
            visuals.SetSkinColor(skinColorSync.Value);
            visuals.SetEyeColor(eyeColorSync.Value);
            visuals.SetHairColor(hairColorSync.Value);
            visuals.SetHair(hairStyleSync.Value);
            visuals.SetArmor("Helmet", helmetIdSync.Value);
            visuals.SetArmor("Chestplate", chestplateIdSync.Value);
            visuals.SetArmor("Leggings", leggingsIdSync.Value);
            visuals.SetArmor("Boots", bootsIdSync.Value);
            visuals.SetArmor("Jetbag", jetbagIdSync.Value);
        }

        debugStatus = "READY";
        if (rb != null) { rb.bodyType = RigidbodyType2D.Dynamic; rb.simulated = true; rb.gravityScale = 3f; }
        if (IsOwner && Camera.main != null) Camera.main.GetComponent<CameraController>()?.SetTarget(transform);
    }

    public void SyncInventoryToNetwork()
    {
        if (!IsServer || playerData == null) return;
        for (int i = 0; i < inventorySlotsSync.Count; i++)
        {
            var localSlot = playerData.inventory.GetSlot(i);
            if (!inventorySlotsSync[i].Equals(localSlot)) inventorySlotsSync[i] = localSlot;
        }
    }

    [ServerRpc] public void RequestSyncInventoryServerRpc() => SyncInventoryToNetwork();
    [ServerRpc] private void RequestSpawnServerRpc() => ConfirmSpawnClientRpc(MapManager.Instance.GetSurfacePosition(50f));
    [ClientRpc] private void ConfirmSpawnClientRpc(Vector2 pos) { if (IsOwner) { transform.position = pos; rb.position = pos; debugStatus = "Spawned"; } }

    #endregion

    #region Update & Visuals

    private void Update()
    {
        if (!IsOwner)
        {
            // [Optimization] Calculate proxy speed based on position change (since linearVelocity doesn't sync by default)
            float deltaX = transform.position.x - lastPosition.x;
            proxySpeedX = deltaX / Time.deltaTime;
            lastPosition = transform.position;

            visuals?.SetFlip(isFlippedSync.Value);
            // Use calculated proxySpeedX for animation
            visuals?.UpdateVisuals(proxySpeedX, isGroundedSync.Value, isDashingSync.Value);
            return;
        }

        // Owner Input & Timers
        lastPosition = transform.position; // Keep track for consistency
        moveInput = moveAction.ReadValue<Vector2>();
        moveInputSync.Value = moveInput;
        movement.Tick();
        
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        if (debugStatus != "READY" || !IsOwner) return;

        movement.FixedTick(moveInput);

        // Jump Physics logic (Sync trigger)
        if (jumpCountSync.Value > lastProcessedJumpCount)
        {
            if (movement.IsGrounded) movement.ApplyJump();
            lastProcessedJumpCount = jumpCountSync.Value;
        }
    }

    private void UpdateVisuals()
    {
        if (visuals == null) return;

        if (Mathf.Abs(moveInput.x) > 0.01f && !isDashingSync.Value)
        {
            isFlippedSync.Value = moveInput.x < 0;
            visuals.SetFlip(isFlippedSync.Value);
        }

        visuals.UpdateVisuals(rb.linearVelocity.x, movement.IsGrounded, isDashingSync.Value);
    }

    public void OnGroundedChanged(bool grounded) { if (IsOwner) isGroundedSync.Value = grounded; }

    #endregion

    #region Interaction & ServerRpc

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = pointAction.ReadValue<Vector2>();
        
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        
        return results.Count > 0;
    }

    private void OnInteract00Performed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;
        if (IsPointerOverUI()) return; // [Fix] 수동 레이캐스트로 UI 체크
        interaction.UseItem(0, selectedHotbarIndex, pointAction.ReadValue<Vector2>());
    }

    private void OnInteract01Performed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;
        if (IsPointerOverUI()) return; // [Fix] 수동 레이캐스트로 UI 체크
        interaction.UseItem(1, selectedHotbarIndex, pointAction.ReadValue<Vector2>());
    }

    [ServerRpc] public void UpdateBlockServerRpc(int x, int y, int id) { MapManager.Instance.SetBlock(x, y, id); }

    [ServerRpc]
    public void DropItemServerRpc(int id, int count, float lookDir)
    {
        Debug.Log($"<color=red>[RPC-RECEIVE]</color> DropItemServerRpc received on Server! ID: {id}, Count: {count}");
        interaction.HandleDropItem(id, count, lookDir);
    }

    public void EndDash() { if (IsOwner) isDashingSync.Value = false; }

    #endregion

    #region Data Helpers

    public void UpdateAppearance(PlayerVisualData v)
    {
        if (!IsOwner) return;
        if (ColorUtility.TryParseHtmlString(v.skinColorHex, out Color s)) skinColorSync.Value = s;
        if (ColorUtility.TryParseHtmlString(v.eyeColorHex, out Color e)) eyeColorSync.Value = e;
        if (ColorUtility.TryParseHtmlString(v.hairColorHex, out Color h)) hairColorSync.Value = h;
        hairStyleSync.Value = v.hairStyleIndex;
    }

    public void UpdateEquipment(PlayerEquipmentData e)
    {
        if (!IsOwner) return;
        helmetIdSync.Value = e.helmetIndex;
        chestplateIdSync.Value = e.chestplateIndex;
        leggingsIdSync.Value = e.leggingsIndex;
        bootsIdSync.Value = e.bootsIndex;
        jetbagIdSync.Value = e.jetbagIndex;
    }

    private void EnsureEventSystem() { if (Object.FindAnyObjectByType<EventSystem>() == null) { new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); } }

    private void OnGUI()
    {
        if (!IsOwner) return;
        GUI.color = Color.black;
        GUILayout.BeginArea(new Rect(15, 15, 300, 250));
        GUILayout.Label($"<b>[PLAYER STATUS]</b>");
        GUILayout.Label($"Status: {debugStatus}");
        GUILayout.Label($"IsGrounded: {movement.IsGrounded}");
        GUILayout.Label($"Dashing: {isDashingSync.Value}");
        GUILayout.EndArea();
    }
    #endregion
}
