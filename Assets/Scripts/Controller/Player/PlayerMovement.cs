using UnityEngine;

/// <summary>
    /// 플레이어의 물리 이동, 점프, 대시 및 지면 감지를 담당하는 컴포넌트입니다.
    /// </summary>
public class PlayerMovement : MonoBehaviour
{
    private PlayerController controller;
    private Rigidbody2D rb;
    private BoxCollider2D col;

    [Header("### Move Settings")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 50f;
    [SerializeField] private float jumpForce = 13f;

    [Header("### Dash Settings")]
    [SerializeField] private float dashSpeed = 28f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 0.5f;
    
    [Header("### Physics Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private float stepHeight = 1.1f; 
    [SerializeField] private float stepCheckDistance = 0.1f;

    [Header("### Step Climb Settings")]
    [Tooltip("자동 등반 시 캐릭터를 수직으로 이동시킬 거리입니다. (추천: 0.3)")]
    [SerializeField] private float stepUpAmount = 0.3f;

    [Tooltip("장애물 검사 기둥의 시작 높이입니다. 발바닥 기준이며, 등반할 블럭과의 충돌을 피하기 위해 1.1f 이상을 권장합니다.")]
    [SerializeField] private float climbCheckMinY = 1.1f;

    [Tooltip("장애물 검사 기둥의 끝 높이입니다. 발바닥 기준이며, 캐릭터가 올라간 후의 머리 높이(약 5.0)를 고려하여 설정합니다. (추천: 4.9)")]
    [SerializeField] private float climbCheckMaxY = 3.7f;

    [Tooltip("검사 영역의 가로 폭 비율입니다. 1.0은 캐릭터 콜라이더 폭과 같으며, 벽면에 너무 민감하게 걸리지 않도록 0.8~0.9 범위를 권장합니다.")]
    [SerializeField] private float climbCheckWidthPercent = 0.85f;

    private float dashTimeLeft;
    private float dashCooldownTimer;
    private bool isGrounded;

    // [Optimization] 지면 체크 최적화를 위한 타이머
    private float groundCheckInterval = 0.05f; 
    private float groundCheckTimer;

    public bool IsGrounded => isGrounded;
    public float DashCooldownTimer => dashCooldownTimer;
    public float DashDuration => dashDuration;

    public void Init(PlayerController ctrl, Rigidbody2D rigidbody, BoxCollider2D collider, LayerMask ground)
    {
        controller = ctrl;
        rb = rigidbody;
        col = collider;
        groundLayer = ground;
    }

    public void Tick()
    {
        if (dashCooldownTimer > 0) dashCooldownTimer -= Time.deltaTime;
        // [Moved] Dash duration timer moved to FixedTick for physics consistency
    }

    public void FixedTick(Vector2 moveInput)
    {
        // [Multiplayer Fix] Only the owner should apply direct velocity changes
        if (!controller.IsOwner) return;

        // [New] Dash Duration Timer (Physics-based)
        if (controller.IsDashing)
        {
            dashTimeLeft -= Time.fixedDeltaTime;
            if (dashTimeLeft <= 0) controller.EndDash();
        }

        OptimizedCheckGrounded();

        if (controller.IsDashing)
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

    public void ApplyJump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

    public void StartDash(float direction)
    {
        dashTimeLeft = dashDuration;
        dashCooldownTimer = dashCooldown;
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

    private void HandleDash()
    {
        rb.gravityScale = 0;
        rb.linearVelocity = new Vector2(controller.DashDirection * dashSpeed, 0);
    }

    private void HandleStepClimb(Vector2 input)
    {
        float dir = controller.IsDashing ? controller.DashDirection : (input.x != 0 ? Mathf.Sign(input.x) : 0);
        if (dir == 0) return;

        // 1. 등반 대상(발밑 블럭) 확인
        Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y + 0.1f);
        Vector2 direction = new Vector2(dir, 0);
        float distance = (col.size.x / 2f) + stepCheckDistance;

        RaycastHit2D hitLower = Physics2D.Raycast(origin, direction, distance, groundLayer);
        if (hitLower.collider != null)
        {
            // 2. 등반 가능 여부 및 사각지대 없는 전면 공간 검사 (Full Frontal Scan)
            float forwardOffset = dir * 0.15f; 
            
            // 인스펙터 설정값 기반 수직 기둥의 높이 및 중심 계산
            float columnHeight = climbCheckMaxY - climbCheckMinY;
            float centerOffsetY = (climbCheckMaxY + climbCheckMinY) / 2f;
            
            Vector2 checkSize = new Vector2(col.size.x * climbCheckWidthPercent, columnHeight);
            
            // 기둥의 중심점 계산: 발바닥(min.y) 기준으로 설정된 중심 오프셋 적용
            Vector2 checkCenter = new Vector2(col.bounds.center.x + forwardOffset, col.bounds.min.y + centerOffsetY + stepUpAmount);
            
            Collider2D obstacle = Physics2D.OverlapBox(checkCenter, checkSize, 0f, groundLayer);
            
            // 3. 기둥 안에 장애물이 전혀 없을 때만 등반 실행
            if (obstacle == null)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                rb.position += new Vector2(dir * 0.05f, stepUpAmount);
                
                isGrounded = true;
                controller.OnGroundedChanged(true);
            }
        }
    }

    /// <summary>
    /// 매 프레임 물리 체크를 하는 대신, 설정된 간격(0.05s)마다 체크하여 CPU 부하를 줄입니다.
    /// </summary>
    private void OptimizedCheckGrounded()
    {
        groundCheckTimer += Time.fixedDeltaTime;
        if (groundCheckTimer < groundCheckInterval) return;
        
        groundCheckTimer = 0f;

        if (col == null) return;
        float boxWidth = col.bounds.size.x * 0.85f;
        float boxHeight = groundCheckRadius;
        Vector2 boxCenter = new Vector2(col.bounds.center.x, col.bounds.min.y - (boxHeight / 2f));
        
        bool newGrounded = Physics2D.OverlapBox(boxCenter, new Vector2(boxWidth, boxHeight), 0f, groundLayer);
        
        if (isGrounded != newGrounded)
        {
            isGrounded = newGrounded;
            controller.OnGroundedChanged(isGrounded);
        }
    }
}
