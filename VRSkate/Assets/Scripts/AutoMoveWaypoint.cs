using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

public class AutoLocomotion : MonoBehaviour
{
    [Header("Route")]
    public GameObject[] waypoints;
    public float speed = 10.0f;
    public float rotationSpeed = 10.0f;
    public float arrivalDistance = 0.15f;
    public bool loopWaypoints = true;

    [Header("Input")]
    public InputActionReference advanceAction;

    [Header("VR Rig")]
    [Tooltip("Leave empty to auto-detect on this GameObject")]
    public XROrigin xrOrigin;

    private int currentWP = 0;
    private bool waitingForTrigger = false;
    private bool advanceWasPressed = false;

    void Start()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("AutoLocomotion: No waypoints assigned.", this);
            enabled = false;
            return;
        }

        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        currentWP = Mathf.Clamp(currentWP, 0, waypoints.Length - 1);

        if (advanceAction != null)
            advanceAction.action.Enable();
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0)
            return;

        HandleAdvanceInput();

        if (waitingForTrigger)
            return;

        Transform target = waypoints[currentWP].transform;
        
        // 1. Calculate physical room offset (ignore Y axis to prevent sinking into the floor)
        Vector3 cameraOffset = xrOrigin.Camera.transform.position - xrOrigin.transform.position;
        cameraOffset.y = 0; 
        
        // 2. Adjust target position so the HEAD arrives at the waypoint, not the floor origin
        Vector3 targetPosition = target.position - cameraOffset;

        // Flatten distance check to X/Z plane 
        Vector3 currentPosFlat = new Vector3(xrOrigin.transform.position.x, 0, xrOrigin.transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetPosition.x, 0, targetPosition.z);

        if (Vector3.Distance(currentPosFlat, targetPosFlat) <= arrivalDistance)
        {
            xrOrigin.transform.position = targetPosition;
            waitingForTrigger = true;
            return;
        }

        // 3. Smoothly rotate the rig AROUND the camera to prevent pendulum motion sickness
        Vector3 lookDirection = target.position - xrOrigin.Camera.transform.position;
        lookDirection.y = 0; 
        
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            Quaternion currentRotation = xrOrigin.transform.rotation;
            Quaternion nextRotation = Quaternion.Slerp(currentRotation, targetRotation, rotationSpeed * Time.deltaTime);
            
            // Calculate the difference in Y rotation for this frame
            float angleDelta = Mathf.DeltaAngle(currentRotation.eulerAngles.y, nextRotation.eulerAngles.y);
            
            // Pivot the world around the user's physical head
            xrOrigin.RotateAroundCameraUsingOriginUp(angleDelta);
        }

        // 4. Move the rig to the offset target
        xrOrigin.transform.position = Vector3.MoveTowards(xrOrigin.transform.position, targetPosition, speed * Time.deltaTime);
    }

    private void HandleAdvanceInput()
    {
        bool advancePressed = IsAdvancePressed();

        if (advancePressed && !advanceWasPressed)
            OnAdvancePressed();

        advanceWasPressed = advancePressed;
    }

    private void OnAdvancePressed()
    {
        if (!waitingForTrigger)
            return;

        AdvanceWaypoint();
    }

    private void AdvanceWaypoint()
    {
        if (currentWP + 1 >= waypoints.Length)
        {
            if (!loopWaypoints)
            {
                Debug.Log("AutoLocomotion: Reached the last waypoint.");
                waitingForTrigger = true;
                return; 
            }
            currentWP = 0;
            Debug.Log("AutoLocomotion: Looping back to the first waypoint.");
        }
        else
        {
            currentWP++;
            Debug.Log($"AutoLocomotion: Advanced to waypoint {currentWP} - {waypoints[currentWP].name}");
        }
        waitingForTrigger = false;
    }

    private bool IsAdvancePressed()
    {
        if (advanceAction == null || advanceAction.action == null)
            return false;

        if (!advanceAction.action.enabled)
            advanceAction.action.Enable();
        if (advanceAction.action.IsPressed())
        {
            Debug.Log($"Advance Action Pressed: {advanceAction.action.IsPressed()}");
        }
        
        return advanceAction.action.IsPressed();
    }

    private void OnDisable()
    {
        if (advanceAction != null)
            advanceAction.action.Disable();
    }
}