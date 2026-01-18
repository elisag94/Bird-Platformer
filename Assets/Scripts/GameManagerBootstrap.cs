using UnityEngine;

public static class GameManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureGameManagerExists()
    {
        // If a GameManager is already in the starting scene, do nothing.
        if (Object.FindAnyObjectByType<GameManager>() != null)
        {
            return;
        }

        GameObject go = new GameObject("GameManager");
        go.AddComponent<GameManager>();
    }
}
