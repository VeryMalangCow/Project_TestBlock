using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region Variable

    [Header("### Components")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D col;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    [Header("### Visuals")]
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite jumpSprite;

    [Header("### Move")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 40f;
    [SerializeField] private float jumpForce = 12f;

    [Header("### Physics")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.2f;

    [Header("### Interaction")]
    [SerializeField] private int selectedBlockId = 0;
    [SerializeField] private float interactRange = 6f;

    private Vector2 moveInput;
    private bool isGrounded;
    private bool jumpRequest;

    #endregion

    #region Input Event

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed && isGrounded)
        {
            jumpRequest = true;
        }
    }

    #endregion

    #region MonoBehaivour

    private void Start()
    {
        transform.position = MapManager.Instance.GetPositionByRatio(50f, 60f);    
    }

    private void Update()
    {
        CheckGrounded();
        HandleInteraction();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
        HandleJump();
    }

    #endregion

    #region Visuals

    private void UpdateVisuals()
    {
        if (spriteRenderer == null) return;

        // Flip sprite based on movement direction
        if (Mathf.Abs(moveInput.x) > 0.01f)
        {
            spriteRenderer.flipX = moveInput.x < 0;
        }

        // Handle Animation and Sprites
        if (!isGrounded)
        {
            // Jump State
            if (animator != null) animator.enabled = false;
            if (jumpSprite != null) spriteRenderer.sprite = jumpSprite;
        }
        else
        {
            // Ground State
            float currentSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (currentSpeed > 0.1f)
            {
                // Walking State
                if (animator != null)
                {
                    animator.enabled = true;
                    animator.SetFloat("MoveSpeed", currentSpeed);
                }
            }
            else
            {
                // Idle State
                if (animator != null) animator.enabled = false;
                if (idleSprite != null) spriteRenderer.sprite = idleSprite;
            }
        }
    }

    #endregion

    #region Physics Move

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
