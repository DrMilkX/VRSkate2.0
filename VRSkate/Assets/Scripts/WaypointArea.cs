using UnityEngine;
using UnityEngine.Events;

public class WaypointArea : MonoBehaviour
{
    [SerializeField] private UnityEvent onPlayerEnter;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            Debug.LogWarning($"WaypointArea: Triggered by non-player object '{other.name}' on '{gameObject.name}'.", this);
            return;
        }

        Debug.Log($"WaypointArea: Player entered '{gameObject.name}' area.", this);
        onPlayerEnter?.Invoke();
        gameObject.SetActive(false);
    }
}
