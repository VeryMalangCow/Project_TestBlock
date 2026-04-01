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
    [SerializeField] private int selectedBlockId = 0;
    [SerializeField] private float interactRange = 6f;

    private Vector2 moveInput;
    private bool isGrounded;
    private float walkCycleTime;
    [SerializeField] private float walkAnimSpeedMultiplier = 2.5f;

    private string debugStatus = "Initializing...";

    #endregion

    #region Network Sync Variables

    // 1. Movement Sync
    private NetworkVariable<Vector2> moveInputSync = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> isFlippedSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> currentFrameSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 2. State Sync (Important for Jump/Dash consistency)
    private NetworkVariable<bool> isDashingSync = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> dashDirectionSync = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> jumpCountSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private int lastProcessedJumpCount;

    // 3. Armor Sync
    private NetworkVariable<int> clothesIdSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> backpackIdSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> headIdSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<int> cloakIdSync = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    #endregion

    #region Awake & Setup

    private void Awake()
    {
        SetupPhysics();
        EnsureEventSystem();
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
        clothesIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Clothes", newVal); };
        backpackIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Backpacks", newVal); };
        headIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Heads", newVal); };
        cloakIdSync.OnValueChanged += (oldVal, newVal) => { visuals.SetArmor("Cloaks", newVal); };

        StartCoroutine(InitPlayerCo());
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

            clothesIdSync.Value = 0;
            backpackIdSync.Value = 0;
            headIdSync.Value = 0;
            cloakIdSync.Value = 0;
        }

        if (!IsServer || IsOwner)
        {
            debugStatus = "Rendering Local Chunks...";
            int retry = 0;
            while (retry < 100) 
            {
                int cx = Mathf.FloorToInt(transform.position.x / ChunkData.ChunkSize.x);
                int cy = Mathf.FloorToInt(transform.position.y / ChunkData.ChunkSize.y);
                if (MeshManager.Instance != null && MeshManager.Instance.IsChunkActive(cx, cy)) break;
                if (IsOwner && MeshManager.Instance != null) MeshManager.Instance.ForceRenderChunk(cx, cy);
                retry++;
                yield return new WaitForSeconds(0.1f);
            }
        }

        if (visuals != null)
        {
            visuals.gameObject.SetActive(true);
            visuals.Init();
            visuals.SetArmor("Clothes", clothesIdSync.Value);
            visuals.SetArmor("Backpacks", backpackIdSync.Value);
            visuals.SetArmor("Heads", headIdSync.Value);
            visuals.SetArmor("Cloaks", cloakIdSync.Value);
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

    [ServerRpc]
    private void RequestSpawnServerRpc()
    {
        Vector2 spawnPos = MapManager.Instance.GetSurfacePosition(50f);
        transform.position = spawnPos;
        rb.position = spawnPos;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 3f;

        var networkTransform = GetComponent<NetworkTransform>();
        if (networkTransform != null) networkTransform.Teleport(spawnPos, Quaternion.identity, transform.localScale);
        ConfirmSpawnClientRpc(spawnPos);
    }

    [ClientRpc]
    private void ConfirmSpawnClientRpc(Vector2 pos) { if (IsOwner) transform.position = pos; }

    #endregion

    #region Update Loops

    private void Update()
    {
        if (!IsOwner)
        {
            if (visuals != null)
            {
                visuals.SetFlip(isFlippedSync.Value);
                visuals.SyncAnimation(currentFrameSync.Value);
            }
            return;
        }

        HandleOwnerInput();
        HandleInteraction();
        UpdateTimers();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        if (debugStatus != "READY") return;

        if (IsOwner || IsServer)
        {
            CheckGrounded();

            // Handle Jump via Counter
            if (jumpCountSync.Value > lastProcessedJumpCount)
            {
                if (isGrounded) ApplyJumpPhysics();
                lastProcessedJumpCount = jumpCountSync.Value;
            }

            Vector2 activeInput = IsOwner ? moveInput : moveInputSync.Value;

            if (isDashingSync.Value)
            {
                HandleDash();
                HandleStepClimb(activeInput);
            }
            else
            {
                HandleHorizontalMovement(activeInput);
                HandleStepClimb(activeInput);
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
        int targetFrame = 0;
        if (isDashingSync.Value) targetFrame = 11;
        else if (!isGrounded) targetFrame = 10;
        else
        {
            float currentHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (currentHorizontalSpeed > 0.1f)
            {
                walkCycleTime += Time.deltaTime * currentHorizontalSpeed * walkAnimSpeedMultiplier;
                targetFrame = Mathf.FloorToInt(walkCycleTime) % 10;
            }
        }
        currentFrameSync.Value = targetFrame;
        visuals.SyncAnimation(targetFrame);
    }

    private void UpdateTimers()
    {
        if (isDashingSync.Value)
        {
            dashTimeLeft -= Time.deltaTime;
            if (dashTimeLeft <= 0)
            {
                if (IsServer) isDashingSync.Value = false;
                rb.gravityScale = 3f;
            }
        }
        if (dashCooldownTimer > 0) dashCooldownTimer -= Time.deltaTime;
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
    }

    private void HandleHorizontalMovement(Vector2 input)
    {
        rb.gravityScale = 3f;
        float targetSpeed = input.x * moveSpeed;
        float currentSpeed = rb.linearVelocity.x;
        
        // Determine if we are accelerating or braking
        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? acceleration : deceleration;
        
        // Snappy Velocity Change: Move current speed towards target speed at accelRate
        // This eliminates "slippery" feeling and fixes the double-dt multiplication bug.
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
        if (!IsOwner) return;
        Vector2 input = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1;
        }
        moveInput = input;
        moveInputSync.Value = input; 

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded && !isDashingSync.Value)
        {
            jumpCountSync.Value++;
        }

        if (Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame && !isDashingSync.Value && dashCooldownTimer <= 0)
        {
            TriggerDashServerRpc();
        }
    }

    [ServerRpc]
    private void TriggerDashServerRpc()
    {
        if (isDashingSync.Value || dashCooldownTimer > 0) return;
        isDashingSync.Value = true;
        dashTimeLeft = dashDuration;
        dashCooldownTimer = dashCooldown;
        Vector2 currentInput = moveInputSync.Value;
        dashDirectionSync.Value = currentInput.x != 0 ? Mathf.Sign(currentInput.x) : (isFlippedSync.Value ? -1f : 1f);
        rb.linearVelocity = new Vector2(dashDirectionSync.Value * dashSpeed, 0);
    }

    #endregion

    #region Interaction

    private void HandleInteraction()
    {
        if (Mouse.current == null) return;
        if (Mouse.current.leftButton.wasPressedThisFrame) UpdateBlock(-1);
        if (Mouse.current.rightButton.wasPressedThisFrame) UpdateBlock(selectedBlockId);
    }

    private void UpdateBlock(int id)
    {
        if (MapManager.Instance == null || Camera.main == null) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -Camera.main.transform.position.z));
        if (Vector2.Distance(transform.position, worldPos) > interactRange) return;
        UpdateBlockServerRpc(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y), id);
    }

    [ServerRpc]
    private void UpdateBlockServerRpc(int x, int y, int id) { MapManager.Instance.SetBlock(x, y, id); }

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
