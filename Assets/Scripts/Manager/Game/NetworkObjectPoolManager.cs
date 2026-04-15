using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NGO 전용 멀티플레이어 오브젝트 풀링 매니저.
/// INetworkPrefabInstanceHandler를 구현하여 NGO의 생성/파괴 로직을 가로챕니다.
/// </summary>
public class NetworkObjectPoolManager : NetworkBehaviour, INetworkPrefabInstanceHandler
{
    public static NetworkObjectPoolManager Instance { get; private set; }

    [System.Serializable]
    public struct PoolConfig
    {
        public GameObject prefab;
        public int initialSize;
    }

    [Header("### Pool Settings")]
    [SerializeField] private List<PoolConfig> pooledPrefabs = new List<PoolConfig>();

    // 프리팹별 풀링 큐 (NetworkObject 단위로 관리)
    private Dictionary<GameObject, Queue<NetworkObject>> poolDictionary = new Dictionary<GameObject, Queue<NetworkObject>>();
    // 네트워크 프리팹 탐색용 딕셔너리
    private Dictionary<uint, GameObject> networkPrefabToGameObject = new Dictionary<uint, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        // 서버와 클라이언트 모두에서 핸들러 등록
        foreach (var config in pooledPrefabs)
        {
            if (config.prefab == null) continue;

            NetworkObject networkObject = config.prefab.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError($"[NetworkObjectPoolManager] {config.prefab.name} has no NetworkObject component!");
                continue;
            }

            // [Fix] GlobalObjectIdHash -> PrefabIdHash (NGO 최신 버전 대응)
            uint prefabHash = networkObject.PrefabIdHash;
            networkPrefabToGameObject[prefabHash] = config.prefab;

            // NGO 시스템에 이 프리팹은 내가 직접 관리하겠다고 보고
            NetworkManager.Singleton.PrefabHandler.AddHandler(networkObject, this);

            // 초기 풀 생성
            if (!poolDictionary.ContainsKey(config.prefab))
            {
                poolDictionary[config.prefab] = new Queue<NetworkObject>();
                for (int i = 0; i < config.initialSize; i++)
                {
                    CreateNewInstance(config.prefab);
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        // 핸들러 해제
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.PrefabHandler != null)
        {
            foreach (var config in pooledPrefabs)
            {
                if (config.prefab != null && config.prefab.TryGetComponent<NetworkObject>(out var netObj))
                {
                    NetworkManager.Singleton.PrefabHandler.RemoveHandler(netObj);
                }
            }
        }
    }

    #region INetworkPrefabInstanceHandler Implementation

    /// <summary>
    /// NGO가 오브젝트 생성을 요청할 때 호출됩니다. (Server/Client 공통)
    /// </summary>
    public NetworkObject Instantiate(ulong globalObjectIdHash, Vector3 position, Quaternion rotation)
    {
        // ulong 하쉬값을 uint로 변환하여 딕셔너리에서 프리팹 탐색
        uint hash32 = (uint)globalObjectIdHash;
        if (!networkPrefabToGameObject.TryGetValue(hash32, out GameObject prefab)) return null;

        NetworkObject netObj = GetNetworkObjectFromPool(prefab);
        netObj.transform.position = position;
        netObj.transform.rotation = rotation;
        netObj.gameObject.SetActive(true);

        return netObj;
    }

    /// <summary>
    /// NGO가 오브젝트 파괴(Despawn)를 요청할 때 호출됩니다. (Server/Client 공통)
    /// </summary>
    public void Destroy(NetworkObject networkObject)
    {
        ReturnToPool(networkObject);
    }

    #endregion

    #region Internal Logic

    private NetworkObject GetNetworkObjectFromPool(GameObject prefab)
    {
        if (!poolDictionary.ContainsKey(prefab) || poolDictionary[prefab].Count == 0)
        {
            return CreateNewInstance(prefab);
        }

        NetworkObject netObj = poolDictionary[prefab].Dequeue();
        return netObj;
    }

    private NetworkObject CreateNewInstance(GameObject prefab)
    {
        // Unity 엔진의 Instantiate 호출
        GameObject obj = UnityEngine.Object.Instantiate(prefab);
        obj.SetActive(false);
        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        
        if (!poolDictionary.ContainsKey(prefab)) poolDictionary[prefab] = new Queue<NetworkObject>();
        
        ReturnToPool(netObj);
        
        return netObj;
    }

    private void ReturnToPool(NetworkObject networkObject)
    {
        networkObject.gameObject.SetActive(false);

        // [Fix] GlobalObjectIdHash -> PrefabIdHash
        uint hash = networkObject.PrefabIdHash;
        if (networkPrefabToGameObject.TryGetValue(hash, out GameObject prefab))
        {
            if (!poolDictionary.ContainsKey(prefab)) poolDictionary[prefab] = new Queue<NetworkObject>();
            
            if (!poolDictionary[prefab].Contains(networkObject))
            {
                poolDictionary[prefab].Enqueue(networkObject);
            }
        }
        else
        {
            Destroy(networkObject.gameObject);
        }
    }

    #endregion

    #region Public API (Server Only)

    /// <summary>
    /// 서버에서 아이템 등을 생성할 때 사용하는 메서드입니다.
    /// </summary>
    public NetworkObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return null;

        // [Fix] GlobalObjectIdHash -> PrefabIdHash
        NetworkObject netObj = Instantiate(
            prefab.GetComponent<NetworkObject>().PrefabIdHash, 
            position, rotation);
        
        netObj.Spawn();
        return netObj;
    }

    #endregion
}
