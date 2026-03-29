using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region Variable

    [Header("### Components")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D col;

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
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
        HandleJump();
    }

    #endregion

    #region Physics Move

    private void CheckGrounded()
    {
        // Simple ground check using a small overlap circle at feet
        Vector2 feetPos = new Vector2(transform.position.x, col.bounds.min.y);
        isGrounded = Physics2D.OverlapCircle(feetPos, groundCheckRadius, groundLayer);
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
        Vector2 feetPos = new Vector2(transform.position.x, col.bounds.min.y);
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(feetPos, groundCheckRadius);
    }

    #endregion
}
