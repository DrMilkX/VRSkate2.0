using UnityEngine;

/// <summary>
/// Ground arrow that follows the player and points toward the current waypoint checkpoint.
/// Attach to a dedicated child object carrying the arrow sprite/renderer. The object does not
/// need to be parented under the player - it repositions itself in world space every frame.
/// </summary>
public class PlayerWaypointArrow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player/rig transform this arrow should follow. Defaults to this object's parent if left empty.")]
    [SerializeField] private Transform player;

    [Tooltip("Checkpoint sequence that defines the ordered waypoints.")]
    [SerializeField] private WaypointCheckpointSequence checkpointSequence;

    [Tooltip("Optional controller that tracks which checkpoint is currently active. If left empty, the arrow always points at the first checkpoint.")]
    [SerializeField] private WaypointCheckpointVisibilityController waypointVisibilityController;

    [Header("Placement")]
    [Tooltip("Layers considered 'ground' when raycasting down from the player to place the arrow.")]
    [SerializeField] private LayerMask groundLayers = ~0;

    [Tooltip("How far above the detected ground the arrow should hover.")]
    [SerializeField] private float heightAboveGround = 0.05f;

    [Tooltip("How far to raycast downward (from above the player) when looking for the ground.")]
    [SerializeField] private float groundCheckDistance = 5f;

    [Header("Renderer")]
    [Tooltip("Renderer(s) to enable/disable based on waypoint availability. Auto-collected from children if left empty.")]
    [SerializeField] private Renderer[] arrowRenderers;

    // Pitch/roll the arrow was authored with (e.g. tilted flat against the ground). Only yaw is updated at runtime.
    private float restPitch;
    private float restRoll;

    private void Reset()
    {
        if (player == null && transform.parent != null)
        {
            player = transform.parent;
        }
    }

    private void Awake()
    {
        if (player == null && transform.parent != null)
        {
            player = transform.parent;
        }

        if (arrowRenderers == null || arrowRenderers.Length == 0)
        {
            arrowRenderers = GetComponentsInChildren<Renderer>(true);
        }

        Vector3 restEuler = transform.eulerAngles;
        restPitch = restEuler.x;
        restRoll = restEuler.z;
    }

    private void LateUpdate()
    {
        Transform nextWaypoint = GetNextWaypoint();
        bool hasWaypoint = player != null && nextWaypoint != null;

        SetVisible(hasWaypoint);
        if (!hasWaypoint)
        {
            return;
        }

        UpdatePosition();
        UpdateRotation(nextWaypoint);
    }

    private Transform GetNextWaypoint()
    {
        if (checkpointSequence == null || checkpointSequence.Count == 0)
        {
            return null;
        }

        int index = waypointVisibilityController != null ? waypointVisibilityController.CurrentWaypointIndex : 0;
        return checkpointSequence.GetCheckpoint(index);
    }

    private void UpdatePosition()
    {
        Vector3 rayOrigin = player.position + Vector3.up * 1f;
        Vector3 groundPosition;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundCheckDistance + 1f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundPosition = hit.point;
        }
        else
        {
            // No ground detected - fall back to the player's own position.
            groundPosition = player.position;
        }

        transform.position = groundPosition + Vector3.up * heightAboveGround;
    }

    private void UpdateRotation(Transform target)
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float yaw = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y;
        transform.eulerAngles = new Vector3(restPitch, yaw, restRoll);
    }

    private void SetVisible(bool visible)
    {
        if (arrowRenderers == null)
        {
            return;
        }

        for (int i = 0; i < arrowRenderers.Length; i++)
        {
            if (arrowRenderers[i] != null && arrowRenderers[i].enabled != visible)
            {
                arrowRenderers[i].enabled = visible;
            }
        }
    }
}
