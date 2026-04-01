using System.Collections;
using UnityEngine;

public class GameManager : PermanentSingleton<GameManager>
{
    private void Start()
    {
        Application.targetFrameRate = 144;
    }

}
