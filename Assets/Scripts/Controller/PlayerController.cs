using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region Variable

    [Header("# Move")]
    [SerializeField] private float moveSpeed = 10f;
    
    [Header("# Interaction")]
    [SerializeField] private int selectedBlockId = 0;

    private Vector2 moveInput;

    #endregion

    #region Move

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    #endregion

    #region Interaction

    public void OnAttack(InputValue value)
    {
        // Left Click (Attack) to remove block
        if (value.isPressed)
        {
            UpdateBlock(-1);
        }
    }

    private void UpdateBlock(int id)
    {
        if (MapManager.Instance == null) return;

        // Get mouse position in world coordinates
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -Camera.main.transform.position.z));
        
        int x = Mathf.FloorToInt(worldPos.x);
        int y = Mathf.FloorToInt(worldPos.y);

        MapManager.Instance.SetBlock(x, y, id);
    }

    #endregion

    #region MonoBehaivour

    private void Update()
    {
        Vector3 move = new Vector3(moveInput.x, moveInput.y, 0) * moveSpeed * Time.deltaTime;
        transform.position += move;

        // Right Click check (for quick prototyping if OnSecondaryFire is not set up)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            UpdateBlock(selectedBlockId);
        }
    }

    #endregion
}
