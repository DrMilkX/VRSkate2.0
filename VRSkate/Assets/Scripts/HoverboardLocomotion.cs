using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
// using UnityEngine.InputSystem;

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
///   2. Assign your Camera Offset transform
///   3. Optionally assign a HoverboardModel transform (will be created if left empty)
/// </summary>
public class HoverboardLocomotion : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields — assign these in the Unity Inspector
    // -------------------------------------------------------------------------

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

    [Tooltip("Multiplier applied to the player's velocity when the board engages. " +
             "1.5 = 50% speed boost on launch.")]
    public float engageSpeedMultiplier = 1.5f;

    [Tooltip("How sharply the board turns per degree of wrist roll. " +
             "Roll is measured from upright (0°) — bigger tilt = faster continuous turn.")]
    public float carveSensitivity = 1.2f;

    [Tooltip("How quickly rotational velocity fades to zero after grip is released. " +
             "Higher = snappier stop, lower = longer coast.")]
    public float carveDamping = 6f;

    [Tooltip("Threshold grip value (0-1) to consider grip as held")]
    public float gripThreshold = 0.5f;

    [Tooltip("How high the board floats above the ground surface")]
    public float boardHoverHeight = 0.05f;

    [Tooltip("Layer mask for ground detection raycast. Set to your floor/terrain layer.")]
    public LayerMask groundMask = ~0;   // default: everything

    [Header("Collision Safety")]
    [Tooltip("How far ahead to check for walls while on the board (metres)")]
    public float wallDetectionDistance = 0.6f;

    [Tooltip("Layer mask for wall detection. Exclude the player and floor layers.")]
    public LayerMask wallMask = ~0;

    [Tooltip("A surface is treated as a wall if its normal's Y component is below this value. " +
             "Keeps floors from triggering a dismount (0.5 works well for most geometry).")]
    public float wallNormalThreshold = 0.5f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    // Locomotion state machine
    private enum BoardState { Idle, Cruising, Braking }
    private BoardState state = BoardState.Idle;

    // Velocity tracking
    private float lockedSpeed = 0f;
    private float speed_speed = 0f;
    private Vector3 travelDirection = Vector3.forward;

    // Positional history ring buffer for velocity sampling
    private struct PositionSample { public Vector3 position; public float time; }
    private Queue<PositionSample> positionHistory = new Queue<PositionSample>();

    // Crouch tracking
    private float boardEngageHeadHeight = 0f;   // headset Y when board was engaged

    // Current rotational velocity (degrees/sec) — decays smoothly on grip release
    private float currentTurnRate = 0f;

    // Trigger edge detection (we want press events, not hold)
    private bool triggerWasPressed = false;

    // Raw XR device polling — lets either controller engage/brake/carve
    private List<UnityEngine.XR.InputDevice> leftControllers = new List<UnityEngine.XR.InputDevice>();
    private List<UnityEngine.XR.InputDevice> rightControllers = new List<UnityEngine.XR.InputDevice>();

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

        if (UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame == true)
        {
            EBrake();
        }
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
        return displacement / elapsed;  // metres per second
    }

    // -------------------------------------------------------------------------
    // Input handling
    // -------------------------------------------------------------------------

    private void RefreshControllerDevices()
    {
        if (leftControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
                leftControllers);

        if (rightControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightControllers);
    }

    /// <summary>
    /// Highest trigger value across both controllers, so either hand can engage/brake.
    /// </summary>
    private float GetCombinedTriggerValue()
    {
        RefreshControllerDevices();

        float value = 0f;
        foreach (var device in leftControllers)
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float v))
                value = Mathf.Max(value, v);
        foreach (var device in rightControllers)
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float v))
                value = Mathf.Max(value, v);

        return value;
    }

    private void HandleTriggerInput()
    {
        float triggerValue = GetCombinedTriggerValue();

// change t//o //TODO 
// GetCombinedTriggerValue
        bool triggerPressed = triggerValue > 0.5f;

        // Rising edge only — we want a single press event, not continuous hold
        if (triggerPressed && !triggerWasPressed)
        {
            OnTriggerPressed();
        }
        else if (!triggerPressed && triggerWasPressed)
        {
            OnTriggerReleased();
        }

        triggerWasPressed = triggerPressed;
    }

