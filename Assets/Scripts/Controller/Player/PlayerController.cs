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

    // Animation Proxy Data
    private Vector3 lastPosition;
    private float proxySpeedX;

    // Item Use Delay
    private float itemUseDelayTimer = 0f;
    public float ItemUseDelayTimer => itemUseDelayTimer;

    // [Server-Only] Anti-Cheat Cooldown
    private float serverLastActionTime = 0f;

    #endregion

    #region Helper Methods

    public float GetItemUseDelay(ItemData data)
    {
        if (data == null) return 0.2f;

        // 1. 무기/도구 데이터가 있다면 해당 데이터의 UseTime(1/Speed)을 우선 사용
        if (data.weaponStats != null && data.weaponStats.speed > 0)
        {
            return data.weaponStats.UseTime;
        }

        // 2. 데이터가 없는 경우 타입별 기본값 반환
        switch (data.type)
        {
            case ItemType.Block:
                return 0.2f;

            case ItemType.Helmet:
            case ItemType.Chestplate:
            case ItemType.Leggings:
            case ItemType.Boots:
            case ItemType.Jetbag:
                return 0.2f;

            case ItemType.Consumable: 
                return 0.5f;

            case ItemType.Weapon:
            case ItemType.Tool:
                return 0.4f;

            default: 
                return 0.2f;
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

    // Ghost Item Sync (Server-side dragging item)
    private NetworkVariable<PlayerInventorySlotData> ghostItemSync = new NetworkVariable<PlayerInventorySlotData>(
        new PlayerInventorySlotData(-1, 0),
        NetworkVariableReadPermission.Owner,
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
            RefreshHeldItem();
        }
    }

    #endregion

    #region Network Logic

    public override void OnNetworkSpawn()
    {
        playerData = new PlayerData();
        if (IsOwner) Local = this;
        inventorySlotsSync.OnListChanged += OnInventoryListChanged;

        if (!IsServer && inventorySlotsSync.Count > 0)
        {
            for (int i = 0; i < inventorySlotsSync.Count; i++)
                playerData.inventory.SetSlotWithoutNotify(i, inventorySlotsSync[i]);
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
        if (!IsServer) playerData.inventory.SetSlot(changeEvent.Index, changeEvent.Value);
        if (IsOwner && changeEvent.Index == selectedHotbarIndex) RefreshHeldItem();
    }

    public void RefreshHeldItem()
    {
        if (!IsOwner || playerData == null || playerData.inventory == null) return;
        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        int itemID = slot.IsEmpty ? -1 : slot.itemID;
        UpdateHeldItemRpc(itemID);
        if (visuals != null) visuals.SetHeldItem(itemID);
    }

    [Rpc(SendTo.Server)]
    public void UpdateHeldItemRpc(int itemID) => heldItemIdSync.Value = itemID;

    private IEnumerator InitPlayerCo()
    {
        debugStatus = "Waiting for Map...";
        if (rb != null) { rb.simulated = false; rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0; }
        if (visuals != null) visuals.gameObject.SetActive(false);

        while (MapManager.Instance == null || !MapManager.Instance.IsMapReady()) yield return null;
        
        int dbRetry = 0;
        while (ItemDataManager.Instance.GetItem(9) == null && dbRetry < 20) { yield return new WaitForSeconds(0.1f); dbRetry++; }

        if (IsServer)
        {
            debugStatus = "Initializing Data (Server)...";
            inventorySlotsSync.Clear();
            for (int i = 0; i < 50; i++) inventorySlotsSync.Add(new PlayerInventorySlotData(-1, 0));
            for (int i = 0; i <= 9; i++) { ItemData item = ItemDataManager.Instance.GetItem(i); if (item != null) playerData.inventory.AddItem(i, item.maxStack); }
            for (int i = 0; i < inventorySlotsSync.Count; i++) inventorySlotsSync[i] = playerData.inventory.GetSlot(i);
            yield return new WaitForEndOfFrame();
        }

        if (IsOwner)
        {
            debugStatus = "Requesting Spawn...";
            RequestSpawnRpc();
            while (debugStatus == "Requesting Spawn...") yield return null;

            if (MeshManager.Instance != null) MeshManager.Instance.SetTarget(transform);

            debugStatus = "Building Terrain...";
            float terrainWaitStartTime = Time.time;
            while (!MapManager.Instance.IsTerrainReadyAt(rb.position) && Time.time - terrainWaitStartTime < 5f) yield return new WaitForSeconds(0.1f);

            UpdateAppearance(playerData.visual);
            var e = playerData.equipment;
            UpdateEquipmentRpc(e.helmetIndex, e.chestplateIndex, e.leggingsIndex, e.bootsIndex, e.jetbagIndex);
            
            InventoryUI ui = Object.FindAnyObjectByType<InventoryUI>();
            if (ui != null) ui.RefreshUI();

            float inventoryWaitTime = 0;
            while (playerData.inventory.GetSlot(selectedHotbarIndex).IsEmpty && inventoryWaitTime < 1.0f) { yield return new WaitForSeconds(0.1f); inventoryWaitTime += 0.1f; }
            RefreshHeldItem();
        }

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

        yield return new WaitForEndOfFrame();
        if (IsOwner) RefreshHeldItem();
        else if (heldItemIdSync.Value != -1) visuals.SetHeldItem(heldItemIdSync.Value);
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

    [Rpc(SendTo.Server)] public void RequestSyncInventoryRpc() => SyncInventoryToNetwork();

    [Rpc(SendTo.Server)] private void RequestSpawnRpc(RpcParams rpcParams = default) => ConfirmSpawnRpc(MapManager.Instance.GetSurfacePosition(50f), RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    
    [Rpc(SendTo.SpecifiedInParams)] private void ConfirmSpawnRpc(Vector2 pos, RpcParams rpcParams = default) { if (IsOwner) { transform.position = pos; rb.position = pos; debugStatus = "Spawned"; } }

    #endregion

    #region Update & Visuals

    private void Update()
    {
        if (!IsOwner)
        {
            float deltaX = transform.position.x - lastPosition.x;
            proxySpeedX = deltaX / Time.deltaTime;
            lastPosition = transform.position;
            if (visuals != null && visuals.IsFlipped != isFlippedSync.Value) visuals.SetFlip(isFlippedSync.Value);
            visuals?.UpdateVisuals(proxySpeedX, isGroundedSync.Value, isDashingSync.Value);
            return;
        }

        if (itemUseDelayTimer > 0) itemUseDelayTimer -= Time.deltaTime;

        HandleContinuousInteraction();

        // [New] Update Block Placement Preview
        if (playerData != null && playerData.inventory != null)
        {
            var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
            int heldItemID = slot.IsEmpty ? -1 : slot.itemID;
            Vector2 screenPos = pointAction.ReadValue<Vector2>();
            Vector2 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
            interaction.UpdatePlacementPreview(heldItemID, worldPos, IsPointerOverUI());
        }

        lastPosition = transform.position;
        moveInput = moveAction.ReadValue<Vector2>();
        moveInputSync.Value = moveInput;
        movement.Tick();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        if (debugStatus != "READY" || !IsOwner) return;
        movement.FixedTick(moveInput);
        if (jumpCountSync.Value > lastProcessedJumpCount)
        {
            if (movement.IsGrounded) movement.ApplyJump();
            lastProcessedJumpCount = jumpCountSync.Value;
        }
    }

    private void UpdateVisuals()
    {
        if (visuals == null) return;

        bool newFlip = isFlippedSync.Value;

        // 1. 대시 중인 경우: 대시 방향을 바라봄 (마우스 무시)
        if (isDashingSync.Value)
        {
            newFlip = dashDirectionSync.Value < 0;
        }
        else
        {
            // 2. 평상시: 마우스 위치를 기준으로 방향 결정
            Vector2 screenPos = pointAction.ReadValue<Vector2>();
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));

            // 플레이어보다 마우스가 왼쪽에 있으면 Flip (true)
            newFlip = mouseWorldPos.x < transform.position.x;
        }

        // 방향이 바뀌었을 때만 동기화 및 비주얼 업데이트
        if (isFlippedSync.Value != newFlip)
        {
            isFlippedSync.Value = newFlip;
            visuals.SetFlip(newFlip);
        }

        visuals.UpdateVisuals(rb.linearVelocity.x, movement.IsGrounded, isDashingSync.Value);
    }

    public void OnGroundedChanged(bool grounded) { if (IsOwner) isGroundedSync.Value = grounded; }

    #endregion

    #region Interaction & Rpc

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        PointerEventData eventData = new PointerEventData(EventSystem.current) { position = pointAction.ReadValue<Vector2>() };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    private void HandleContinuousInteraction()
    {
        // 1. 기본 조건 체크 (Owner 여부, 준비 완료 상태)
        if (!IsOwner || debugStatus != "READY") 
        {
            if (visuals != null) visuals.StopItemUseAnimation();
            return;
        }

        // 2. 현재 상태 판단: 버튼을 누르고 있거나, 아직 이전 아이템 사용의 딜레이(UseTime)가 남았는가?
        bool isButtonPressed = !IsPointerOverUI() && (interactAction.IsPressed() || interact01Action.IsPressed());
        bool isStillActive = isButtonPressed || itemUseDelayTimer > 0;
        
        if (!isStillActive)
        {
            if (visuals != null) visuals.StopItemUseAnimation();
            return;
        }

        // 3. 마우스 각도 계산 (애니메이션 유지를 위해 계속 갱신)
        Vector2 screenPos = pointAction.ReadValue<Vector2>();
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));
        Vector2 dir = (mouseWorldPos - transform.position).normalized;
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (IsFlipped) targetAngle = 180f - targetAngle;
        targetAngle += 90f; 

        // 4. 애니메이션 시작/갱신 요청 (아이템 데이터 기반)
        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        if (!slot.IsEmpty)
        {
            var itemData = ItemDataManager.Instance.GetItem(slot.itemID);
            if (itemData != null)
            {
                // [Fix] 블록 타입인 경우에만 휘두르기 애니메이션 실행 (실시간 각도 사용)
                if (itemData.type == ItemType.Block)
                {
                    float duration = GetItemUseDelay(itemData);
                    visuals.StartItemUseAnimation(targetAngle, duration);
                }
                else
                {
                    // 블록이 아니면 애니메이션 중단
                    visuals.StopItemUseAnimation();
                }
            }
        }

        // 5. 실제 상호작용 실행 (버튼이 눌려있고 타이머가 끝났을 때만)
        if (isButtonPressed && itemUseDelayTimer <= 0)
        {
            if (interactAction.IsPressed()) PerformWorldInteraction(0);
            else if (interact01Action.IsPressed()) PerformWorldInteraction(1);
        }
    }

    private void PerformWorldInteraction(int buttonIndex)
    {
        if (playerData == null || playerData.inventory == null) return;

        var slot = playerData.inventory.GetSlot(selectedHotbarIndex);
        if (slot.IsEmpty) return;

        ItemData itemData = ItemDataManager.Instance.GetItem(slot.itemID);
        if (itemData == null) return;

        // 딜레이 설정
        itemUseDelayTimer = GetItemUseDelay(itemData);

        // 월드 좌표 계산 (애니메이션에서 쓴 값을 재활용해도 됨)
        Vector2 screenPos = pointAction.ReadValue<Vector2>();
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -Camera.main.transform.position.z));

        // 실제 상호작용 실행
        interaction.UseItem(buttonIndex, selectedHotbarIndex, worldPos);
    }

    // [Note] OnInteractPerformed 이벤트들은 이제 UI 드래그 앤 드롭 등 1회성 이벤트를 위해서만 남겨두거나 
    // 혹은 Update 방식과 충돌하지 않도록 내용을 비웁니다. 
    // 여기서는 연속 설치를 위해 Update 방식으로 완전히 이전합니다.
    private void OnInteract00Performed(InputAction.CallbackContext ctx) { }
    private void OnInteract01Performed(InputAction.CallbackContext ctx) { }

    [Rpc(SendTo.Server)]
    public void QuickEquipRpc(int hotbarIndex)
    {
        if (playerData == null || playerData.inventory == null || playerData.equipment == null) return;

        var slot = playerData.inventory.GetSlot(hotbarIndex);
        if (slot.IsEmpty) return;

        ItemData itemData = ItemDataManager.Instance.GetItem(slot.itemID);
        if (itemData == null || !IsEquipmentType(itemData.type)) return;

        // 1. 현재 장착된 아이템 정보 가져오기
        int oldTypeID = playerData.equipment.GetEquipment(itemData.type);
        int oldItemID = ItemDataManager.Instance.FindItemIDByType(itemData.type, oldTypeID);

        // 2. 서버 데이터 갱신 (장착)
        SetEquipmentOnServer(itemData.type, itemData.typeID);

        // 3. 인벤토리 슬롯 교체
        if (oldItemID >= 0)
        {
            // 기존 장비가 있었다면 핫바 슬롯으로 돌려보냄
            playerData.inventory.SetSlot(hotbarIndex, new PlayerInventorySlotData(oldItemID, 1));
        }
        else
        {
            // 기존 장비가 없었다면 핫바 슬롯을 비움
            playerData.inventory.ClearSlot(hotbarIndex);
        }

        // 4. 동기화
        SyncInventoryToNetwork();
    }

    [Rpc(SendTo.Server)]
    public void PlaceBlockRpc(int x, int y, int itemID, int hotbarIndex)
    {
        ItemData itemData = ItemDataManager.Instance.GetItem(itemID);
        float delay = itemData != null ? GetItemUseDelay(itemData) : 0.2f;
        if (Time.time - serverLastActionTime < delay - 0.05f) return;

        Vector2 playerPos = transform.position;
        if (Mathf.Abs(x + 0.5f - playerPos.x) > 8.5f || Mathf.Abs(y + 0.5f - playerPos.y) > 6.5f) return;
        if (MapManager.Instance.IsBlockActive(x, y)) return;
        bool hasNeighbor = MapManager.Instance.IsBlockActive(x + 1, y) || MapManager.Instance.IsBlockActive(x - 1, y) || MapManager.Instance.IsBlockActive(x, y + 1) || MapManager.Instance.IsBlockActive(x, y - 1);
        if (!hasNeighbor) return;

        Vector2 checkPos = new Vector2(x + 0.5f, y + 0.5f);
        if (Physics2D.OverlapBox(checkPos, Vector2.one * 0.95f, 0f, LayerMask.GetMask("Player")) != null) return;

        var slot = playerData.inventory.GetSlot(hotbarIndex);
        if (slot.IsEmpty || slot.itemID != itemID) return;

        if (playerData.inventory.RemoveItemFromSlot(hotbarIndex, 1))
        {
            serverLastActionTime = Time.time;
            MapManager.Instance.SetBlock(x, y, itemID);
            SyncInventoryToNetwork();
        }
    }

    [Rpc(SendTo.Server)]
    public void InteractSlotRpc(int index, int buttonIndex, bool isModifier)
    {
        if (playerData == null || playerData.inventory == null) return;
        var inventory = playerData.inventory;
        var clickedSlot = inventory.GetSlot(index);
        var draggingData = ghostItemSync.Value;

        if (buttonIndex == 0)
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
                if (clickedSlot.IsEmpty) { inventory.SetSlot(index, draggingData); ghostItemSync.Value = new PlayerInventorySlotData(-1, 0); }
                else if (clickedSlot.itemID == draggingData.itemID)
                {
                    ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    int max = data != null ? data.maxStack : 999;
                    int toAdd = Mathf.Min(max - clickedSlot.stackCount, draggingData.stackCount);
                    inventory.SetSlot(index, new PlayerInventorySlotData(clickedSlot.itemID, clickedSlot.stackCount + toAdd));
                    draggingData.stackCount -= toAdd;
                    ghostItemSync.Value = draggingData.stackCount <= 0 ? new PlayerInventorySlotData(-1, 0) : draggingData;
                }
                else { PlayerInventorySlotData temp = clickedSlot; inventory.SetSlot(index, draggingData); ghostItemSync.Value = temp; }
            }
        }
        else if (buttonIndex == 1)
        {
            if (draggingData.IsEmpty)
            {
                if (!clickedSlot.IsEmpty)
                {
                    ItemData itemData = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    if (itemData != null && IsEquipmentType(itemData.type))
                    {
                        int oldTypeID = playerData.equipment.GetEquipment(itemData.type);
                        int oldItemID = ItemDataManager.Instance.FindItemIDByType(itemData.type, oldTypeID);
                        SetEquipmentOnServer(itemData.type, itemData.typeID);
                        if (oldItemID >= 0) inventory.SetSlot(index, new PlayerInventorySlotData(oldItemID, 1));
                        else inventory.ClearSlot(index);
                        SyncInventoryToNetwork(); return;
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
                if (clickedSlot.IsEmpty) { inventory.SetSlot(index, draggingData); ghostItemSync.Value = new PlayerInventorySlotData(-1, 0); }
                else if (clickedSlot.itemID == draggingData.itemID)
                {
                    ItemData data = ItemDataManager.Instance.GetItem(clickedSlot.itemID);
                    int max = data != null ? data.maxStack : 999;
                    if (draggingData.stackCount < max)
                    {
                        int toPick = Mathf.Min(max - draggingData.stackCount, isModifier ? Mathf.Min(10, clickedSlot.stackCount) : 1);
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
                else { PlayerInventorySlotData temp = clickedSlot; inventory.SetSlot(index, draggingData); ghostItemSync.Value = temp; }
            }
        }
        SyncInventoryToNetwork();
    }

    [Rpc(SendTo.Server)]
    public void InteractEquipmentSlotRpc(ItemType type, int buttonIndex, bool isModifier)
    {
        if (playerData == null || playerData.equipment == null) return;
        var draggingData = ghostItemSync.Value;
        int currentTypeID = playerData.equipment.GetEquipment(type);
        int currentItemID = ItemDataManager.Instance.FindItemIDByType(type, currentTypeID);
        PlayerInventorySlotData currentSlotData = new PlayerInventorySlotData(currentItemID, currentItemID >= 0 ? 1 : 0);

        if (buttonIndex == 0)
        {
            if (draggingData.IsEmpty) { if (!currentSlotData.IsEmpty) { ghostItemSync.Value = currentSlotData; SetEquipmentOnServer(type, -1); } }
            else
            {
                ItemData draggingItemData = ItemDataManager.Instance.GetItem(draggingData.itemID);
                if (draggingItemData != null && draggingItemData.type == type) { SetEquipmentOnServer(type, draggingItemData.typeID); ghostItemSync.Value = currentSlotData; }
            }
        }
        else if (buttonIndex == 1 && draggingData.IsEmpty && !currentSlotData.IsEmpty)
        {
            if (playerData.inventory.AddItem(currentItemID, 1) == 0) { SetEquipmentOnServer(type, -1); SyncInventoryToNetwork(); }
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

    private bool IsEquipmentType(ItemType type) => type == ItemType.Helmet || type == ItemType.Chestplate || type == ItemType.Leggings || type == ItemType.Boots || type == ItemType.Jetbag;

    [Rpc(SendTo.Server)]
    public void DropItemRpc(int id, int count, float lookDir, RpcParams rpcParams = default)
    {
        if (ghostItemSync.Value.itemID == id && ghostItemSync.Value.stackCount >= count)
        {
            interaction.HandleDropItem(id, count, lookDir);
            var updatedGhost = ghostItemSync.Value;
            updatedGhost.stackCount -= count;
            ghostItemSync.Value = updatedGhost.stackCount <= 0 ? new PlayerInventorySlotData(-1, 0) : updatedGhost;
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

    [Rpc(SendTo.Server)]
    public void UpdateEquipmentRpc(int helmet, int chest, int leggings, int boots, int jetbag)
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
