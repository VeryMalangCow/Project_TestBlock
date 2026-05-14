using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("### Data")]
    [SerializeField] private BlockVisualDatabase visualDatabase;

    [Header("### Master Prefabs")]
    [SerializeField] private GameObject masterHitDust;   // 모든 블록 공용 타격 파티클
    [SerializeField] private GameObject masterBreakDust; // 모든 블록 공용 파괴 파티클
    [SerializeField] private GameObject fakeGlowPrefab;  // 모든 블록 공용 발광 효과

    [Header("### Audio")]
    [SerializeField] private AudioSource localAudioSource;

    private Dictionary<string, Queue<GameObject>> poolDictionary = new Dictionary<string, Queue<GameObject>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (visualDatabase != null) visualDatabase.Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #region Public Interface

    public void PlayHitFX(Vector2 worldPos, int blockID)
    {
        var data = visualDatabase.GetData(blockID);
        if (data == null) return;

        // 1. 마스터 타격 파티클 소환 및 색상 주입
        if (masterHitDust != null)
        {
            GameObject dust = SpawnFromPool(masterHitDust, worldPos, Quaternion.identity);
            SetParticleColor(dust, data.mainColor);
        }

        // 2. 발광 효과 (데이터 설정에 따라)
        if (data.useGlow && fakeGlowPrefab != null)
        {
            StartCoroutine(FakeGlowSequence(worldPos, data.mainColor, 0.1f, 0.8f));
        }

        // 3. 타격 사운드 (로컬)
        if (data.blockSound != null && localAudioSource != null)
        {
            localAudioSource.PlayOneShot(data.blockSound);
        }
    }

    public void PlayBreakFX(Vector2 worldPos, int blockID)
    {
        var data = visualDatabase.GetData(blockID);
        if (data == null) return;

        // 1. 마스터 파괴 파티클 소환 및 색상 주입
        if (masterBreakDust != null)
        {
            GameObject dust = SpawnFromPool(masterBreakDust, worldPos, Quaternion.identity);
            SetParticleColor(dust, data.mainColor);
        }

        // 2. 발광 효과 (파괴 시에는 더 강하게)
        if (data.useGlow && fakeGlowPrefab != null)
        {
            StartCoroutine(FakeGlowSequence(worldPos, data.mainColor, 0.2f, 1.5f));
        }

        // 3. 파괴 사운드 (공간음)
        if (data.blockSound != null)
        {
            AudioSource.PlayClipAtPoint(data.blockSound, new Vector3(worldPos.x, worldPos.y, -2f));
        }
    }

    #endregion

    #region Pooling System

    private GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        string key = prefab.name;
        if (!poolDictionary.ContainsKey(key))
        {
            poolDictionary.Add(key, new Queue<GameObject>());
        }

        GameObject objectToSpawn;
        if (poolDictionary[key].Count > 0)
        {
            objectToSpawn = poolDictionary[key].Dequeue();
            objectToSpawn.SetActive(true);
            objectToSpawn.transform.position = position;
            objectToSpawn.transform.rotation = rotation;
        }
        else
        {
            objectToSpawn = Instantiate(prefab, position, rotation);
            objectToSpawn.name = key;
        }

        StartCoroutine(ReturnToPoolAfterDelay(objectToSpawn, key));
        return objectToSpawn;
    }

    private IEnumerator ReturnToPoolAfterDelay(GameObject obj, string key)
    {
        var ps = obj.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            yield return new WaitForSeconds(ps.main.duration + ps.main.startLifetime.constantMax);
        }
        else
        {
            yield return new WaitForSeconds(1f);
        }

        obj.SetActive(false);
        poolDictionary[key].Enqueue(obj);
    }

    private void SetParticleColor(GameObject obj, Color color)
    {
        var ps = obj.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            // 색상을 약간 랜덤화하여 자연스럽게 표현
            main.startColor = new ParticleSystem.MinMaxGradient(color * 0.7f, color * 1.3f);
        }
    }

    #endregion

    #region Visual Sequences

    private IEnumerator FakeGlowSequence(Vector2 pos, Color color, float duration, float scale)
    {
        GameObject glow = SpawnFromPool(fakeGlowPrefab, pos, Quaternion.identity);
        var renderer = glow.GetComponent<SpriteRenderer>();
        if (renderer == null) yield break;

        renderer.color = color;
        glow.transform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            glow.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * scale, t);
            
            Color c = color;
            c.a = 1f - t;
            renderer.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        glow.SetActive(false);
    }

    #endregion
}
