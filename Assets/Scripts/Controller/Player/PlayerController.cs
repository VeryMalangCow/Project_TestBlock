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

    // [New] Item Use Delay
    private float itemUseDelayTimer = 0f;
    public float ItemUseDelayTimer => itemUseDelayTimer;

    // [Server-Only] Anti-Cheat Cooldown
    private float serverLastActionTime = 0f;

    #endregion

    #region Helper Methods

    public float GetItemUseDelay(ItemType type)
    {
        switch (type)
        {
            case ItemType.Block: return 0.2f;
            case ItemType.Helmet:
            case ItemType.Chestplate:
            case ItemType.Leggings:
            case ItemType.Boots:
            case ItemType.Jetbag:
                return 2.0f;
            case ItemType.Consumable: return 0.5f;
            case ItemType.Sword:
            case ItemType.Tool:
                return 0.4f;
            default: return 0.2f;
        }
    }

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
    private NetworkVariable<int> helmetIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> chestplateIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> leggingsIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> bootsIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> jetbagIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Color> skinColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> eyeColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Color> hairColorSync = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> hairStyleSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> heldItemIdSync = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // [New] 서버에서 관리하는 마우스 드래깅 아이템 (유령 아이템)
    private NetworkVariable<PlayerInventorySlotData> ghostItemSync = new NetworkVariable<PlayerInventorySlotData>(
        new PlayerInventorySlotData(-1, 0),
        NetworkVariableReadPermission.Owner, // 본인만 알면 됨
        NetworkVariableWritePermission.Server
    );

    public PlayerInventorySlotData GhostItem => ghostItemSync.Value;

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
                    action.performed += _ => { if (IsOwner && itemUseDelayTimer <= 0) { selectedHotbarIndex = index; RefreshHeldItem(); } };
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
        if (!IsOwner || itemUseDelayTimer > 0) return;
        float scroll = ctx.ReadValue<Vector2>().y;
        if (Mathf.Abs(scroll) > 0.1f)
        {
            if (scroll > 0) selectedHotbarIndex = (selectedHotbarIndex - 1 + 10) % 10;
            else selectedHotbarIndex = (selectedHotbarIndex + 1) % 10;
            
            // [New] 핫바 선택 변경 시 들고 있는 아이템 갱신
            RefreshHeldItem();
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

        if (!IsServer)
        {
            // [Important] NetworkList는 스폰 시점에 이미 데이터가 있을 수 있으므로 초기 수동 동기화 수행
            if (inventorySlotsSync.Count > 0)
            {
                for (int i = 0; i < inventorySlotsSync.Count; i++)
                {
                    playerData.inventory.SetSlotWithoutNotify(i, inventorySlotsSync[i]);
                }
            }
        }

        helmetIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Helmet", newVal); playerData.equipment.helmetIndex = newVal; Object.FindAnyObjectByType<InventoryUI>()?.RefreshEquipmentUI(); };
        chestplateIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Chestplate", newVal); playerData.equipment.chestplateIndex = newVal; Object.FindAnyObjectByType<InventoryUI>()?.RefreshEquipmentUI(); };
        leggingsIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Leggings", newVal); playerData.equipment.leggingsIndex = newVal; Object.FindAnyObjectByType<InventoryUI>()?.RefreshEquipmentUI(); };
        bootsIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Boots", newVal); playerData.equipment.bootsIndex = newVal; Object.FindAnyObjectByType<InventoryUI>()?.RefreshEquipmentUI(); };
        jetbagIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Jetbag", newVal); playerData.equipment.jetbagIndex = newVal; Object.FindAnyObjectByType<InventoryUI>()?.RefreshEquipmentUI(); };

        skinColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetSkinColor(newVal);
        eyeColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetEyeColor(newVal);
        hairColorSync.OnValueChanged += (oldVal, newVal) => visuals.SetHairColor(newVal);
        hairStyleSync.OnValueChanged += (oldVal, newVal) => visuals.SetHair(newVal);
        heldItemIdSync.OnValueChanged += (oldVal, newVal) => visuals.SetHeldItem(newVal);

        StartCoroutine(InitPlayerCo());
    }

    private void OnInventoryListChanged(NetworkListEvent<PlayerInventorySlotData> changeEvent)
    {
        if (playerData == null || playerData.inventory == null) return;

        // [Fix] 서버(호스트)인 경우에도 본인의 데이터라면 갱신 로직을 수행해야 합니다.
        if (!IsServer)
        {
            playerData.inventory.SetSlot(changeEvent.Index, changeEvent.Value);
        }

        // [Key] 현재 선택된 핫바 슬롯의 아이템이 바뀌었다면 (들어올림, 교체, 배치 등) 
        // 즉시 들고 있는 아이템을 다시 확인하여 동기화합니다.
        if (IsOwner)
        {
            switch (changeEvent.Type)
            {
                case NetworkListEvent<PlayerInventorySlotData>.EventType.Add:
                case NetworkListEvent<PlayerInventorySlotData>.EventType.Value:
                case NetworkListEvent<PlayerInventorySlotData>.EventType.Remove:
                    if (changeEvent.Index == selectedHotbarIndex)
                    {
                        RefreshHeldItem();
                    }
                    break;
            }
        }
    }
    public void RefreshHeldItem()
    {
        if (!IsOwner || playerData == null || playerData.inventory == null) return;

        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        int itemID = slot.IsEmpty ? -1 : slot.itemID;
        
        // 서버에 현재 들고 있는 아이템 ID 업데이트 요청
        UpdateHeldItemServerRpc(itemID);
        
        // [Fix] Owner는 네트워크 변수의 변경 여부와 상관없이 즉시 본인의 비주얼을 업데이트합니다.
        // (값이 이미 0일 경우 OnValueChanged가 호출되지 않아 비주얼이 안 나타날 수 있기 때문)
        if (visuals != null) visuals.SetHeldItem(itemID);
    }

    [ServerRpc]
    public void UpdateHeldItemServerRpc(int itemID)
    {
        heldItemIdSync.Value = itemID;
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
            
            // [New] 서버가 데이터를 넣은 후 클라이언트(본인 포함)에게 동기화될 시간을 아주 잠깐 줍니다.
            yield return new WaitForEndOfFrame();
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
            UpdateAppearance(playerData.visual);
            var e = playerData.equipment;
            UpdateEquipmentServerRpc(e.helmetIndex, e.chestplateIndex, e.leggingsIndex, e.bootsIndex, e.jetbagIndex);
            
            // 데이터가 모두 준비된 후 UI 갱신 강제
            InventoryUI ui = Object.FindAnyObjectByType<InventoryUI>();
            if (ui != null) ui.RefreshUI();

            // [Fix] 인벤토리가 비어있다면 잠시 기다림 (동기화 대기)
            float inventoryWaitTime = 0;
            while (playerData.inventory.GetSlot(selectedHotbarIndex).IsEmpty && inventoryWaitTime < 1.0f)
            {
                yield return new WaitForSeconds(0.1f);
                inventoryWaitTime += 0.1f;
            }

            // 초기 들고 있는 아이템 동기화
            RefreshHeldItem();
        }

        // [Visual Initialization]
        if (visuals != null)
        {
            visuals.gameObject.SetActive(true);
            visuals.Init(); // [Key] 여기서 isVisualsReady = true 및 렌더러 비활성화 처리됨
            
            // 기본 외형 및 장비 초기화 (값만 설정)
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

        // [Final Stage] 모든 준비 완료
        debugStatus = "READY";
        if (rb != null) { rb.bodyType = RigidbodyType2D.Dynamic; rb.simulated = true; rb.gravityScale = 3f; }
        if (IsOwner && Camera.main != null) Camera.main.GetComponent<CameraController>()?.SetTarget(transform);

        // [New] 캐릭터 로드가 완전히 끝난 후, 들고 있는 아이템 시각화 강제 실행
        yield return new WaitForEndOfFrame(); // 시스템이 한 프레임 완전히 안정화될 때까지 대기

        if (IsOwner)
        {
            // Owner는 인벤토리 데이터가 서버로부터 확실히 도착할 때까지 대기
            float inventoryWait = 0;
            while (playerData.inventory.GetSlot(selectedHotbarIndex).IsEmpty && inventoryWait < 2.0f)
            {
                yield return new WaitForSeconds(0.1f);
                inventoryWait += 0.1f;
            }
            
            // 인벤토리가 비어있지 않거나 타임아웃 되면 갱신 실행
            RefreshHeldItem(); 
        }
        else
        {
            // Proxy(타인)는 서버에서 이미 동기화된 값을 즉시 시각화에 적용
            if (heldItemIdSync.Value != -1)
            {
                visuals.SetHeldItem(heldItemIdSync.Value);
            }
        }
    }

    public void SyncInventoryToNetwork()
    {
        if (!IsServer || playerData == null) return;
        
        // [Fix] 현재 리스트 상태를 로컬 데이터와 비교하여 필요한 경우에만 갱신
        for (int i = 0; i < inventorySlotsSync.Count; i++)
        {
            var localSlot = playerData.inventory.GetSlot(i);
            if (!inventorySlotsSync[i].Equals(localSlot))
            {
                inventorySlotsSync[i] = localSlot;
            }
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

            // [Fix] 방향이 바뀔 때만 SetFlip 호출 (아이템 떨림 방지)
            if (visuals != null && visuals.IsFlipped != isFlippedSync.Value)
            {
                visuals.SetFlip(isFlippedSync.Value);
            }

            // Use calculated proxySpeedX for animation
            visuals?.UpdateVisuals(proxySpeedX, isGroundedSync.Value, isDashingSync.Value);
            return;
        }

        // Owner Input & Timers
        if (itemUseDelayTimer > 0) itemUseDelayTimer -= Time.deltaTime;

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
            bool newFlip = moveInput.x < 0;
            if (isFlippedSync.Value != newFlip)
            {
                isFlippedSync.Value = newFlip;
                visuals.SetFlip(newFlip);
            }
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
        if (!IsOwner || itemUseDelayTimer > 0) return;
        if (IsPointerOverUI()) return;

        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        if (!slot.IsEmpty)
        {
            var itemData = ItemDataManager.Instance.GetItem(slot.itemID);
            if (itemData != null) itemUseDelayTimer = GetItemUseDelay(itemData.type);
        }

        Vector2 screenPos = pointAction.ReadValue<Vector2>();
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
        interaction.UseItem(0, selectedHotbarIndex, worldPos);
    }

    private void OnInteract01Performed(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || itemUseDelayTimer > 0) return;
        if (IsPointerOverUI()) return;

        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        if (!slot.IsEmpty)
        {
            var itemData = ItemDataManager.Instance.GetItem(slot.itemID);
            if (itemData != null) itemUseDelayTimer = GetItemUseDelay(itemData.type);
        }

        Vector2 screenPos = pointAction.ReadValue<Vector2>();
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
        interaction.UseItem(1, selectedHotbarIndex, worldPos);
    }

    [ServerRpc]
    public void PlaceBlockServerRpc(int x, int y, int itemID, int hotbarIndex)
    {
        // 1. Cooldown Check
        ItemData itemData = ItemDataManager.Instance.GetItem(itemID);
        float delay = itemData != null ? GetItemUseDelay(itemData.type) : 0.2f;
        if (Time.time - serverLastActionTime < delay - 0.05f) return;

        // 2. Distance Check (Server-side)
        Vector2 playerPos = transform.position;
        if (Mathf.Abs(x + 0.5f - playerPos.x) > 8.5f || Mathf.Abs(y + 0.5f - playerPos.y) > 6.5f) return;

        // 3. Adjacency Check
        if (MapManager.Instance.IsBlockActive(x, y)) return;
        bool hasNeighbor = MapManager.Instance.IsBlockActive(x + 1, y) ||
                           MapManager.Instance.IsBlockActive(x - 1, y) ||
                           MapManager.Instance.IsBlockActive(x, y + 1) ||
                           MapManager.Instance.IsBlockActive(x, y - 1);
        if (!hasNeighbor) return;

        // 4. Inventory Validation & Consumption
        var slot = playerData.inventory.GetSlot(hotbarIndex);
        if (slot.IsEmpty || slot.itemID != itemID) return;

        if (playerData.inventory.RemoveItemFromSlot(hotbarIndex, 1))
        {
            serverLastActionTime = Time.time;
            MapManager.Instance.SetBlock(x, y, itemID);
            SyncInventoryToNetwork();
        }
    }

    [ServerRpc]
    public void InteractSlotServerRpc(int index, int buttonIndex, bool isModifier)
    {
        if (playerData == null || playerData.inventory == null) return;
        var inventory = playerData.inventory;
        var clickedSlot = inventory.GetSlot(index);
        var draggingData = ghostItemSync.Value;

        if (buttonIndex == 0) // Interact_00 (Left Click)
        {
            if (draggingData.IsEmpty)
            {
                if (!clickedSlot.IsEmpty)
                {
                    int amountToPick = isModifier ? Mathf.CeilToInt(clickedSlot.stackCount / 2.0f) : clickedSlot.stackCount;
                    ghostItemSync.Value = new PlayerInventorySlotData(clickedSlot.itemID, amountToPick);
                    
                    int remaining = clickedSlot.stackCount - amountToPick;
                    if (remaining <= 0) inventory.ClearSlot(index);
                    else inventory.SetSlot(index, new PlayerInventorySlotData(clickedSlot.itemID, remaining));
                }
            }
            else
            {
                if (clickedSlot.IsEmpty)
                {
                    inventory.SetSlot(index, draggingData);
                    ghostItemSync.Value = new PlayerInventorySlotData(-1, 0);
                }
                else if (clickedSlot.itemID == draggingData.itemID)
                {
                    ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    int max = data != null ? data.maxStack : 999;
                    int canAdd = max - clickedSlot.stackCount;
                    int toAdd = Mathf.Min(canAdd, draggingData.stackCount);
                    
                    inventory.SetSlot(index, new PlayerInventorySlotData(clickedSlot.itemID, clickedSlot.stackCount + toAdd));
                    
                    draggingData.stackCount -= toAdd;
                    if (draggingData.stackCount <= 0) ghostItemSync.Value = new PlayerInventorySlotData(-1, 0);
                    else ghostItemSync.Value = draggingData;
                }
                else
                {
                    // Swap
                    PlayerInventorySlotData temp = clickedSlot;
                    inventory.SetSlot(index, draggingData);
                    ghostItemSync.Value = temp;
                }
            }
        }
        else if (buttonIndex == 1) // Interact_01 (Right Click)
        {
            if (draggingData.IsEmpty)
            {
                if (!clickedSlot.IsEmpty)
                {
                    // [Added] Auto-Equip Check
                    ItemData itemData = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    if (itemData != null && IsEquipmentType(itemData.type))
                    {
                        int oldTypeID = playerData.equipment.GetEquipment(itemData.type);
                        int oldItemID = ItemDataManager.Instance.FindItemIDByType(itemData.type, oldTypeID);
                        
                        SetEquipmentOnServer(itemData.type, itemData.typeID);
                        
                        if (oldItemID >= 0) inventory.SetSlot(index, new PlayerInventorySlotData(oldItemID, 1));
                        else inventory.ClearSlot(index);
                        
                        SyncInventoryToNetwork();
                        return;
                    }

                    int amountToPick = isModifier ? Mathf.Min(10, clickedSlot.stackCount) : 1;
                    ghostItemSync.Value = new PlayerInventorySlotData(clickedSlot.itemID, amountToPick);
                    
                    int remaining = clickedSlot.stackCount - amountToPick;
                    if (remaining <= 0) inventory.ClearSlot(index);
                    else inventory.SetSlot(index, new PlayerInventorySlotData(clickedSlot.itemID, remaining));
                }
            }
            else
            {
                if (clickedSlot.IsEmpty)
                {
                    // Drop ALL
                    inventory.SetSlot(index, draggingData);
                    ghostItemSync.Value = new PlayerInventorySlotData(-1, 0);
                }
                else if (clickedSlot.itemID == draggingData.itemID)
                {
                    ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    int max = data != null ? data.maxStack : 999;
                    if (draggingData.stackCount < max)
                    {
                        int canPick = max - draggingData.stackCount;
                        int desiredPick = isModifier ? Mathf.Min(10, clickedSlot.stackCount) : 1;
                        int toPick = Mathf.Min(canPick, desiredPick);

                        if (toPick > 0)
                        {
                            draggingData.stackCount += toPick;
                            ghostItemSync.Value = draggingData;

                            int remaining = clickedSlot.stackCount - toPick;
                            if (remaining <= 0) inventory.ClearSlot(index);
                            else inventory.SetSlot(index, new PlayerInventorySlotData(clickedSlot.itemID, remaining));
                        }
                    }
                }
                else
                {
                    // Swap
                    PlayerInventorySlotData temp = clickedSlot;
                    inventory.SetSlot(index, draggingData);
                    ghostItemSync.Value = temp;
                }
            }
        }

        SyncInventoryToNetwork();
    }

    [ServerRpc]
    public void InteractEquipmentSlotServerRpc(ItemType type, int buttonIndex, bool isModifier)
    {
        if (playerData == null || playerData.equipment == null) return;
        
        var draggingData = ghostItemSync.Value;
        int currentTypeID = playerData.equipment.GetEquipment(type);
        int currentItemID = ItemDataManager.Instance.FindItemIDByType(type, currentTypeID);
        PlayerInventorySlotData currentSlotData = new PlayerInventorySlotData(currentItemID, currentItemID >= 0 ? 1 : 0);

        if (buttonIndex == 0) // Left Click
        {
            if (draggingData.IsEmpty)
            {
                if (!currentSlotData.IsEmpty)
                {
                    ghostItemSync.Value = currentSlotData;
                    SetEquipmentOnServer(type, -1);
                }
            }
            else
            {
                ItemData draggingItemData = ItemDataManager.Instance.GetItem(draggingData.itemID);
                if (draggingItemData != null && draggingItemData.type == type)
                {
                    SetEquipmentOnServer(type, draggingItemData.typeID);
                    ghostItemSync.Value = currentSlotData;
                }
            }
        }
        else if (buttonIndex == 1) // Right Click
        {
            if (draggingData.IsEmpty && !currentSlotData.IsEmpty)
            {
                if (playerData.inventory.AddItem(currentItemID, 1) == 0)
                {
                    SetEquipmentOnServer(type, -1);
                    SyncInventoryToNetwork();
                }
            }
        }
    }

    private void SetEquipmentOnServer(ItemType type, int typeID)
    {
        if (!IsServer) return;
        playerData.equipment.SetEquipment(type, typeID);
        switch (type)
        {
            case ItemType.Helmet: helmetIdSync.Value = typeID; break;
            case ItemType.Chestplate: chestplateIdSync.Value = typeID; break;
            case ItemType.Leggings: leggingsIdSync.Value = typeID; break;
            case ItemType.Boots: bootsIdSync.Value = typeID; break;
            case ItemType.Jetbag: jetbagIdSync.Value = typeID; break;
        }
    }

    private bool IsEquipmentType(ItemType type)
    {
        return type == ItemType.Helmet || type == ItemType.Chestplate || 
               type == ItemType.Leggings || type == ItemType.Boots || 
               type == ItemType.Jetbag;
    }

    [ServerRpc]
    public void DropItemServerRpc(int id, int count, float lookDir)
    {
        Debug.Log($"<color=red>[RPC-RECEIVE]</color> DropItemServerRpc received on Server! ID: {id}, Count: {count}");
        
        // [Fix] 버리는 아이템이 서버에서 관리하는 GhostItem과 일치하는지 확인 (보안 및 데이터 정합성)
        if (ghostItemSync.Value.itemID == id && ghostItemSync.Value.stackCount >= count)
        {
            interaction.HandleDropItem(id, count, lookDir);
            
            // GhostItem 수량 차감
            var updatedGhost = ghostItemSync.Value;
            updatedGhost.stackCount -= count;
            if (updatedGhost.stackCount <= 0) ghostItemSync.Value = new PlayerInventorySlotData(-1, 0);
            else ghostItemSync.Value = updatedGhost;
        }
        else
        {
            Debug.LogWarning($"[Drop-Server] Invalid drop request from client! Ghost: {ghostItemSync.Value.itemID}x{ghostItemSync.Value.stackCount}, Requested: {id}x{count}");
        }
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

    [ServerRpc]
    public void UpdateEquipmentServerRpc(int helmet, int chest, int leggings, int boots, int jetbag)
    {
        SetEquipmentOnServer(ItemType.Helmet, helmet);
        SetEquipmentOnServer(ItemType.Chestplate, chest);
        SetEquipmentOnServer(ItemType.Leggings, leggings);
        SetEquipmentOnServer(ItemType.Boots, boots);
        SetEquipmentOnServer(ItemType.Jetbag, jetbag);
    }

    private void EnsureEventSystem() { if (Object.FindAnyObjectByType<EventSystem>() == null) { new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); } }

    #endregion
}
