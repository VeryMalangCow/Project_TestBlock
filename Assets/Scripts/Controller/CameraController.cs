using Unity.Netcode;
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
        if (target == null)
        {
            // Try to find the local player if target is lost
            FindLocalPlayer();
            return;
        }

        transform.position = target.position + offset;
    }

    #endregion

    #region Find Target

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.SetTarget(target);
        }
    }

    private void FindLocalPlayer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer != null)
        {
            target = localPlayer.transform;
            Debug.Log("[CameraController] Target set to Local Player.");
            
            // Also notify MeshManager to follow this player for sliding window
            if (MeshManager.Instance != null)
            {
                MeshManager.Instance.SetTarget(target);
            }
        }
    }

    #endregion
}
