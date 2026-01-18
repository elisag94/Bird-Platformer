using UnityEngine;

public class Goal : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    [Header("Optional behavior")]
    [SerializeField] private bool loadMainMenuOnReach = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag))
        {
            return;
        }

        Debug.Log("Goal reached! (Bird reunited with family)", this);

        if (loadMainMenuOnReach && GameManager.Instance != null)
        {
            GameManager.Instance.LoadMainMenu();
        }
    }
}
