using UnityEngine;

/// <summary>
/// CameraRig — three camera modes with smooth follow.
///
/// Modes (ADR-011):
///   Isometric    — 45° angle, smooth follow. Default.
///   TopDown      — near-overhead, wider FOV, most readable.
///   ThirdPerson  — behind and above player, most immersive.
///
/// Mode toggled via SetMode() — called by HUD camera button (feature/hud-and-polish).
/// Preference persisted to PlayerPrefs so it survives between sessions on same device.
///
/// Attach to: Main Camera in the scene.
/// The camera follows the player tagged "Player" — auto-connects at runtime.
/// No manual wiring needed — same pattern as UIFeedback player anchor.
/// </summary>
public class CameraRig : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // CAMERA MODE
    // ─────────────────────────────────────────────────────────────────────────

    public enum CameraMode { Isometric, TopDown, ThirdPerson }

    /// Current active camera mode. Read by HUD to show correct toggle icon.
    public CameraMode CurrentMode { get; private set; } = CameraMode.Isometric;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Follow Settings")]
    [Tooltip("How quickly the camera follows the player. From GameConstants.")]
    [SerializeField] private float followSpeed = 4f;

    [Tooltip("Height offset above the player for all camera modes.")]
    [SerializeField] private float heightOffset = 2f;

    [Header("Isometric Settings")]
    [Tooltip("Isometric camera angle in degrees (X rotation).")]
    [SerializeField] private float isometricAngle = 45f;

    [Tooltip("Isometric camera distance from player.")]
    [SerializeField] private float isometricDistance = 20f;

    [Header("Top-Down Settings")]
    [Tooltip("Top-down camera angle in degrees (X rotation, near 90).")]
    [SerializeField] private float topDownAngle = 70f;

    [Tooltip("Top-down camera height above player.")]
    [SerializeField] private float topDownHeight = 25f;

    [Header("Third-Person Settings")]
    [Tooltip("Distance behind player for third-person mode.")]
    [SerializeField] private float thirdPersonDistance = 8f;

    [Tooltip("Height above player for third-person mode.")]
    [SerializeField] private float thirdPersonHeight = 4f;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// The player transform being followed. Resolved each frame via tag.
    private Transform _playerTransform;

    /// Cached Camera component for FOV adjustments.
    private Camera _camera;

    /// Last known player heading for third-person offset calculation.
    private Vector3 _lastPlayerForward = Vector3.forward;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        // Restore saved camera mode preference.
        int savedMode = PlayerPrefs.GetInt(GameConstants.CameraModePrefKey, 0);
        SetMode((CameraMode)savedMode, save: false);
    }

    private void LateUpdate()
    {
        ResolvePlayerTransform();
        if (_playerTransform == null) return;

        // Track player heading for third-person offset.
        Vector3 vel = _playerTransform.GetComponent<Rigidbody>()?.linearVelocity
                      ?? Vector3.zero;
        if (vel.magnitude > 0.5f)
            _lastPlayerForward = new Vector3(vel.x, 0f, vel.z).normalized;

        FollowPlayer();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the camera mode and optionally saves to PlayerPrefs.
    /// Called by HUD camera toggle button.
    /// Cycles through modes in order: Isometric → TopDown → ThirdPerson → Isometric.
    /// </summary>
    public void CycleMode()
    {
        CameraMode next = CurrentMode switch
        {
            CameraMode.Isometric => CameraMode.TopDown,
            CameraMode.TopDown => CameraMode.ThirdPerson,
            CameraMode.ThirdPerson => CameraMode.Isometric,
            _ => CameraMode.Isometric
        };
        SetMode(next);
    }

    /// <summary>
    /// Sets a specific camera mode directly.
    /// </summary>
    public void SetMode(CameraMode mode, bool save = true)
    {
        CurrentMode = mode;
        if (save)
            PlayerPrefs.SetInt(GameConstants.CameraModePrefKey, (int)mode);

        Debug.Log($"[CameraRig] Mode set to {mode}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOLLOW LOGIC
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the player transform each frame via FindWithTag.
    /// Falls back gracefully if no player exists yet.
    /// Auto-connects when the player spawns — no manual wiring needed.
    /// </summary>
    private void ResolvePlayerTransform()
    {
        if (_playerTransform != null) return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
            _playerTransform = player.transform;
    }

    /// <summary>
    /// Smoothly moves the camera to its target position each LateUpdate.
    /// Target position and rotation depend on the current CameraMode.
    /// LateUpdate ensures the camera moves after player physics resolve.
    /// </summary>
    private void FollowPlayer()
    {
        Vector3 targetPos = Vector3.zero;
        Quaternion targetRot = Quaternion.identity;

        switch (CurrentMode)
        {
            case CameraMode.Isometric:
                targetPos = GetIsometricPosition();
                targetRot = Quaternion.Euler(isometricAngle, 45f, 0f);
                break;

            case CameraMode.TopDown:
                targetPos = GetTopDownPosition();
                targetRot = Quaternion.Euler(topDownAngle, 0f, 0f);
                break;

            case CameraMode.ThirdPerson:
                targetPos = GetThirdPersonPosition();
                targetRot = GetThirdPersonRotation();
                break;
        }

        // Smooth follow — lerp position and rotation each frame.
        transform.position = Vector3.Lerp(
            transform.position, targetPos,
            followSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Lerp(
            transform.rotation, targetRot,
            followSpeed * Time.deltaTime);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POSITION CALCULATORS
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 GetIsometricPosition()
    {
        // Fixed 45° isometric offset — NE of player looking SW.
        Vector3 offset = new Vector3(-1f, 1f, -1f).normalized * isometricDistance;
        return _playerTransform.position + offset
               + Vector3.up * heightOffset;
    }

    private Vector3 GetTopDownPosition()
    {
        // Directly above the player.
        return new Vector3(
            _playerTransform.position.x,
            _playerTransform.position.y + topDownHeight,
            _playerTransform.position.z);
    }

    private Vector3 GetThirdPersonPosition()
    {
        // Behind the player based on last movement direction.
        Vector3 back = -_lastPlayerForward;
        return _playerTransform.position
               + back * thirdPersonDistance
               + Vector3.up * thirdPersonHeight;
    }

    private Quaternion GetThirdPersonRotation()
    {
        // Look toward the player from the third-person position.
        Vector3 dir = (_playerTransform.position + Vector3.up * heightOffset)
                      - transform.position;
        if (dir == Vector3.zero) return transform.rotation;
        return Quaternion.LookRotation(dir);
    }
}