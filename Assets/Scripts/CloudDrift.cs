using UnityEngine;

/// <summary>
/// Slides a cloud sideways and wraps it back to the start, the Unity equivalent
/// of the `drift` CSS keyframe on the coming-soon page.
///
/// Works whether the cloud is a world-space Sprite Renderer (parent it to the
/// camera) or a UI Image on a Canvas — it moves localPosition either way. The
/// units differ though: world space is metres, UI is canvas pixels, so a UI
/// cloud needs much larger Speed / Wrap values.
/// </summary>
public class CloudDrift : MonoBehaviour
{
    [Tooltip("Units per second. Try 0.4 in world space, 25 on a Canvas.")]
    [SerializeField] private float speed = 0.4f;

    [Tooltip("Local X at which the cloud jumps back to Reset To X.")]
    [SerializeField] private float wrapAtX = 14f;

    [SerializeField] private float resetToX = -14f;

    [Tooltip("Start somewhere random along the path so clouds don't move in lockstep.")]
    [SerializeField] private bool randomiseStart = true;

    private void Start()
    {
        if (!randomiseStart)
        {
            return;
        }

        Vector3 p = transform.localPosition;
        p.x = Random.Range(resetToX, wrapAtX);
        transform.localPosition = p;
    }

    private void Update()
    {
        Vector3 p = transform.localPosition;
        p.x += speed * Time.deltaTime;

        if (p.x > wrapAtX)
        {
            p.x = resetToX;
        }

        transform.localPosition = p;
    }
}
