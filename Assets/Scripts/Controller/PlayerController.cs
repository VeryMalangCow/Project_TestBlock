using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region Variable

    [Header("### Components")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D col;
    [SerializeField] private PlayerVisuals visuals;

    [Header("### Move")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 40f;
    [SerializeField] private float jumpForce = 12f;

    [Header("### Dash")]
    [SerializeField] private float dashSpeed = 25f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 0.5f;
    private bool isDashing;
    private float dashTimeLeft;
    private float dashCooldownTimer;
    private float dashDirection;

    [Header("### Physics")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private float stepHeight = 1.1f; // Slightly more than 1 block
    [SerializeField] private float stepCheckDistance = 0.1f;

    [Header("### Interaction")]
    [SerializeField] private int selectedBlockId = 0;
    [SerializeField] private float interactRange = 6f;

    private Vector2 moveInput;
    private bool isGrounded;
    private bool jumpRequest;

    // Animation state
    private float walkCycleTime;
    [SerializeField] private float walkAnimSpeedMultiplier = 2f;

    #endregion

    #region Input Event

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed && isGrounded && !isDashing)
        {
            jumpRequest = true;
        }
    }

    public void OnDash(InputValue value)
    {
        if (value.isPressed && !isDashing && dashCooldownTimer <= 0)
        {
            isDashing = true;
            dashTimeLeft = dashDuration;
            dashCooldownTimer = dashCooldown;
            
            // Dash direction: movement input direction or current facing direction
            dashDirection = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : (visuals != null && visuals.IsFlipped ? -1f : 1f);
            
            // Reset vertical velocity
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        }
    }

    #endregion

    #region MonoBehaivour

    private void Start()
    {
        transform.position = MapManager.Instance.GetPositionByRatio(50f, 60f);

        if (visuals != null)
        {
            visuals.Init();
            // Test: Load default body and some equipment
            // Categories: Backpacks, Cloaks, Clothes, Heads
            visuals.SetArmor("Clothes", 0);
            visuals.SetArmor("Backpacks", 0);
            visuals.SetArmor("Heads", 0);
            visuals.SetArmor("Cloaks", 0);
        }
    }

    private void Update()
    {
        CheckGrounded();
        HandleInteraction();
        UpdateTimers();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        if (isDashing)
        {
            HandleDash();
            HandleStepClimb(); // Dash-step is very important for fluidity
        }
        else
        {
            HandleHorizontalMovement();
            HandleJump();
            HandleStepClimb();
        }
    }

    #endregion

    #region Visuals

    private void UpdateVisuals()
    {
        if (visuals == null) return;

        // Flip visuals based on movement direction
        if (Mathf.Abs(moveInput.x) > 0.01f && !isDashing)
        {
            visuals.SetFlip(moveInput.x < 0);
        }

        int targetFrame = 0;

        if (isDashing)
        {
            // Dash State (Frame 11)
            targetFrame = 11;
        }
        else if (!isGrounded)
        {
            // Jump State (Frame 10)
            targetFrame = 10;
            walkCycleTime = 0; // Reset walk cycle
        }
        else
        {
            float currentHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x);
            
            if (currentHorizontalSpeed > 0.1f)
            {
                // Walk Animation (Frames 0-9)
                // Cycle index based on speed and time
                walkCycleTime += Time.deltaTime * currentHorizontalSpeed * walkAnimSpeedMultiplier;
                targetFrame = Mathf.FloorToInt(walkCycleTime) % 10;
            }
            else
            {
                // Idle State (Frame 0)
                targetFrame = 0;
                walkCycleTime = 0;
            }
        }

        visuals.SyncAnimation(targetFrame);
    }

    #endregion

    #region Physics Move

    private void UpdateTimers()
    {
        if (isDashing)
        {
            dashTimeLeft -= Time.deltaTime;
            if (dashTimeLeft <= 0)
            {
                isDashing = false;
                // Restore gravity
                rb.gravityScale = 3f;
            }
        }

        if (dashCooldownTimer > 0)
        {
            dashCooldownTimer -= Time.deltaTime;
        }
    }

    private void HandleDash()
    {
        // Override gravity and apply high speed
        rb.gravityScale = 0;
        rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0);
    }

    private void HandleStepClimb()
    {
        // Direction to check
        float dir = isDashing ? dashDirection : (moveInput.x != 0 ? Mathf.Sign(moveInput.x) : 0);
        if (dir == 0) return;

        Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y + 0.1f);
        Vector2 direction = new Vector2(dir, 0);
        float distance = (col.size.x / 2f) + stepCheckDistance;

        // 1. Check if there's a wall at foot level
        RaycastHit2D hitLower = Physics2D.Raycast(origin, direction, distance, groundLayer);
        if (hitLower.collider != null)
        {
            // 2. Check if the space above (stepHeight) is clear
            Vector2 upperOrigin = origin + new Vector2(0, stepHeight);
            RaycastHit2D hitUpper = Physics2D.Raycast(upperOrigin, direction, distance, groundLayer);

            if (hitUpper.collider == null)
            {
                // 3. Step up: Move the player slightly up and forward
                // Using position offset for immediate "snappy" step feel
                rb.position += new Vector2(0, 0.2f); // Small nudge to clear the edge
            }
        }
    }

    private void CheckGrounded()
    {
        if (col == null) return;

        // Calculate a box that is slightly narrower than the player to avoid wall friction
        // but covers the feet area.
        float boxWidth = col.bounds.size.x * 0.9f; 
        float boxHeight = groundCheckRadius;
        Vector2 boxCenter = new Vector2(col.bounds.center.x, col.bounds.min.y - (boxHeight / 2f));

        isGrounded = Physics2D.OverlapBox(boxCenter, new Vector2(boxWidth, boxHeight), 0f, groundLayer);
    }

    private void HandleHorizontalMovement()
    {
        rb.gravityScale = 3f; // Default gravity scale (adjust as needed for better feel)

        float targetSpeed = moveInput.x * moveSpeed;
        float speedDiff = targetSpeed - rb.linearVelocity.x;
        
        // Apply acceleration or deceleration
        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? acceleration : deceleration;
        float movement = speedDiff * accelRate * Time.fixedDeltaTime;

        rb.AddForce(Vector2.right * movement, ForceMode2D.Force);
    }

    private void HandleJump()
    {
        if (jumpRequest)
        {
            // Reset vertical velocity for consistent jump height
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            jumpRequest = false;
        }
    }

    #endregion

    #region Interaction

    private void HandleInteraction()
    {
        // Left Click (Destroy)
        if (Mouse.current.leftButton.isPressed)
        {
            UpdateBlock(-1);
        }
        
        // Right Click (Place)
        if (Mouse.current.rightButton.isPressed)
        {
            UpdateBlock(selectedBlockId);
        }
    }

    private void UpdateBlock(int id)
    {
        if (MapManager.Instance == null || Camera.main == null) return;

        // Get world position from mouse
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -Camera.main.transform.position.z));
        
        // Range check
        if (Vector2.Distance(transform.position, worldPos) > interactRange) return;

        int x = Mathf.FloorToInt(worldPos.x);
        int y = Mathf.FloorToInt(worldPos.y);

        MapManager.Instance.SetBlock(x, y, id);
    }

    #endregion

    #region Debug

    private void OnDrawGizmos()
    {
        if (col == null) return;
        
        float boxWidth = col.bounds.size.x * 0.9f;
        float boxHeight = groundCheckRadius;
        Vector2 boxCenter = new Vector2(col.bounds.center.x, col.bounds.min.y - (boxHeight / 2f));

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireCube(boxCenter, new Vector3(boxWidth, boxHeight, 0));
    }

    #endregion
}
