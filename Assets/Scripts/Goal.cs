using UnityEngine;

/// <summary>
/// Phase 5: the family nest. Entering this trigger wins the level.
/// The collider on this object must have Is Trigger checked.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Goal : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    [Header("Feedback (optional)")]
    [SerializeField] private AudioClip winSound;

    private bool alreadyTriggered;

    private void Reset()
    {
        // Convenience: newly added Goal components start as a trigger.
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (alreadyTriggered || !other.CompareTag(playerTag))
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogError("Goal reached but GameManager.Instance is null.", this);
            return;
        }

        alreadyTriggered = true;

        if (winSound != null)
        {
            AudioSource.PlayClipAtPoint(winSound, transform.position);
        }

        GameManager.Instance.Win();
    }
}