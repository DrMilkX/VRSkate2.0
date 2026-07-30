using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class HoverboardLocomotionAlt : MonoBehaviour
{
    [Header("References")]
    public Transform cameraOffset;
    public Transform headset;
    public Transform hoverboardModel;

    [Header("Locomotion Settings")]
    public float velocitySampleDuration = 1.0f;
    public float brakeDeceleration = 1.5f;
    public float crouchSpeedMultiplier = 2.0f;
    public float engageSpeedMultiplier = 1.5f;
    public float carveSensitivity = 45.0f; // Adjusted for button-based turning (degrees per second)
    public float carveDamping = 6f;
    public float boardHoverHeight = 0.05f;
    public LayerMask groundMask = ~0;

    [Header("Collision Safety")]
    public float wallDetectionDistance = 0.6f;
    public LayerMask wallMask = ~0;
    public float wallNormalThreshold = 0.5f;

    private enum BoardState { Idle, Active }
    private BoardState state = BoardState.Idle;

    private float lockedSpeed = 0f;
    private float currentSpeed = 0f;
    private Vector3 travelDirection = Vector3.forward;
    
    private struct PositionSample { public Vector3 position; public float time; }
    private Queue<PositionSample> positionHistory = new Queue<PositionSample>();

    private float boardEngageHeadHeight = 0f;
    private float currentTurnRate = 0f;

    private List<InputDevice> rightControllers = new List<InputDevice>();

    private void Start()
    {
        if (headset == null) headset = Camera.main.transform;
        if (cameraOffset == null && headset != null) cameraOffset = headset.parent;

        if (hoverboardModel == null)
        {
            hoverboardModel = CreatePlaceholderBoard();
        }
        else if (hoverboardModel.gameObject.scene.rootCount == 0)
        {
            hoverboardModel = Instantiate(hoverboardModel);
            hoverboardModel.name = "Hoverboard";
        }

        SetBoardVisible(false);
    }

    private void Update()
    {
        RecordPositionHistory();
        HandleLocomotionInput();

        if (state == BoardState.Active)
        {
            if (CheckWallAhead()) return;
            HandleCarving();
            MovePlayer();
        }

        UpdateBoardModelPosition();
    }

    private void RecordPositionHistory()
    {
        positionHistory.Enqueue(new PositionSample { position = headset.position, time = Time.time });

        while (positionHistory.Count > 0 && Time.time - positionHistory.Peek().time > velocitySampleDuration + 0.1f)
        {
            positionHistory.Dequeue();
        }
    }

    private Vector3 SampleHeadsetVelocity()
    {
        if (positionHistory.Count < 2) return Vector3.zero;

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

        return (headset.position - oldest.position) / elapsed;
    }

    private void RefreshControllerDevices()
    {
        if (rightControllers.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightControllers);
        }
    }

    private void HandleLocomotionInput()
    {
        RefreshControllerDevices();

        float grip = 0f;
        float trigger = 0f;

        foreach (var device in rightControllers)
        {
            if (device.TryGetFeatureValue(CommonUsages.grip, out float g)) grip = Mathf.Max(grip, g);
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float t)) trigger = Mathf.Max(trigger, t);
        }

        // Calculate average hold to determine progressive braking
        float averageHold = (grip + trigger) / 2f;

        if (state == BoardState.Idle)
        {
            // Grip + Trigger Held = Start
            if (averageHold > 0.9f)
            {
                TryEngageBoard();
            }
        }
        else if (state == BoardState.Active)
        {
            // Slowly release to brake: target speed scales with how hard you hold the triggers
            float crouchMultiplier = CalculateCrouchMultiplier();
            float maxPossibleSpeed = lockedSpeed * crouchMultiplier;
            float targetSpeed = maxPossibleSpeed * averageHold;

            // Smoothly brake down to the target speed based on trigger release
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, brakeDeceleration * Time.deltaTime);

            if (currentSpeed <= 0.05f && averageHold < 0.1f)
            {
                state = BoardState.Idle;
                currentSpeed = 0f;
                SetBoardVisible(false);
            }
        }
    }

    private void TryEngageBoard()
    {
        Vector3 velocity = SampleHeadsetVelocity();

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

        if (speed < 0.1f) return;

        lockedSpeed = speed * engageSpeedMultiplier;
        currentSpeed = lockedSpeed;
        
        travelDirection = headset.forward;
        travelDirection.y = 0f;
        travelDirection.Normalize();

        boardEngageHeadHeight = headset.position.y;
        state = BoardState.Active;
        SetBoardVisible(true);
    }

    private float CalculateCrouchMultiplier()
    {
        float currentHeadY = headset.position.y;
        float crouchDelta = Mathf.Max(0f, boardEngageHeadHeight - currentHeadY);
        return 1f + (crouchDelta * crouchSpeedMultiplier);
    }

    private void HandleCarving()
    {
        bool isAPressed = false;
        foreach (var device in rightControllers)
        {
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed) && aPressed)
            {
                isAPressed = true;
                break;
            }
        }

        // Pressing A starts rotating
        if (isAPressed)
        {
            currentTurnRate = carveSensitivity;
        }
        else
        {
            currentTurnRate = Mathf.Lerp(currentTurnRate, 0f, carveDamping * Time.deltaTime);
        }

        if (Mathf.Abs(currentTurnRate) < 0.01f) return;

        float turnAmount = currentTurnRate * Time.deltaTime;
        transform.RotateAround(headset.position, Vector3.up, turnAmount);

        travelDirection = Quaternion.AngleAxis(turnAmount, Vector3.up) * travelDirection;
        travelDirection.y = 0f;
        travelDirection.Normalize();
    }

    private void MovePlayer()
    {
        transform.position += travelDirection * currentSpeed * Time.deltaTime;
    }

    private bool CheckWallAhead()
    {
        if (Physics.Raycast(headset.position, travelDirection, out RaycastHit hit, wallDetectionDistance, wallMask))
        {
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
        currentSpeed = 0f;
        currentTurnRate = 0f;
        state = BoardState.Idle;
        SetBoardVisible(false);
    }

    private void UpdateBoardModelPosition()
    {
        if (hoverboardModel == null) return;

        Vector3 boardPos = headset.position;

        if (Physics.Raycast(new Vector3(boardPos.x, boardPos.y, boardPos.z), Vector3.down, out RaycastHit hit, 10f, groundMask))
        {
            boardPos.y = hit.point.y + boardHoverHeight;
        }
        else
        {
            boardPos.y = 0f + boardHoverHeight;
        }

        hoverboardModel.position = boardPos;

        if (state == BoardState.Active && travelDirection != Vector3.zero)
        {
            hoverboardModel.rotation = Quaternion.LookRotation(travelDirection, Vector3.up);
        }
        else
        {
            Vector3 headForward = headset.forward;
            headForward.y = 0f;
            if (headForward != Vector3.zero)
                hoverboardModel.rotation = Quaternion.LookRotation(headForward, Vector3.up);
        }
    }

    private void SetBoardVisible(bool visible)
    {
        if (hoverboardModel != null) hoverboardModel.gameObject.SetActive(visible);
    }

    private Transform CreatePlaceholderBoard()
    {
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "HoverboardPlaceholder";
        board.transform.localScale = new Vector3(0.4f, 0.05f, 0.8f);

        Renderer rend = board.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(1f, 0.08f, 0.58f); 
        rend.material = mat;

        Destroy(board.GetComponent<Collider>());
        return board.transform;
    }
}