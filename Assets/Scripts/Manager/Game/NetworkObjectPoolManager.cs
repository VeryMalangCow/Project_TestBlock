using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Unity 6 & NGO 2.x 환경용 네트워크 오브젝트 풀 매니저
/// - 프리팹별 전용 InstanceHandler 등록
/// - 클라이언트에서 정상적으로 시각 오브젝트가 생성되도록 수정
/// </summary>
public class NetworkObjectPoolManager : MonoBehaviour
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

    // 공식 프리팹 기준 풀
    private readonly Dictionary<GameObject, Queue<NetworkObject>> poolDictionary = new();
    private readonly Dictionary<GameObject, PoolInstanceHandler> handlerDictionary = new();

    // 현재 사용 중인 객체 추적
    private readonly HashSet<NetworkObject> activeObjects = new();

    // 각 인스턴스가 어느 prefab 풀에 속하는지 추적
    private readonly Dictionary<NetworkObject, GameObject> instanceToPrefab = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(WaitForNetworkManager());
    }

    private IEnumerator WaitForNetworkManager()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        var networkPrefabs = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        foreach (var config in pooledPrefabs)
        {
            if (config.prefab == null)
            {
                continue;
            }

            GameObject officialPrefab = null;

            foreach (var netPrefab in networkPrefabs)
            {
                if (netPrefab.Prefab == null)
                {
                    continue;
                }

                if (netPrefab.Prefab == config.prefab || netPrefab.Prefab.name == config.prefab.name)
                {
                    officialPrefab = netPrefab.Prefab;
                    break;
                }
            }

            if (officialPrefab == null)
            {
                Debug.LogWarning($"[Pool-Init] NetworkPrefab not found for {config.prefab.name}");
                continue;
            }

            if (!poolDictionary.ContainsKey(officialPrefab))
            {
                poolDictionary[officialPrefab] = new Queue<NetworkObject>();
            }

            // 프리팹별 전용 핸들러 등록
            if (!handlerDictionary.ContainsKey(officialPrefab))
            {
                var handler = new PoolInstanceHandler(this, officialPrefab);
                handlerDictionary.Add(officialPrefab, handler);
                NetworkManager.Singleton.PrefabHandler.AddHandler(officialPrefab, handler);
            }

            // 초기 풀 생성
            for (int i = 0; i < config.initialSize; i++)
            {
                CreateNewInstance(officialPrefab);
            }

            Debug.Log($"[Pool-Init] Registered {officialPrefab.name}");
        }
    }

    #region Inner Handler

    /// <summary>
    /// 프리팹 하나당 하나씩 등록되는 전용 핸들러
    /// </summary>
    private class PoolInstanceHandler : INetworkPrefabInstanceHandler
    {
        private readonly NetworkObjectPoolManager manager;
        private readonly GameObject prefab;

        public PoolInstanceHandler(NetworkObjectPoolManager manager, GameObject prefab)
        {
            this.manager = manager;
            this.prefab = prefab;
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            // ownerClientId를 해시처럼 사용하면 안 됨
            NetworkObject netObj = manager.GetNetworkObjectFromPool(prefab);

            netObj.transform.SetPositionAndRotation(position, rotation);
            netObj.gameObject.SetActive(true);
            manager.activeObjects.Add(netObj);

            return netObj;
        }

        public void Destroy(NetworkObject networkObject)
        {
            manager.ReturnToPool(networkObject);
        }
    }

    #endregion

    #region Internal Logic

    private NetworkObject GetNetworkObjectFromPool(GameObject prefab)
    {
        if (!poolDictionary.ContainsKey(prefab))
        {
            poolDictionary[prefab] = new Queue<NetworkObject>();
        }

        while (poolDictionary[prefab].Count > 0)
        {
            NetworkObject netObj = poolDictionary[prefab].Dequeue();

            if (netObj != null && !activeObjects.Contains(netObj))
            {
                return netObj;
            }
        }

        return CreateNewInstance(prefab);
    }

    private NetworkObject CreateNewInstance(GameObject prefab)
    {
        GameObject obj = Instantiate(prefab);
        obj.SetActive(false);

        NetworkObject netObj = obj.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[Pool] Prefab {prefab.name} does not have NetworkObject.");
            return null;
        }

        if (!poolDictionary.ContainsKey(prefab))
        {
            poolDictionary[prefab] = new Queue<NetworkObject>();
        }

        poolDictionary[prefab].Enqueue(netObj);
        instanceToPrefab[netObj] = prefab;

        return netObj;
    }

    private void ReturnToPool(NetworkObject networkObject)
    {
        if (networkObject == null)
        {
            return;
        }

        activeObjects.Remove(networkObject);

        networkObject.gameObject.SetActive(false);

        if (instanceToPrefab.TryGetValue(networkObject, out GameObject prefab))
        {
            if (!poolDictionary.ContainsKey(prefab))
            {
                poolDictionary[prefab] = new Queue<NetworkObject>();
            }

            if (!poolDictionary[prefab].Contains(networkObject))
            {
                poolDictionary[prefab].Enqueue(networkObject);
            }
        }
        else
        {
            Debug.LogWarning($"[Pool] Returned object {networkObject.name} has no prefab mapping.");
        }
    }

    #endregion

    #region Public API (Server)

    public NetworkObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return null;
        }

        GameObject officialPrefab = GetOfficialPrefab(prefab);
        if (officialPrefab == null)
        {
            Debug.LogError($"[Pool-Spawn] Could not find official prefab for {prefab.name}");
            return null;
        }

        NetworkObject netObj = GetNetworkObjectFromPool(officialPrefab);
        if (netObj == null)
        {
            return null;
        }

        netObj.transform.SetPositionAndRotation(pos, rot);
        netObj.gameObject.SetActive(true);
        activeObjects.Add(netObj);

        if (!netObj.IsSpawned)
        {
            netObj.Spawn(true);
        }

        return netObj;
    }

    public void Despawn(NetworkObject networkObject, bool destroy = false)
    {
        if (networkObject == null)
        {
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (networkObject.IsSpawned)
        {
            networkObject.Despawn(destroy);
        }
        else
        {
            ReturnToPool(networkObject);
        }
    }

    private GameObject GetOfficialPrefab(GameObject prefab)
    {
        if (prefab == null || NetworkManager.Singleton == null)
        {
            return null;
        }

        var networkPrefabs = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        foreach (var netPrefab in networkPrefabs)
        {
            if (netPrefab.Prefab == null)
            {
                continue;
            }

            if (netPrefab.Prefab == prefab || netPrefab.Prefab.name == prefab.name)
            {
                return netPrefab.Prefab;
            }
        }

        return null;
    }

    #endregion
}