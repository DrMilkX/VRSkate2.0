using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

public class AutoLocomotion : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Assign the shared checkpoint sequence holder here instead of populating a local array.")]
    public WaypointCheckpointSequence checkpointSequence;

    [Tooltip("Optional controller that keeps only the current waypoint marker visible.")]
    public WaypointCheckpointVisibilityController waypointVisibilityController;

    [Tooltip("NPC characters to face when reaching each checkpoint. Uses the current checkpoint index.")]
    public GameObject[] npcCharacters;

    public float speed = 10.0f;
    [Tooltip("How fast the rig smoothly aligns to the new waypoint before moving (degrees per second).")]
    public float waypointRotationSpeed = 100.0f;
    [Tooltip("How quickly the player turns to face the current NPC, in degrees per second.")]
    public float npcLookRotationSpeed = 180.0f;
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
    private Coroutine npcLookRoutine;
    private Coroutine waypointTurnRoutine;

    void Start()
    {
        if (checkpointSequence == null || checkpointSequence.Count == 0)
        {
            Debug.LogWarning("AutoLocomotion: No checkpoint sequence assigned.", this);
            enabled = false;
            return;
        }

        if (npcCharacters == null || npcCharacters.Length == 0)
        {
            Debug.LogWarning("AutoLocomotion: No NPC characters assigned, so the player will move but not have a look target.", this);
        }
        else if (npcCharacters.Length != checkpointSequence.Count)
        {
            Debug.LogWarning($"AutoLocomotion: NPC count ({npcCharacters.Length}) does not match checkpoint count ({checkpointSequence.Count}). The player will still move on the waypoint route, but some checkpoints may not have a matching NPC look target.", this);
        }

        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogWarning("AutoLocomotion: XR Origin or headset camera is missing, so auto-move cannot run safely.", this);
            enabled = false;
            return;
        }

        if (waypointVisibilityController != null)
        {
            currentWP = Mathf.Clamp(waypointVisibilityController.CurrentWaypointIndex, 0, checkpointSequence.Count - 1);
        }
        else
        {
            currentWP = Mathf.Clamp(currentWP, 0, checkpointSequence.Count - 1);
        }

        if (waypointVisibilityController != null)
        {
            waypointVisibilityController.SetCurrentWaypoint(currentWP);
        }

        if (advanceAction != null)
            advanceAction.action.Enable();
            
        AlignToCurrentWaypoint();
    }

    void Update()
    {
        if (checkpointSequence == null || checkpointSequence.Count == 0)
            return;

        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogWarning("AutoLocomotion: XR Origin or headset camera became unavailable during auto-move.", this);
            enabled = false;
            return;
        }

        HandleAdvanceInput();

        // Halt movement if waiting for input OR if the initial snappy turn is still running
        if (waitingForTrigger || waypointTurnRoutine != null)
            return;

        Transform target = checkpointSequence.GetCheckpoint(currentWP);
        if (target == null)
        {
            Debug.LogWarning($"AutoLocomotion: Checkpoint {currentWP + 1} is missing from the sequence.", this);
            enabled = false;
            return;
        }

        Vector3 cameraOffset = xrOrigin.Camera.transform.position - xrOrigin.transform.position;
        cameraOffset.y = 0; 
        
        Vector3 targetPosition = target.position - cameraOffset;

        Vector3 currentPosFlat = new Vector3(xrOrigin.transform.position.x, 0, xrOrigin.transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetPosition.x, 0, targetPosition.z);

        if (Vector3.Distance(currentPosFlat, targetPosFlat) <= arrivalDistance)
        {
            xrOrigin.transform.position = targetPosition;
            waitingForTrigger = true;
            
            StartNpcLookRoutine();
            return;
        }

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
        if (waypointVisibilityController != null)
        {
            waypointVisibilityController.AdvanceWaypoint();
            if (checkpointSequence != null && checkpointSequence.Count > 0)
            {
                currentWP = Mathf.Clamp(waypointVisibilityController.CurrentWaypointIndex, 0, checkpointSequence.Count - 1);
            }
        }
        else
        {
            int targetCount = checkpointSequence != null ? checkpointSequence.Count : 0;
            if (currentWP + 1 >= targetCount)
            {
                if (!loopWaypoints)
                {
                    Debug.Log("AutoLocomotion: Reached the last checkpoint.");
                    waitingForTrigger = true;
                    return; 
                }
                currentWP = 0;
                Debug.Log("AutoLocomotion: Looping back to the first checkpoint.");
            }
            else
            {
                currentWP++;
                Debug.Log($"AutoLocomotion: Advanced to checkpoint {currentWP + 1} - {checkpointSequence.GetCheckpointName(currentWP)}");
            }
        }

        if (waypointVisibilityController != null)
        {
            waypointVisibilityController.SetCurrentWaypoint(currentWP);
        }

        waitingForTrigger = false;
        AlignToCurrentWaypoint();
    }
    
    private void AlignToCurrentWaypoint()
    {
        if (waypointTurnRoutine != null)
        {
            StopCoroutine(waypointTurnRoutine);
        }
        
        waypointTurnRoutine = StartCoroutine(SmoothAlignToWaypoint());
    }

    private IEnumerator SmoothAlignToWaypoint()
    {
        Transform target = checkpointSequence.GetCheckpoint(currentWP);
        if (target == null)
        {
            waypointTurnRoutine = null;
            yield break;
        }

        while (true)
        {
            Vector3 targetDirection = target.position - xrOrigin.Camera.transform.position;
            targetDirection.y = 0f;

            if (targetDirection.sqrMagnitude < 0.0001f) break;

            float targetYaw = Quaternion.LookRotation(targetDirection, Vector3.up).eulerAngles.y;
            float currentYaw = xrOrigin.Camera.transform.eulerAngles.y;

            float nextYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, waypointRotationSpeed * Time.deltaTime);
            float angleDelta = Mathf.DeltaAngle(currentYaw, nextYaw);

            if (Mathf.Abs(angleDelta) > 0.0001f)
            {
                xrOrigin.RotateAroundCameraUsingOriginUp(angleDelta);
            }

            if (Mathf.Abs(Mathf.DeltaAngle(xrOrigin.Camera.transform.eulerAngles.y, targetYaw)) <= 0.5f)
            {
                break;
            }

            yield return null;
        }

        waypointTurnRoutine = null;
    }

    private void StartNpcLookRoutine()
    {
        if (npcLookRoutine != null)
        {
            StopCoroutine(npcLookRoutine);
        }

        npcLookRoutine = StartCoroutine(SmoothFaceCurrentNpc());
    }

    private IEnumerator SmoothFaceCurrentNpc()
    {
        if (xrOrigin == null || npcCharacters == null || currentWP < 0 || currentWP >= npcCharacters.Length)
        {
            waitingForTrigger = true;
            yield break;
        }

        GameObject npc = npcCharacters[currentWP];
        if (npc == null)
        {
            Debug.LogWarning($"AutoLocomotion: No NPC assigned for checkpoint {currentWP + 1}.", this);
            waitingForTrigger = true;
            yield break;
        }

        Transform npcTransform = npc.transform;
        Debug.Log($"AutoLocomotion: Smoothly turning toward NPC '{npc.name}' at checkpoint {currentWP + 1}.", this);

        while (true)
        {
            Vector3 targetDirection = npcTransform.position - xrOrigin.Camera.transform.position;
            targetDirection.y = 0f;

            if (targetDirection.sqrMagnitude < 0.0001f) break;

            float targetYaw = Quaternion.LookRotation(targetDirection, Vector3.up).eulerAngles.y;
            float currentYaw = xrOrigin.Camera.transform.eulerAngles.y;

            if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) < 0.5f)
            {
                break;
            }

            float nextYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, npcLookRotationSpeed * Time.deltaTime);
            float angleDelta = Mathf.DeltaAngle(currentYaw, nextYaw);

            if (Mathf.Abs(angleDelta) > 0.0001f)
            {
                xrOrigin.RotateAroundCameraUsingOriginUp(angleDelta);
            }

            if (Mathf.Abs(Mathf.DeltaAngle(xrOrigin.Camera.transform.eulerAngles.y, targetYaw)) <= 0.5f)
            {
                break;
            }

            yield return null;
        }

        waitingForTrigger = true;
        npcLookRoutine = null;
    }

    private bool IsAdvancePressed()
    {
        if (advanceAction == null || advanceAction.action == null)
            return false;

        if (!advanceAction.action.enabled)
            advanceAction.action.Enable();
        
        return advanceAction.action.IsPressed();
    }

    private void OnDisable()
    {
        if (npcLookRoutine != null)
        {
            StopCoroutine(npcLookRoutine);
            npcLookRoutine = null;
        }

        if (waypointTurnRoutine != null)
        {
            StopCoroutine(waypointTurnRoutine);
            waypointTurnRoutine = null;
        }

        if (advanceAction != null)
            advanceAction.action.Disable();
    }
}