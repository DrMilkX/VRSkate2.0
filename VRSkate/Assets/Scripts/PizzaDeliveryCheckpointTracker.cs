using System.Collections;
using UnityEngine;

/// <summary>
/// Attach this to the player delivering pizza.
/// Waypoint trigger cylinders call into this component when the character enters them.
/// The tracker validates the checkpoint order and reveals the matching pizza object.
/// </summary>
[DisallowMultipleComponent]
public class PizzaDeliveryCheckpointTracker : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Shared checkpoint sequence asset/component that defines the order of waypoint cylinders.")]
    [SerializeField] private WaypointCheckpointSequence checkpointSequence;

    [Tooltip("Optional waypoint visibility controller to advance when a checkpoint is reached.")]
    [SerializeField] private WaypointCheckpointVisibilityController waypointVisibilityController;

    [Tooltip("If enabled, the checkpoint sequence starts over after the last checkpoint is delivered.")]
    [SerializeField] private bool loopSequence = false;

    [Header("Pizza Delivery")]
    [Tooltip("Existing pizza GameObjects in checkpoint order. Leave empty if you want to use character roots instead.")]
    [SerializeField] private GameObject[] pizzaObjects = new GameObject[0];

    [Tooltip("How far above the landing point the pizza should appear before floating down.")]
    [SerializeField] private float pizzaSpawnHeight = 1.5f;

    [Tooltip("How long the pizza takes to float down into place.")]
    [SerializeField] private float pizzaFloatDownDuration = 0.75f;

    private int nextCheckpointIndex = 0;
    private Transform[] resolvedPizzaTargets = new Transform[0];

    public void SetCheckpointSequence(WaypointCheckpointSequence sequence)
    {
        checkpointSequence = sequence;
        ResetProgress();
    }

    public void ResetProgress()
    {
        nextCheckpointIndex = 0;
    }

    private void Start()
    {
        Debug.Log($"PizzaDeliveryCheckpointTracker: Starting on '{gameObject.name}'. Checkpoints assigned = {(checkpointSequence != null ? checkpointSequence.Count : 0)}, pizza objects assigned = {(pizzaObjects != null ? pizzaObjects.Length : 0)}.", this);

        if (waypointVisibilityController == null)
        {
            waypointVisibilityController = FindAnyObjectByType<WaypointCheckpointVisibilityController>();
        }

        ResolvePizzaTargets();

        // Debug.Log($"PizzaDeliveryCheckpointTracker: Resolved {resolvedPizzaTargets.Length} pizza object(s).", this);

        for (int i = 0; i < resolvedPizzaTargets.Length; i++)
        {
            if (resolvedPizzaTargets[i] != null)
            {
                // Debug.Log($"PizzaDeliveryCheckpointTracker: Hiding pizza object {i + 1} -> '{resolvedPizzaTargets[i].name}'.", resolvedPizzaTargets[i]);
                resolvedPizzaTargets[i].gameObject.SetActive(false);
            }
            else
            {
                Debug.LogWarning($"PizzaDeliveryCheckpointTracker: Pizza object slot {i + 1} is empty or could not be resolved.", this);
            }
        }
    }

    public bool TryHandleCheckpoint(Transform checkpointTransform)
    {
        if (checkpointSequence == null || checkpointSequence.Count == 0)
        {
            Debug.LogWarning($"PizzaDeliveryCheckpointTracker: No checkpoint sequence assigned on '{gameObject.name}'.", this);
            return false;
        }

        if (checkpointTransform == null)
        {
            Debug.LogWarning($"PizzaDeliveryCheckpointTracker: Received a null checkpoint transform on '{gameObject.name}'.", this);
            return false;
        }

        Transform expectedCheckpoint = checkpointSequence.GetCheckpoint(nextCheckpointIndex);
        if (expectedCheckpoint == null)
        {
            Debug.LogWarning($"PizzaDeliveryCheckpointTracker: Checkpoint {nextCheckpointIndex + 1} is missing from the sequence.", this);
            enabled = false;
            return false;
        }

        // Debug.Log($"PizzaDeliveryCheckpointTracker: Triggered by '{checkpointTransform.name}', expecting '{expectedCheckpoint.name}' for checkpoint {nextCheckpointIndex + 1}.", this);

        if (!IsCheckpointMatch(checkpointTransform, expectedCheckpoint))
        {
            Debug.Log($"PizzaDeliveryCheckpointTracker: Ignored trigger '{checkpointTransform.name}' because it does not match the expected checkpoint '{expectedCheckpoint.name}'.", this);
            return false;
        }

        Debug.Log($"PizzaDeliveryCheckpointTracker: Arrived at {checkpointSequence.GetCheckpointName(nextCheckpointIndex)} checkpoint.", this);
        RevealPizza(nextCheckpointIndex);

        if (waypointVisibilityController != null)
        {
            waypointVisibilityController.AdvanceWaypoint();
        }

        nextCheckpointIndex++;
        if (nextCheckpointIndex >= checkpointSequence.Count)
        {
            if (loopSequence)
            {
                nextCheckpointIndex = 0;
            }
            else
            {
                nextCheckpointIndex = checkpointSequence.Count;
            }
        }

        return true;
    }

    private void ResolvePizzaTargets()
    {
        if (pizzaObjects != null && pizzaObjects.Length > 0)
        {
            resolvedPizzaTargets = new Transform[pizzaObjects.Length];
            for (int i = 0; i < pizzaObjects.Length; i++)
            {
                resolvedPizzaTargets[i] = pizzaObjects[i] != null ? pizzaObjects[i].transform : null;
                if (pizzaObjects[i] != null)
                {
                    // Debug.Log($"PizzaDeliveryCheckpointTracker: Slot {i + 1} mapped to pizza object '{pizzaObjects[i].name}'.", this);
                }
            }
            return;
        }

        Debug.LogWarning($"PizzaDeliveryCheckpointTracker: No pizzaObjects assigned on '{gameObject.name}'. The reveal step will not work until the array is populated.", this);
        resolvedPizzaTargets = new Transform[0];
    }

    private bool IsCheckpointMatch(Transform candidate, Transform expected)
    {
        if (candidate == null || expected == null)
        {
            return false;
        }

        return candidate == expected || candidate.IsChildOf(expected) || expected.IsChildOf(candidate);
    }

    private void RevealPizza(int pizzaIndex)
    {
        if (pizzaIndex < 0 || pizzaIndex >= resolvedPizzaTargets.Length)
        {
            Debug.LogWarning($"PizzaDeliveryCheckpointTracker: No pizza object assigned for checkpoint {pizzaIndex + 1}. Resolved pizza count = {resolvedPizzaTargets.Length}.", this);
            return;
        }

        Transform pizzaTransform = resolvedPizzaTargets[pizzaIndex];
        if (pizzaTransform == null)
        {
            Debug.LogWarning($"PizzaDeliveryCheckpointTracker: Pizza object {pizzaIndex + 1} could not be resolved.", this);
            return;
        }

        Vector3 landingPosition = pizzaTransform.position;
        Vector3 startPosition = landingPosition + Vector3.up * pizzaSpawnHeight;

        Debug.Log($"PizzaDeliveryCheckpointTracker: Revealing '{pizzaTransform.name}' from {startPosition} to {landingPosition}.", pizzaTransform);

        pizzaTransform.position = startPosition;
        pizzaTransform.gameObject.SetActive(true);
        StartCoroutine(FloatPizzaDown(pizzaTransform, landingPosition));
    }

    private IEnumerator FloatPizzaDown(Transform pizzaTransform, Vector3 landingPosition)
    {
        if (pizzaTransform == null)
        {
            yield break;
        }

        Vector3 startPosition = pizzaTransform.position;
        float duration = Mathf.Max(0.01f, pizzaFloatDownDuration);
        float elapsed = 0f;

        while (elapsed < duration && pizzaTransform != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            pizzaTransform.position = Vector3.Lerp(startPosition, landingPosition, t);
            yield return null;
        }

        if (pizzaTransform != null)
        {
            pizzaTransform.position = landingPosition;
        }
    }
}