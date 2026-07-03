using UnityEngine;

/// <summary>
/// Holds the ordered checkpoint sequence for the pizza delivery route.
/// Attach this to the waypoint manager or any route manager object, then assign the checkpoint transforms in order.
/// </summary>
public class WaypointCheckpointSequence : MonoBehaviour
{
    [Tooltip("Checkpoint transforms in the exact order the player should reach them.")]
    [SerializeField] private Transform[] checkpoints = new Transform[0];

    public int Count => checkpoints != null ? checkpoints.Length : 0;

    public Transform[] Checkpoints => checkpoints;

    public Transform GetCheckpoint(int index)
    {
        if (checkpoints == null || index < 0 || index >= checkpoints.Length)
        {
            return null;
        }

        return checkpoints[index];
    }

    public string GetCheckpointName(int index)
    {
        Transform checkpoint = GetCheckpoint(index);
        if (checkpoint == null)
        {
            return $"Checkpoint {index + 1}";
        }

        return checkpoint.name;
    }
}
