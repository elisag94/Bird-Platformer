using UnityEngine;

/// <summary>
/// Phase 4: smooth-damp follow, clamped to the level bounds so the camera
/// never shows the empty space past the edges of the level.
/// Attach to Main Camera and drag the Bird into Target.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("If Target is empty at startup, find the object tagged Player instead.")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private string playerTag = "Player";

    [Header("Follow")]
    [SerializeField] private Vector2 offset = new Vector2(1.5f, 0.5f);
    [Tooltip("Seconds for the camera to catch up. Higher = laggier, floatier.")]
    [SerializeField] private float smoothTime = 0.18f;

    [Header("Level bounds (world space)")]
    [SerializeField] private bool useBounds = true;
    [SerializeField] private Vector2 minBounds = new Vector2(-15f, -5f);
    [SerializeField] private Vector2 maxBounds = new Vector2(15f, 6f);

    private Camera cam;
    private Vector3 velocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (target == null && autoFindPlayer)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);

            if (player != null)
            {
                target = player.transform;
            }
        }
    }

    // LateUpdate, not Update: the player has already moved this frame, so the
    // camera follows the final position and doesn't jitter.
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desired = new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            transform.position.z);

        if (useBounds)
        {
            desired = ClampToBounds(desired);
        }

        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
    }

    private Vector3 ClampToBounds(Vector3 desired)
    {
        // Shrink the allowed area by half the camera's view so the edges of the
        // level line up with the edges of the screen.
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = minBounds.x + halfWidth;
        float maxX = maxBounds.x - halfWidth;
        float minY = minBounds.y + halfHeight;
        float maxY = maxBounds.y - halfHeight;

        // If the level is narrower than the camera, just centre on it.
        desired.x = minX > maxX ? (minBounds.x + maxBounds.x) * 0.5f : Mathf.Clamp(desired.x, minX, maxX);
        desired.y = minY > maxY ? (minBounds.y + maxBounds.y) * 0.5f : Mathf.Clamp(desired.y, minY, maxY);

        return desired;
    }

    private void OnDrawGizmosSelected()
    {
        if (!useBounds)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Vector3 centre = new Vector3((minBounds.x + maxBounds.x) * 0.5f, (minBounds.y + maxBounds.y) * 0.5f, 0f);
        Vector3 size = new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0.1f);
        Gizmos.DrawWireCube(centre, size);
    }
}