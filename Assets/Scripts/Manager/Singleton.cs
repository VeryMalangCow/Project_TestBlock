using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    #region Variable

    private static T instance;


    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Object.FindFirstObjectByType<T>();
                if (instance == null)
                {
                    GameObject obj = new GameObject();
                    obj.name = typeof(T).Name;
                    instance = obj.AddComponent<T>();
                }
            }
            return instance;
        }
    }

    #endregion

    #region MonoBehaviour

    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = this as T;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    #endregion

}

public class PermanentSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    #region Variable

    private static T instance;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Object.FindFirstObjectByType<T>();
                if (instance == null)
                {
                    GameObject obj = new GameObject();
                    obj.name = typeof(T).Name;
                    instance = obj.AddComponent<T>();
                }
            }
            return instance;
        }
    }

    #endregion

    #region MonoBehaviour

    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = this as T;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion
}
