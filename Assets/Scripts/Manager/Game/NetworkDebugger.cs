using Unity.Netcode;
using UnityEngine;
using TMPro;
using System.Text;
using System.Collections.Generic;

public class NetworkDebugger : MonoBehaviour
{
    [Header("# Debug UI")]
    [SerializeField] private TextMeshProUGUI debugDisplayText;
    
    private StringBuilder sb = new StringBuilder();
    private HashSet<ulong> trackedObjects = new HashSet<ulong>();

    private void Update()
    {
        if (debugDisplayText == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) 
        {
            debugDisplayText.text = "Network: Disconnected";
            return;
        }

        UpdateDebugText();
        TrackNetworkObjects();
    }

    private void UpdateDebugText()
    {
        sb.Clear();
        
        // 1. 네트워크 정보
        var nm = NetworkManager.Singleton;
        string role = nm.IsServer ? (nm.IsHost ? "Host" : "Server") : "Client";
        sb.AppendLine($"<b>[NET]</b> {role} (ID: {nm.LocalClientId})");
        
        // 2. 플레이어 정보
        if (PlayerController.Local != null)
        {
            var p = PlayerController.Local;
            sb.AppendLine($"<b>[PLAYER]</b> Pos: {p.transform.position.x:F1}, {p.transform.position.y:F1}");
            sb.AppendLine($"Hotbar: {p.SelectedHotbarIndex + 1} | Dash: {p.IsDashing}");
            
            var ghost = p.GhostItem;
            if (!ghost.IsEmpty) sb.AppendLine($"Ghost: {ghost.itemID} (x{ghost.stackCount})");
        }

        // 3. 맵 정보
        if (MapManager.Instance != null)
        {
            sb.AppendLine($"<b>[MAP]</b> {MapManager.Instance.activeStyle} (Ready: {MapManager.Instance.IsMapReady()})");
        }

        debugDisplayText.text = sb.ToString();
    }

    private void TrackNetworkObjects()
    {
        var allNetObjs = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        foreach (var obj in allNetObjs)
        {
            if (!trackedObjects.Contains(obj.NetworkObjectId))
            {
                trackedObjects.Add(obj.NetworkObjectId);
                Debug.Log($"[Net-Spawn] {obj.name} (ID: {obj.NetworkObjectId})");
            }
        }
    }
}