// TODO: add grip + trigger held = start, slowly release them to start braking, pressing A to start rotating/turning.
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

    private void OnTriggerReleased()
    {
        switch (state)
        {
            case BoardState.Braking:
                // Re-lock current speed and return to cruising
                lockedSpeed = speed_speed;
                state = BoardState.Cruising;
                break;
        }
    }

    private bool IsGripHeld()
    {
        RefreshControllerDevices();

        foreach (var device in leftControllers)
            if (device.TryGetFeatureValue(CommonUsages.grip, out float v) && v > gripThreshold)
                return true;
        foreach (var device in rightControllers)
            if (device.TryGetFeatureValue(CommonUsages.grip, out float v) && v > gripThreshold)
                return true;

        return false;
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
        // velocity.y = 0f;

        // use either vertical or horizontal velocity (use the faster of the two and zero the other)
        if (Mathf.Abs(velocity.y) > Mathf.Abs(velocity.x) && Mathf.Abs(velocity.y) > Mathf.Abs(velocity.z))
        {
            velocity.x = 0f;
            velocity.z = 0f;
        }
        else
        {
            velocity.y = 0f;
        }

        float speed = Mathf.Abs(velocity.magnitude);

        // boost speed if moving mostly vertically (crouch activation)
        // if (velocity.x == 0f || velocity.z == 0f)
        // {
        //     speed *= 1.5f; 
        // }

        // Require some minimum forward movement to engage
        // (prevents accidental trigger presses while standing still)
        if (speed < 0.1f)
        {
            Debug.Log("HoverboardLocomotion: Not enough forward movement to engage.");
            return;
        }

        // Lock velocity with engage boost
        lockedSpeed = speed * engageSpeedMultiplier;
        // travelDirection = velocity.normalized;

        // use the headset's forward direction projected onto the horizontal plane as the travel direction
        travelDirection = headset.forward;
        travelDirection.y = 0f;

        // Record head height at engage time for crouch calculation
        boardEngageHeadHeight = headset.position.y;

        state = BoardState.Cruising;
        SetBoardVisible(true);

        Debug.Log($"HoverboardLocomotion: Board engaged at {lockedSpeed:F2} m/s");
    }

    // -------------------------------------------------------------------------
    // State: Cruising
    // -------------------------------------------------------------------------

    private void UpdateCruising()
    {
        if (CheckWallAhead()) return;

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
        if (CheckWallAhead()) return;

        // Carving still works while braking
        HandleCarving();

        // Update speed with deceleration and allow crouch to amplify it as well (same as cruising)
        boardEngageHeadHeight = headset.position.y;

        // Decelerate
        float triggerValue = GetCombinedTriggerValue();
        speed_speed = Mathf.Max(0f, speed_speed - (brakeDeceleration * (triggerValue+0.75f)) * Time.deltaTime);

        if (speed_speed <= 0f)
        {
            state = BoardState.Idle;
            currentTurnRate = 0f;
            SetBoardVisible(false);
            Debug.Log("HoverboardLocomotion: Full stop.");
            return;
        }

        MovePlayer(travelDirection, speed_speed);
    }

    public void EBrake()
    {
        state = BoardState.Idle;
        currentTurnRate = 0f;
        lockedSpeed = 0f;
        speed_speed = 0f;
        SetBoardVisible(false);
        Debug.Log("HoverboardLocomotion: Emergency brake — dismounted.");
    }

    // -------------------------------------------------------------------------
    // Wall detection — dismounts the player on imminent vertical collision
    // -------------------------------------------------------------------------

    /// <summary>
    /// Casts a ray forward along the travel direction from headset height.
    /// Returns true (and triggers dismount) if a vertical wall is detected ahead.
    /// </summary>
    private bool CheckWallAhead()
    {
        // Ray origin at headset position, direction = travel direction (horizontal)
        Vector3 origin = headset.position;
        Vector3 dir = travelDirection;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, wallDetectionDistance, wallMask))
        {
            // Only react to surfaces that are roughly vertical (not the floor)
            if (Mathf.Abs(hit.normal.y) < wallNormalThreshold)
            {
                DismountFromWall();
                return true;
            }
        }

        return false;
    }

    private void DismountFromWall()
    {
        lockedSpeed = 0f;
        currentTurnRate = 0f;
        state = BoardState.Idle;
        SetBoardVisible(false);
        Debug.Log("HoverboardLocomotion: Wall collision — dismounted.");
    }

    // -------------------------------------------------------------------------
    // Carving — grip held + wrist roll rotates travel direction
    // -------------------------------------------------------------------------

    private void HandleCarving()
    {
        if (IsGripHeld())
        {
            // Signed roll from upright: positive = tilted right, negative = tilted left.
            float signedRoll = Mathf.DeltaAngle(0f, GetControllerRoll());
            currentTurnRate = -signedRoll * carveSensitivity;
        }
        else
        {
            // Smoothly decay turn rate to zero after grip released
            currentTurnRate = Mathf.Lerp(currentTurnRate, 0f, carveDamping * Time.deltaTime);
        }

        if (Mathf.Abs(currentTurnRate) < 0.01f) return;

        float turnAmount = currentTurnRate * Time.deltaTime;
        transform.RotateAround(headset.position, Vector3.up, turnAmount);

        travelDirection = Quaternion.AngleAxis(turnAmount, Vector3.up) * travelDirection;
        travelDirection.y = 0f;
        travelDirection.Normalize();
    }

    /// <summary>
    /// Extracts the roll (Z-axis rotation) from whichever controller has the highest
    /// grip value, so carving works from either hand.
    /// </summary>
    private float GetControllerRoll()
    {
        RefreshControllerDevices();

        float bestGrip = -1f;
        float roll = 0f;

        void Consider(List<InputDevice> devices)
        {
            foreach (var device in devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.grip, out float grip) &&
                    grip > gripThreshold && grip > bestGrip &&
                    device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                {
                    bestGrip = grip;
                    roll = rotation.eulerAngles.z;
                }
            }
        }

        Consider(leftControllers);
        Consider(rightControllers);

        return roll;
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
        speed_speed = speed;
    }

    // -------------------------------------------------------------------------
    // Hoverboard model
    // -------------------------------------------------------------------------

    private void UpdateBoardModelPosition()
    {
        if (hoverboardModel == null) return;

        // Follow headset horizontally
        Vector3 boardPos = headset.position;

        // Raycast straight down from headset height to find the ground surface
        if (Physics.Raycast(new Vector3(boardPos.x, boardPos.y, boardPos.z),
                            Vector3.down, out RaycastHit hit, 10f, groundMask))
        {
            boardPos.y = hit.point.y + boardHoverHeight;
        }
        else
        {
            // Fallback if raycast misses (e.g. player is above a gap)
            boardPos.y = 0f + boardHoverHeight;
        }

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
}