using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// NGO 엔진 레벨의 모든 네트워크 객체를 실시간 감시합니다. (Unity 6 완벽 대응)
/// </summary>
public class NetworkDebugger : MonoBehaviour
{
    private HashSet<ulong> trackedObjects = new HashSet<ulong>();

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        // 현재 씬에 있는 모든 NetworkObject 탐색
        var allNetObjs = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        
        foreach (var obj in allNetObjs)
        {
            if (!trackedObjects.Contains(obj.NetworkObjectId))
            {
                // 새로 발견된 객체 로그 출력
                trackedObjects.Add(obj.NetworkObjectId);
                
                string status = obj.IsOwnedByServer ? "Server" : "Client";
                Debug.Log($"<color=yellow>[Net-Spawn]</color> {status} Detected: {obj.name} | Hash: {obj.PrefabIdHash} | ID: {obj.NetworkObjectId}");
                
                // 만약 해시가 0이라면 즉시 경고
                if (obj.PrefabIdHash == 0)
                {
                    Debug.LogError($"<color=red>[Net-CRITICAL]</color> Object {obj.name} spawned with HASH 0! This object is broken.");
                }
            }
        }
    }

    [ContextMenu("Force Log All Network Objects")]
    public void LogAll()
    {
        var allNetObjs = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        Debug.Log($"--- Current Network Objects ({allNetObjs.Length}) ---");
        foreach (var obj in allNetObjs)
        {
            Debug.Log($"{obj.name} | Hash: {obj.PrefabIdHash} | IsSpawned: {obj.IsSpawned}");
        }
    }
}
