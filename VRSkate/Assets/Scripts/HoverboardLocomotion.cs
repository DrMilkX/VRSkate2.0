using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;

/// <summary>
/// HoverboardLocomotion - Attach to your XR Origin GameObject.
///
/// Recreates a frictionless virtual hoverboard locomotion system.
/// - Trigger press while moving forward locks velocity and engages the board
/// - Trigger press again while cruising brakes gradually to a stop
/// - Grip held + wrist roll carves/turns the direction of travel
/// - Crouching while on the board speeds up continuously (lower = faster)
/// - A visible hoverboard model sits at foot level under the player
///
/// Setup:
///   1. Attach this script to your XR Origin
///   2. Assign the InputActionReferences in the Inspector
///   3. Assign your Camera Offset transform
///   4. Optionally assign a HoverboardModel transform (will be created if left empty)
/// </summary>
public class HoverboardLocomotion : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields — assign these in the Unity Inspector
    // -------------------------------------------------------------------------

    [Header("Input Actions")]
    [Tooltip("Right controller trigger — engage/brake")]
    public InputActionReference triggerAction;

    [Tooltip("Right controller grip — hold to carve")]
    public InputActionReference gripAction;

    [Tooltip("Right controller rotation — used for wrist roll carving")]
    public InputActionReference controllerRotationAction;

    [Header("References")]
    [Tooltip("The Camera Offset child of XR Origin")]
    public Transform cameraOffset;

    [Tooltip("The Main Camera (headset transform)")]
    public Transform headset;

    [Tooltip("Optional: assign your pink hoverboard mesh here. " +
             "If left empty, a placeholder cube will be created.")]
    public Transform hoverboardModel;

    [Header("Locomotion Settings")]
    [Tooltip("How many seconds of displacement history to use for velocity sampling")]
    public float velocitySampleDuration = 1.0f;

    [Tooltip("Deceleration rate in m/s² when braking")]
    public float brakeDeceleration = 1.5f;

    [Tooltip("How much crouching amplifies speed. " +
             "Final speed = lockedSpeed * (1 + crouchDelta * this value)")]
    public float crouchSpeedMultiplier = 2.0f;

    [Tooltip("Sensitivity of wrist roll to turning. Degrees of turn per degree of roll per second.")]
    public float carveSensitivity = 1.2f;

    [Tooltip("Threshold grip value (0-1) to consider grip as held")]
    public float gripThreshold = 0.5f;

    [Tooltip("How far below the board model sits relative to the headset Y position")]
    public float boardHeightOffset = -1.0f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    // Locomotion state machine
    private enum BoardState { Idle, Cruising, Braking }
    private BoardState state = BoardState.Idle;

    // Velocity tracking
    private float lockedSpeed = 0f;
    private Vector3 travelDirection = Vector3.forward;

    // Positional history ring buffer for velocity sampling
    private struct PositionSample { public Vector3 position; public float time; }
    private Queue<PositionSample> positionHistory = new Queue<PositionSample>();

    // Crouch tracking
    private float boardEngageHeadHeight = 0f;   // headset Y when board was engaged

    // Wrist roll / carving
    private float previousControllerRoll = 0f;

    // Trigger edge detection (we want press events, not hold)
    private bool triggerWasPressed = false;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        // Auto-find headset if not assigned
        if (headset == null)
            headset = Camera.main.transform;

        // Auto-find cameraOffset if not assigned
        if (cameraOffset == null && headset != null)
            cameraOffset = headset.parent;

        // Create a placeholder board only if no prefab was assigned in the Inspector
        if (hoverboardModel == null)
        {
            Debug.LogWarning("HoverboardLocomotion: No hoverboard model assigned — " +
                             "using pink placeholder. Assign a prefab in the Inspector.");
            hoverboardModel = CreatePlaceholderBoard();
        }
        else
        {
            // If the assigned reference is a project asset (not a scene instance),
            // instantiate it so we have a live scene object to move around
            if (hoverboardModel.gameObject.scene.rootCount == 0)
            {
                hoverboardModel = Instantiate(hoverboardModel);
                hoverboardModel.name = "Hoverboard";
            }
        }

        // Hoverboard starts hidden until board is engaged
        SetBoardVisible(false);

        // Enable input actions (they may not auto-enable depending on your setup)
        triggerAction?.action.Enable();
        gripAction?.action.Enable();
        controllerRotationAction?.action.Enable();
    }

    private void Update()
    {
        RecordPositionHistory();
        HandleTriggerInput();

        switch (state)
        {
            case BoardState.Idle:
                UpdateIdle();
                break;
            case BoardState.Cruising:
                UpdateCruising();
                break;
            case BoardState.Braking:
                UpdateBraking();
                break;
        }

        UpdateBoardModelPosition();
    }

    // -------------------------------------------------------------------------
    // Position history — continuously sampled every frame, old entries pruned
    // -------------------------------------------------------------------------

    private void RecordPositionHistory()
    {
        positionHistory.Enqueue(new PositionSample
        {
            position = headset.position,
            time = Time.time
        });

        // Prune entries older than our sample window
        while (positionHistory.Count > 0 &&
               Time.time - positionHistory.Peek().time > velocitySampleDuration + 0.1f)
        {
            positionHistory.Dequeue();
        }
    }

    /// <summary>
    /// Calculates velocity from positional displacement over the sample duration.
    /// Returns the velocity vector (direction + magnitude) of head movement.
    /// </summary>
    private Vector3 SampleHeadsetVelocity()
    {
        if (positionHistory.Count < 2)
            return Vector3.zero;

        // Find the oldest sample within our window
        PositionSample oldest = default;
        float targetTime = Time.time - velocitySampleDuration;
        bool found = false;

        foreach (var sample in positionHistory)
        {
            if (sample.time >= targetTime)
            {
                oldest = sample;
                found = true;
                break;
            }
        }

        if (!found) return Vector3.zero;

        float elapsed = Time.time - oldest.time;
        if (elapsed <= 0f) return Vector3.zero;

        Vector3 displacement = headset.position - oldest.position;
        return (displacement / elapsed) * 2;  // metres per second
    }

    // -------------------------------------------------------------------------
    // Input handling
    // -------------------------------------------------------------------------

    private void HandleTriggerInput()
    {
        float triggerValue = triggerAction != null
            ? triggerAction.action.ReadValue<float>()
            : 0f;

        bool triggerPressed = triggerValue > 0.8f;

        // Rising edge only — we want a single press event, not continuous hold
        if (triggerPressed && !triggerWasPressed)
        {
            OnTriggerPressed();
        }

        triggerWasPressed = triggerPressed;
    }

    private void OnTriggerPressed()
    {
        switch (state)
        {
            case BoardState.Idle:
                TryEngageBoard();
                break;

            case BoardState.Cruising:
                BeginBraking();
                break;

            case BoardState.Braking:
                // Pressing trigger while already braking cancels the brake
                // and re-locks the current (reduced) speed
                state = BoardState.Cruising;
                break;
        }
    }

    private bool IsGripHeld()
    {
        if (gripAction == null) return false;
        return gripAction.action.ReadValue<float>() > gripThreshold;
    }

    // -------------------------------------------------------------------------
    // State: Idle
    // -------------------------------------------------------------------------

    private void UpdateIdle()
    {
        // Nothing to do while idle — board is hidden, player walks freely
        // Trigger press is handled in HandleTriggerInput → TryEngageBoard
    }

    private void TryEngageBoard()
    {
        Vector3 velocity = SampleHeadsetVelocity();

        // Flatten to horizontal plane — we don't want vertical head bob
        // to contribute to the travel direction
        velocity.y = 0f;

        float speed = velocity.magnitude;

        // Require some minimum forward movement to engage
        // (prevents accidental trigger presses while standing still)
        if (speed < 0.1f)
        {
            Debug.Log("HoverboardLocomotion: Not enough forward movement to engage.");
            return;
        }

        // Lock velocity
        lockedSpeed = speed;
        travelDirection = velocity.normalized;

        // Record head height at engage time for crouch calculation
        boardEngageHeadHeight = headset.position.y;

        // Read initial controller roll to use as carving baseline
        previousControllerRoll = GetControllerRoll();

        state = BoardState.Cruising;
        SetBoardVisible(true);

        Debug.Log($"HoverboardLocomotion: Board engaged at {lockedSpeed:F2} m/s");
    }

    // -------------------------------------------------------------------------
    // State: Cruising
    // -------------------------------------------------------------------------

    private void UpdateCruising()
    {
        HandleCarving();

        float currentSpeed = CalculateCrouchSpeed();
        MovePlayer(travelDirection, currentSpeed);
    }

    /// <summary>
    /// Crouch below the engage height to go faster.
    /// crouchDelta is clamped to >= 0 so standing taller doesn't slow you down.
    /// </summary>
    private float CalculateCrouchSpeed()
    {
        float currentHeadY = headset.position.y;
        float crouchDelta = Mathf.Max(0f, boardEngageHeadHeight - currentHeadY);
        return lockedSpeed * (1f + crouchDelta * crouchSpeedMultiplier);
    }

    // -------------------------------------------------------------------------
    // State: Braking
    // -------------------------------------------------------------------------

    private void BeginBraking()
    {
        state = BoardState.Braking;
        Debug.Log("HoverboardLocomotion: Braking...");
    }

    private void UpdateBraking()
    {
        // Carving still works while braking
        HandleCarving();

        // Decelerate
        lockedSpeed = Mathf.Max(0f, lockedSpeed - brakeDeceleration * Time.deltaTime);

        if (lockedSpeed <= 0f)
        {
            // Full stop
            state = BoardState.Idle;
            SetBoardVisible(false);
            Debug.Log("HoverboardLocomotion: Full stop.");
            return;
        }

        MovePlayer(travelDirection, lockedSpeed);
    }

    // -------------------------------------------------------------------------
    // Carving — grip held + wrist roll rotates travel direction
    // -------------------------------------------------------------------------

    private void HandleCarving()
    {
        if (!IsGripHeld()) 
        {
            // Reset roll baseline when grip is released
            previousControllerRoll = GetControllerRoll();
            return;
        }

        float currentRoll = GetControllerRoll();
        float rollDelta = Mathf.DeltaAngle(previousControllerRoll, currentRoll);
        previousControllerRoll = currentRoll;

        // Rotate the XR Origin around the headset's position on the Y axis
        // so the camera view turns with the carve, not just the travel direction
        float turnAmount = rollDelta * carveSensitivity * Time.deltaTime;

        // Rotate around the headset's world position so the player pivots
        // in place rather than orbiting around the XR Origin's feet pivot
        Vector3 pivotPoint = headset.position;
        transform.RotateAround(pivotPoint, Vector3.up, turnAmount);

        // Keep travel direction in sync with the new forward orientation
        travelDirection = Quaternion.AngleAxis(turnAmount, Vector3.up) * travelDirection;
        travelDirection.y = 0f;
        travelDirection.Normalize();
    }

    /// <summary>
    /// Extracts the roll (Z-axis rotation) from the controller quaternion.
    /// </summary>
    private float GetControllerRoll()
    {
        if (controllerRotationAction == null) return 0f;

        Quaternion rotation = controllerRotationAction.action.ReadValue<Quaternion>();
        // Convert to Euler and return the Z (roll) component
        return rotation.eulerAngles.z;
    }

    // -------------------------------------------------------------------------
    // Movement — moves the XR Origin (not the camera) so the player's
    // view follows along without affecting their ability to look around freely
    // -------------------------------------------------------------------------

    private void MovePlayer(Vector3 direction, float speed)
    {
        Vector3 movement = direction * speed * Time.deltaTime;
        // Move the XR Origin itself, not the headset transform
        transform.position += movement;
    }

    // -------------------------------------------------------------------------
    // Hoverboard model
    // -------------------------------------------------------------------------

    private void UpdateBoardModelPosition()
    {
        if (hoverboardModel == null) return;

        // Position board under the headset horizontally,
        // at a fixed Y offset below headset height
        Vector3 boardPos = headset.position;
        boardPos.y = headset.position.y + boardHeightOffset;
        hoverboardModel.position = boardPos;

        // Board faces the direction of travel when cruising/braking,
        // stays oriented with the player when idle
        if (state != BoardState.Idle && travelDirection != Vector3.zero)
        {
            hoverboardModel.rotation = Quaternion.LookRotation(travelDirection, Vector3.up);
        }
        else
        {
            // Face same horizontal direction as headset when idle
            Vector3 headForward = headset.forward;
            headForward.y = 0f;
            if (headForward != Vector3.zero)
                hoverboardModel.rotation = Quaternion.LookRotation(headForward, Vector3.up);
        }
    }

    private void SetBoardVisible(bool visible)
    {
        if (hoverboardModel != null)
            hoverboardModel.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Creates a simple pink placeholder board if no model is assigned.
    /// Replace with your actual hoverboard mesh.
    /// </summary>
    private Transform CreatePlaceholderBoard()
    {
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "HoverboardPlaceholder";
        board.transform.localScale = new Vector3(0.4f, 0.05f, 0.8f);

        // Hot pink material
        Renderer rend = board.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(1f, 0.08f, 0.58f); // Hot pink — Back to the Future 2 style
        rend.material = mat;

        // Remove collider so it doesn't interfere with physics
        Destroy(board.GetComponent<Collider>());

        return board.transform;
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    private void OnDestroy()
    {
        triggerAction?.action.Disable();
        gripAction?.action.Disable();
        controllerRotationAction?.action.Disable();
    }
}