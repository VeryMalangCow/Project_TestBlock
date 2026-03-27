using UnityEngine;

public class CameraController : MonoBehaviour
{
    #region Variable

    [Header("# Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10f);

    #endregion

    #region MonoBehaviour

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        transform.position = desiredPosition;
    }

    #endregion
}
