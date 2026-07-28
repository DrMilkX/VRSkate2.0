using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class TransformWaypoint : MonoBehaviour
{
    // Fired whenever the player arrives at a new current waypoint (index, waypoint transform).
    // Does not fire for the initial waypoint set in Start().
    public event Action<int, Transform> OnWaypointReached;

    [SerializeField] public Transform[] waypoints;
    public Transform currentWaypoint;
    public int currentWaypointIndex = 0;
    public bool loopWaypoints = true;

    // player transform and arrow to show direction of the next waypoint
    public Transform playerTransform;
    public Transform arrowSprite;

    public LayerMask groundLayerMask = 1 << 0; // default to layer 0 (Default)
    public float distanceInFront = 1f; // distance in front of the player to place the arrow

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // If no waypoints are assigned in the inspector, use the child transforms of this object as waypoints
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("TransformWaypoint: No waypoints assigned.", this);

            // use the child transforms of this object as waypoints if none are assigned
            waypoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                waypoints[i] = transform.GetChild(i);
            }
        }

        // If there are still no waypoints, log a warning and return
        if (waypoints.Length == 0)
        {
            Debug.LogWarning("TransformWaypoint: No child transforms found to use as waypoints.", this);
            // hide the arrow
            arrowSprite.gameObject.SetActive(false);
            arrowSprite = null;
            return;
        }


        currentWaypoint = waypoints[0];
        currentWaypointIndex = 0;
        HideAllWaypoints();
        ShowCurrentWaypoint();
        

        // set the arrow sprite to point to the current waypoint
        ShowArrowToCurrentWaypoint();
        

    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current?.wKey.wasPressedThisFrame == true)
        {
            NextWaypoint();
        }

        // make the arrow point to the current waypoint and always in front of the player on the ground
        ShowArrowToCurrentWaypoint();
    }

    // disable all the waypoint lights
    void HideAllWaypoints()
    {
        foreach (Transform waypoint in waypoints){
            waypoint.gameObject.SetActive(false);
        }
    }

    // enable the current waypoint light
    void ShowCurrentWaypoint()
    {
        if (currentWaypoint != null){
            currentWaypoint.gameObject.SetActive(true);
        }
    }

    // toggle the next waypoint in the list, looping back to the first one if at the end
    public void NextWaypoint()
    {
        if (currentWaypointIndex < waypoints.Length - 1){
            currentWaypointIndex++;
            currentWaypoint = waypoints[currentWaypointIndex];
            HideAllWaypoints();
            ShowCurrentWaypoint();

            Debug.Log($"TransformWaypoint: Moved to waypoint {currentWaypointIndex + 1} - {currentWaypoint.name}");
            OnWaypointReached?.Invoke(currentWaypointIndex, currentWaypoint);
        }
        else if (loopWaypoints)
        {
            // loop back to the first waypoint
            currentWaypointIndex = 0;
            currentWaypoint = waypoints[currentWaypointIndex];
            HideAllWaypoints();
            ShowCurrentWaypoint();
            Debug.Log($"TransformWaypoint: Looping back to waypoint {currentWaypointIndex + 1} - {currentWaypoint.name}");
            OnWaypointReached?.Invoke(currentWaypointIndex, currentWaypoint);

        }else
        {
            arrowSprite.gameObject.SetActive(false);
            Debug.Log("TransformWaypoint: Reached the last waypoint and looping is disabled.");
        }

    }

    public void ShowParticularWaypoint(int index)
    {
        if ((index >= 0 && index < waypoints.Length) || (index == -1 && waypoints.Length > 0)) // allow -1 to show the last waypoint
        {
            if (index == -1)
            {
                index = waypoints.Length - 1; // set to last waypoint
            }
            currentWaypointIndex = index;
            currentWaypoint = waypoints[currentWaypointIndex];
            HideAllWaypoints();
            ShowCurrentWaypoint();
            Debug.Log($"TransformWaypoint: Moved to waypoint {currentWaypointIndex + 1} - {currentWaypoint.name}");
            OnWaypointReached?.Invoke(currentWaypointIndex, currentWaypoint);
        }
        else
        {
            Debug.LogWarning($"TransformWaypoint: Invalid waypoint index {index}. Must be between 0 and {waypoints.Length - 1}.", this);
        }
    }


    void ShowArrowToCurrentWaypoint()
    {
        if (arrowSprite != null && playerTransform != null && currentWaypoint != null)
        {
            // place the arrow on the ground in front of the player (using a raycast from head to the ground) and point it to the current waypoint
            
            // cast a ray down from the player to find the ground beneath them
            RaycastHit hit;
            float maxDistance = 10f;
            if (Physics.Raycast(playerTransform.position, Vector3.down, out hit, maxDistance, groundLayerMask))
            {
                // place the arrow just above the ground
                arrowSprite.position = hit.point + Vector3.up * 0.01f;

                // point the arrow toward the current waypoint, using the XZ plane
                Vector3 direction = currentWaypoint.position - playerTransform.position;
                Vector3 dirXZ = new Vector3(direction.x, 0f, direction.z);
                if (dirXZ.sqrMagnitude > 0.0001f)
                {
                    float angle = Mathf.Atan2(dirXZ.z, dirXZ.x) * Mathf.Rad2Deg;
                    // rotate arrow flat on the ground (assumes arrow's forward is +X)
                    arrowSprite.rotation = Quaternion.Euler(0f, -angle, 0f);
                }

                Vector3 vector3InFront = playerTransform.forward * distanceInFront;
                Vector3 vector3InFrontXZ = new Vector3(vector3InFront.x, 0f, vector3InFront.z);
                arrowSprite.position = hit.point + vector3InFrontXZ + Vector3.up * 0.01f; // place the arrow slightly above the ground
            }
            else
            {
                // if no ground found, keep arrow at player's feet
                arrowSprite.position = playerTransform.position + Vector3.down * 0.5f;
            }
            arrowSprite.gameObject.SetActive(true);
        }else
        {
            Debug.LogWarning("TransformWaypoint: Arrow sprite or player transform is not assigned.", this);
        }
    }

    public Vector3 GetCurrentWaypointPosition()
    {
        if (currentWaypoint != null)
            return waypoints[currentWaypointIndex].position;
        else
            return Vector3.zero;
    }


}
