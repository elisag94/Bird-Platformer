using UnityEngine;

/// <summary>
/// Phase 3: anything the bird must not touch. Works whether the collider is a
/// solid (spikes you bump into) or a trigger (an invisible danger zone).
/// Put the object on the Hazard layer and attach this.
/// </summary>
public class Hazard : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    [Header("Feedback (optional)")]
    [Tooltip("Optional particle/flash prefab spawned at the point of contact.")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private AudioClip hitSound;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // contactCount can be 0 in edge cases; fall back to the collider centre.
        Vector2 point = collision.contactCount > 0
            ? collision.GetContact(0).point
            : (Vector2)collision.collider.bounds.center;

        TryKill(collision.collider, point);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKill(other, other.bounds.center);
    }

    private void TryKill(Collider2D other, Vector2 contactPoint)
    {
        if (!other.CompareTag(playerTag))
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogError("Hazard hit but GameManager.Instance is null.", this);
            return;
        }

        if (GameManager.Instance.State != GameManager.GameState.Playing)
        {
            return;
        }

        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, contactPoint, Quaternion.identity);
        }

        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, contactPoint);
        }

        GameManager.Instance.Lose();
    }
}