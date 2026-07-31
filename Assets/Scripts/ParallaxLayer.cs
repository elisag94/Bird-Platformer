using UnityEngine;

/// <summary>
/// Scrolls a background layer at a fraction of the camera's movement, which is
/// what sells depth: distant things appear to move less than near things.
///
/// Attach to each hill layer and set Parallax Factor:
///   0   = locked to the camera (infinitely far away)
///   0.3 = far hills
///   0.6 = near hills
///   1   = moves with the world (no parallax at all)
/// </summary>
public class ParallaxLayer : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float parallaxFactor = 0.3f;

    [Tooltip("Leave on for a side-scroller — vertical parallax on hills usually looks wrong.")]
    [SerializeField] private bool horizontalOnly = true;

    private Transform cam;
    private Vector3 layerStart;
    private Vector3 camStart;

    private void Start()
    {
        Camera main = Camera.main;

        if (main == null)
        {
            Debug.LogError("ParallaxLayer needs a camera tagged MainCamera in the scene.", this);
            enabled = false;
            return;
        }

        cam = main.transform;
        layerStart = transform.position;
        camStart = cam.position;
    }

    // LateUpdate so this runs after the camera has moved for the frame.
    private void LateUpdate()
    {
        Vector3 camDelta = cam.position - camStart;

        float x = layerStart.x + camDelta.x * parallaxFactor;
        float y = horizontalOnly ? layerStart.y : layerStart.y + camDelta.y * parallaxFactor;

        transform.position = new Vector3(x, y, layerStart.z);
    }
}
