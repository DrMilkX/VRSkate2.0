using UnityEngine;
using WrightAngle.Waypoint;

/// <summary>
/// Keeps only one waypoint target active at a time so waypoint markers do not clutter the screen.
/// Assign waypoint targets in order, then advance the current index from your gameplay logic.
/// </summary>
[DisallowMultipleComponent]
public class WaypointCheckpointVisibilityController : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Waypoint targets in the order they should be shown.")]
    [SerializeField] private WaypointTarget[] waypointTargets = new WaypointTarget[0];

    [Tooltip("The waypoint UI manager that owns the marker pool and visibility state. If left empty, the controller will try to find one in the scene.")]
    [SerializeField] private WaypointUIManager waypointUIManager;

    [Tooltip("Start by showing the first waypoint target.")]
    [SerializeField] private bool activateFirstOnStart = true;

    [Tooltip("Wrap back to the first waypoint after the last one.")]
    [SerializeField] private bool loopWaypoints = true;

    [Tooltip("If enabled, the controller will try to register every assigned target on start so only the current one is visible.")]
    [SerializeField] private bool registerTargetsOnStart = true;

    [Header("State")]
    [SerializeField] private int currentWaypointIndex = 0;

    public int CurrentWaypointIndex => currentWaypointIndex;

    private void Start()
    {
        if (waypointTargets == null || waypointTargets.Length == 0)
        {
            Debug.LogWarning($"WaypointCheckpointVisibilityController: No waypoint targets assigned on '{gameObject.name}'.", this);
            return;
        }

        if (waypointUIManager == null)
        {
            waypointUIManager = FindAnyObjectByType<WaypointUIManager>();
        }

        if (waypointUIManager == null)
        {
            Debug.LogWarning($"WaypointCheckpointVisibilityController: No WaypointUIManager found. Marker visibility will fall back to target activate/deactivate calls on '{gameObject.name}'.", this);
        }

        WaypointTarget[] allSceneTargets = FindObjectsByType<WaypointTarget>(FindObjectsInactive.Include);
        if (allSceneTargets.Length != waypointTargets.Length)
        {
            Debug.LogWarning($"WaypointCheckpointVisibilityController: Assigned target count ({waypointTargets.Length}) does not match waypoint targets found in the scene ({allSceneTargets.Length}). If you only see a subset of checkpoints, make sure all 7 targets are assigned here.", this);
        }

        if (registerTargetsOnStart)
        {
            for (int i = 0; i < waypointTargets.Length; i++)
            {
                WaypointTarget target = waypointTargets[i];
                if (target == null)
                {
                    continue;
                }

                if (!target.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"WaypointCheckpointVisibilityController: Target {i + 1} ('{target.gameObject.name}') is inactive in the hierarchy and cannot be registered for marker visibility.", target);
                    continue;
                }

                target.ActivateWaypoint();
                if (waypointUIManager != null)
                {
                    waypointUIManager.Register(target);
                }
            }
        }

        currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, waypointTargets.Length - 1);

        if (activateFirstOnStart)
        {
            ApplyCurrentWaypointVisibility();
        }
        else
        {
            DeactivateAllWaypoints();
        }
    }

    public void SetCurrentWaypoint(int index)
    {
        if (waypointTargets == null || waypointTargets.Length == 0)
        {
            Debug.LogWarning($"WaypointCheckpointVisibilityController: Cannot set waypoint index {index} because no targets are assigned.", this);
            return;
        }

        if (index < 0 || index >= waypointTargets.Length)
        {
            Debug.LogWarning($"WaypointCheckpointVisibilityController: Waypoint index {index} is out of range for {waypointTargets.Length} target(s).", this);
            return;
        }

        currentWaypointIndex = index;
        ApplyCurrentWaypointVisibility();
    }

    public void AdvanceWaypoint()
    {
        if (waypointTargets == null || waypointTargets.Length == 0)
        {
            return;
        }

        int nextIndex = currentWaypointIndex + 1;
        if (nextIndex >= waypointTargets.Length)
        {
            if (!loopWaypoints)
            {
                Debug.Log("WaypointCheckpointVisibilityController: Reached the last waypoint.", this);
                DeactivateAllWaypoints();
                return;
            }

            nextIndex = 0;
        }

        SetCurrentWaypoint(nextIndex);
    }

    public void DeactivateAllWaypoints()
    {
        if (waypointTargets == null)
        {
            return;
        }

        for (int i = 0; i < waypointTargets.Length; i++)
        {
            WaypointTarget target = waypointTargets[i];
            if (target != null)
            {
                if (waypointUIManager != null)
                {
                    waypointUIManager.SetTargetActive(target, false);
                }
                else
                {
                    target.DeactivateWaypoint();
                }
            }
        }
    }

    private void ApplyCurrentWaypointVisibility()
    {
        if (waypointTargets == null || waypointTargets.Length == 0)
        {
            return;
        }

        for (int i = 0; i < waypointTargets.Length; i++)
        {
            WaypointTarget target = waypointTargets[i];
            if (target == null)
            {
                continue;
            }

            if (i == currentWaypointIndex)
            {
                if (waypointUIManager != null)
                {
                    waypointUIManager.SetTargetActive(target, true);
                }
                else
                {
                    target.ActivateWaypoint();
                }
                Debug.Log($"WaypointCheckpointVisibilityController: Activated waypoint {i + 1} - {target.gameObject.name}.", target);
            }
            else
            {
                if (waypointUIManager != null)
                {
                    waypointUIManager.SetTargetActive(target, false);
                }
                else
                {
                    target.DeactivateWaypoint();
                }
            }
        }
    }
}
