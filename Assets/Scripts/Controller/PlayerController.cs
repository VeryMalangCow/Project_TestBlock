using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region Variable

    [Header("# Move")]
    [SerializeField] private float moveSpeed = 10f;
    
    private Vector2 moveInput;

    #endregion

    #region Move

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    #endregion

    #region MonoBehaivour

    private void Update()
    {
        Vector3 move = new Vector3(moveInput.x, moveInput.y, 0) * moveSpeed * Time.deltaTime;
        transform.position += move;
    }

    #endregion
}
