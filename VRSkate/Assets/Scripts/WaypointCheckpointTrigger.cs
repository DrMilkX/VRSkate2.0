using UnityEngine;

/// <summary>
/// Put this on each waypoint cylinder trigger.
/// When a character with a PizzaDeliveryCheckpointTracker enters the trigger, the tracker validates sequence order
/// and reveals the matching pizza.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaypointCheckpointTrigger : MonoBehaviour
{
    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            return;
        }

        PizzaDeliveryCheckpointTracker tracker = other.GetComponentInParent<PizzaDeliveryCheckpointTracker>();
        if (tracker == null)
        {
            return;
        }

        tracker.TryHandleCheckpoint(transform);
    }
}