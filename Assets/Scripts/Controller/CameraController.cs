using UnityEngine;

public class CameraController : MonoBehaviour
{
    #region Variable

    [SerializeField] private Transform target;
    [SerializeField] private float smoothSpeed = 0.125f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10f);

    private Vector3 currentVelocity;

    #endregion

    #region MonoBehaviour

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;
        // Smoothly follow the target using SmoothDamp
        //Vector3 smoothedPosition = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothSpeed);

        //transform.position = smoothedPosition;
        transform.position = desiredPosition;
    }

    #endregion
}
