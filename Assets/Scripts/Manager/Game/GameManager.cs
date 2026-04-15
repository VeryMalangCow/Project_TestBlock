using System.Collections;
using UnityEngine;

public class GameManager : PermanentSingleton<GameManager>
{
    [SerializeField] private Texture2D cursorTexture;

    private void Start()
    {
        Application.targetFrameRate = 120;
        Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.ForceSoftware);
    }

}
