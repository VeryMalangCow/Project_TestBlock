using UnityEngine;

public class GameManager : PermanentSingleton<GameManager>
{
    // Game logic goes here

    #region MonoBehaviour

    private void Start()
    {
        ResourceManager.Instance.Init();

        MapManager.Instance.mapGenerator.GenerateMap();
    }

    #endregion

}
