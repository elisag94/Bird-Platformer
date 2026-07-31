using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Phase 2: bird movement. Walk/run on the ground, flap to get airborne,
/// hold W in the air to glide (reduced gravity).
///
/// Controls:
///   A / D          - move left / right
///   W (tap)        - flap (jump when grounded, one extra flap in the air)
///   W (hold)       - glide while falling
///   Left Shift     - run instead of walk
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("Horizontal movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8.5f;
    [SerializeField] private float groundAcceleration = 60f;
    [SerializeField] private float airAcceleration = 25f;
    [Tooltip("How much steering authority you have while airborne (0 = none, 1 = full).")]
    [SerializeField, Range(0f, 1f)] private float airControl = 0.75f;

    [Header("Flap / glide")]
    [SerializeField] private float jumpImpulse = 9f;
    [Tooltip("Upward impulse for flaps taken while already in the air.")]
    [SerializeField] private float airFlapImpulse = 6.5f;
    [SerializeField] private int maxAirFlaps = 3;
    [SerializeField] private float normalGravityScale = 3f;
    [Tooltip("Gravity scale while holding W and moving downward. Lower = floatier glide.")]
    [SerializeField] private float glideGravityScale = 0.6f;
    [SerializeField] private float maxFallSpeed = 12f;
    [SerializeField] private float maxRiseSpeed = 10f;

    [Header("Ground check")]
    [Tooltip("Local offset of the ground probe. For a 1x1 collider, -0.5 sits at the feet.")]
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, -0.5f);
    [SerializeField] private float groundCheckRadius = 0.18f;
    [Tooltip("Set this to the Ground layer in the Inspector, or nothing will ever be grounded.")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Forgiveness (game feel)")]
    [Tooltip("Grace period after walking off a ledge where a flap still counts as a ground jump.")]
    [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("How early a flap press is remembered before landing.")]
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Visuals")]
    [SerializeField] private bool flipSpriteWithDirection = true;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    private float horizontalInput;
    private bool runHeld;
    private bool flapHeld;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private int airFlapsUsed;
    private bool inputEnabled = true;

    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.freezeRotation = true;          // a physics bird that tips over is not fun
        rb.gravityScale = normalGravityScale;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (groundLayer.value == 0)
        {
            Debug.LogWarning(
                "PlayerController2D: Ground Layer mask is empty. Set it to 'Ground' in the Inspector " +
                "or the bird will never be able to flap off the ground.", this);
        }
    }

    private void OnEnable()
    {
        GameManager.StateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameManager.StateChanged -= HandleGameStateChanged;
    }

    private void HandleGameStateChanged(GameManager.GameState state)
    {
        SetInputEnabled(state == GameManager.GameState.Playing);
    }

    /// <summary>Called by the GameManager on win/lose so the bird stops responding.</summary>
    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (!enabled)
        {
            horizontalInput = 0f;
            flapHeld = false;
            runHeld = false;
            jumpBufferCounter = 0f;
        }
    }

    // Input is read in Update (once per frame, so no presses are missed),
    // physics is applied in FixedUpdate. Keeping these separate is the standard
    // Unity pattern and avoids input being swallowed on slow frames.
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || !inputEnabled)
        {
            horizontalInput = 0f;
            flapHeld = false;
            return;
        }

        float right = keyboard.dKey.isPressed ? 1f : 0f;
        float left = keyboard.aKey.isPressed ? 1f : 0f;
        horizontalInput = right - left;

        runHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        flapHeld = keyboard.wKey.isPressed;

        if (keyboard.wKey.wasPressedThisFrame)
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }
    }

    private void FixedUpdate()
    {
        UpdateGrounded();
        ApplyFlap();
        ApplyHorizontalMovement();
        ApplyGravityAndClamps();
        UpdateSpriteFacing();
    }

    private void UpdateGrounded()
    {
        Vector2 probe = (Vector2)transform.position + groundCheckOffset;
        IsGrounded = Physics2D.OverlapCircle(probe, groundCheckRadius, groundLayer);

        if (IsGrounded)
        {
            coyoteCounter = coyoteTime;
            airFlapsUsed = 0;
        }
        else
        {
            coyoteCounter -= Time.fixedDeltaTime;
        }
    }

    private void ApplyFlap()
    {
        if (jumpBufferCounter <= 0f)
        {
            return;
        }

        bool canGroundJump = coyoteCounter > 0f;
        bool canAirFlap = !canGroundJump && airFlapsUsed < maxAirFlaps;

        if (!canGroundJump && !canAirFlap)
        {
            return;
        }

        float impulse = canGroundJump ? jumpImpulse : airFlapImpulse;

        // Zero out downward velocity first so a flap always feels the same
        // whether you're rising, falling, or standing still.
        Vector2 v = rb.linearVelocity;
        v.y = Mathf.Max(0f, v.y);
        rb.linearVelocity = v;

        rb.AddForce(Vector2.up * impulse, ForceMode2D.Impulse);

        if (!canGroundJump)
        {
            airFlapsUsed++;
        }

        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
    }

    private void ApplyHorizontalMovement()
    {
        float targetSpeed = horizontalInput * (runHeld ? runSpeed : walkSpeed);

        if (!IsGrounded)
        {
            targetSpeed *= airControl;
        }

        float acceleration = IsGrounded ? groundAcceleration : airAcceleration;

        float newX = Mathf.MoveTowards(
            rb.linearVelocity.x,
            targetSpeed,
            acceleration * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
    }

    private void ApplyGravityAndClamps()
    {
        // Glide only applies while falling — holding W on the way up shouldn't
        // turn the flap into a rocket.
        bool gliding = flapHeld && !IsGrounded && rb.linearVelocity.y < 0f;
        rb.gravityScale = gliding ? glideGravityScale : normalGravityScale;

        float clampedY = Mathf.Clamp(rb.linearVelocity.y, -maxFallSpeed, maxRiseSpeed);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, clampedY);
    }

    private void UpdateSpriteFacing()
    {
        if (!flipSpriteWithDirection || spriteRenderer == null)
        {
            return;
        }

        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            spriteRenderer.flipX = horizontalInput < 0f;
        }
    }

    // Draws the ground probe in the Scene view when the bird is selected.
    // If this circle isn't touching the ground sprite, grounding will never fire.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere((Vector2)transform.position + groundCheckOffset, groundCheckRadius);
    }
}