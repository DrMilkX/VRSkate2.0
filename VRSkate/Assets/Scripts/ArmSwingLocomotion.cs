using UnityEngine;

/// <summary>
/// ArmSwingLocomotion — Attach to XR Origin.
///
/// Moves the player forward based on how fast they swing their controllers.
/// Works for both walking-in-place (alternating swing) and jogging (faster swing).
/// Movement direction follows the headset forward, flattened to horizontal.
///
/// Setup:
///   1. Attach to XR Origin
///   2. Assign Left Controller and Right Controller transforms in the Inspector
///   3. Add as a Locomotion Option entry in LocomotionSwitcher
/// </summary>
public class ArmSwingLocomotion : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("References")]
    [Tooltip("Main Camera / headset — movement direction source")]
    public Transform headset;

    [Tooltip("Left controller transform (drag in Left Controller GameObject)")]
    public Transform leftController;

    [Tooltip("Right controller transform (drag in Right Controller GameObject)")]
    public Transform rightController;

    [Header("Swing Settings")]
    [Tooltip("Scales arm swing speed into movement speed. " +
             "Higher = faster movement for the same swing intensity.")]
    public float speedMultiplier = 2.5f;

    [Tooltip("Minimum combined swing speed (m/s) before any movement is applied. " +
             "Prevents drift from micro hand tremors.")]
    public float deadzone = 0.4f;

    [Tooltip("Maximum movement speed in m/s regardless of swing speed.")]
    public float maxSpeed = 5f;

    [Tooltip("How quickly movement speed ramps up when you start swinging (m/s²).")]
    public float acceleration = 10f;

    [Tooltip("How quickly movement speed ramps down when you stop swinging (m/s²).")]
    public float deceleration = 6f;

    [Tooltip("Bonus multiplier when controllers swing in opposite directions (alternating walk). " +
             "1.0 = no bonus, 1.3 = 30% speed boost for correct alternating form.")]
    [Range(1f, 2f)]
    public float alternatingBonus = 1.25f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private Vector3 leftPrevPos;
    private Vector3 rightPrevPos;
    private float smoothedSpeed = 0f;
    private bool prevPosInitialized = false;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void OnEnable()
    {
        // Reset state when switching to this mode so there's no speed carry-over
        smoothedSpeed = 0f;
        prevPosInitialized = false;

        // disable the controller's teleport interactor and enable trhe near-far interactor
        // if (leftController != null)
        // {
        //     var leftTeleport = leftController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        //     if (leftTeleport != null) leftTeleport.enabled = false; 
        // }
        // if (rightController != null)
        // {
        //     var rightTeleport = rightController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        //     if (rightTeleport != null) rightTeleport.enabled = false; 
        // }
    }

    private void Start()
    {
        if (headset == null)
            headset = Camera.main.transform;
    }

    private void Update()
    {
        // Skip first frame after enable so prev positions are valid
        if (!prevPosInitialized)
        {
            if (leftController  != null) leftPrevPos  = leftController.position;
            if (rightController != null) rightPrevPos = rightController.position;
            prevPosInitialized = true;
            return;
        }

        float targetSpeed = CalculateTargetSpeed();

        // Ramp up quickly, ramp down more gently for natural feel
        float rate = targetSpeed > smoothedSpeed ? acceleration : deceleration;
        smoothedSpeed = Mathf.MoveTowards(smoothedSpeed, targetSpeed, rate * Time.deltaTime);

        if (smoothedSpeed > 0.01f)
        {
            Vector3 forward = headset.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
            {
                forward.Normalize();
                transform.position += forward * smoothedSpeed * Time.deltaTime;
            }
        }

        // Store positions for next frame
        if (leftController  != null) leftPrevPos  = leftController.position;
        if (rightController != null) rightPrevPos = rightController.position;
    }

    // -------------------------------------------------------------------------
    // Speed calculation
    // -------------------------------------------------------------------------

    private float CalculateTargetSpeed()
    {
        float leftY  = 0f;
        float rightY = 0f;

        if (leftController != null)
        {
            Vector3 vel = (leftController.position - leftPrevPos) / Time.deltaTime;
            leftY = Mathf.Abs(vel.y);   // vertical swing only — immune to turning arcs
        }
        if (rightController != null)
        {
            Vector3 vel = (rightController.position - rightPrevPos) / Time.deltaTime;
            rightY = Mathf.Abs(vel.y);
        }

        // Both hands must be moving — single-hand noise (turning, gesturing) is ignored
        if (leftY < deadzone || rightY < deadzone)
            return 0f;

        float combinedSpeed = (leftY + rightY) * 0.5f * speedMultiplier;

        // Bonus for true alternating swing (one hand up while other is down)
        float leftRawY  = leftController  != null ? (leftController.position  - leftPrevPos).y  / Time.deltaTime : 0f;
        float rightRawY = rightController != null ? (rightController.position - rightPrevPos).y / Time.deltaTime : 0f;
        bool alternating = (leftRawY > 0f && rightRawY < 0f) || (leftRawY < 0f && rightRawY > 0f);
        if (alternating)
            combinedSpeed *= alternatingBonus;

        return Mathf.Min(combinedSpeed, maxSpeed);
    }
}
