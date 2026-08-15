using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

public class AutoLocomotion : MonoBehaviour
{
    public GameObject[] waypoints;
    public int currentWP = 0;

    public float speed = 3.0f;

    public bool moveForward = false;
    public bool loopWaypoints = false;
    private bool started = false;
    private bool finished = false;

    [Header("Input")]
    public InputActionReference advanceAction;
    public Transform cameraTransform;
    public float turnSpeed = 5.0f;
    public float pauseAmt = 1.0f; // amount of time to pause at each waypoint

    void Start()
    {
        if (waypoints.Length == 0)
        {
            Debug.LogWarning("AutoLocomotion: No waypoints assigned.", this);
            return;
        }

        // Set the initial position to the first waypoint
        transform.position = waypoints[0].transform.position;
        started = false;

        // Subscribe to the advance action
        if (advanceAction != null){
            advanceAction.action.Enable();
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Move the player forward if the moveForward flag is set
        if(moveForward){
            MovePlayer();
        }

        if (advanceAction.action.IsPressed()){
            // Debug.Log("Advance button pressed");
            if(!finished && !started){
                moveForward = true;
                started = true;
            }
        }

        // HandleAdvanceInput();

    }

    void MovePlayer()
    {
        if (waypoints.Length == 0 || currentWP >= waypoints.Length || finished)
            return;

        // Get the current waypoint
        GameObject currentWaypoint = waypoints[currentWP];

        // If the player is close enough to the waypoint, move to the next one
        if (Vector3.Distance(transform.position, currentWaypoint.transform.position) < 0.1f){
            StartCoroutine(PauseAtWaypoint());

            // If we are at the last waypoint and not looping, stop moving forward
            if(currentWP == waypoints.Length - 1 && !loopWaypoints){
                moveForward = false; 
                finished = true;
            }
            return;
        }
        // Move towards the current waypoint
        if(moveForward){
            transform.position = Vector3.MoveTowards(transform.position, currentWaypoint.transform.position, speed * Time.deltaTime);
        }
    }

    public void ToggleMoveForward()
    {
        moveForward = !moveForward;
    }

    // Coroutine to pause at the current waypoint, rotate towards the next waypoint, and then resume moving forward
    private IEnumerator PauseAtWaypoint()
    {
        moveForward = false;

        yield return new WaitForSeconds(pauseAmt);

        // If we are at the last waypoint and not looping, stop moving forward
        if(currentWP == waypoints.Length - 1 && !loopWaypoints){
            moveForward = false; 
            finished = true;
            yield break;
        }

        // Start turning towards the next waypoint
        int nextWP = (currentWP + 1) % waypoints.Length;
        GameObject nextWaypoint = waypoints[nextWP];

        // Calculate the direction to the next waypoint
        Vector3 directionToNext = (nextWaypoint.transform.position - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(directionToNext);

        // Rotate towards the target rotation over time
        while (Quaternion.Angle(transform.rotation, targetRotation) > 0.1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            yield return null;
        }

        // set the current waypoint to the next one
        currentWP = nextWP;

        // Ensure we end up exactly at the target rotation
        moveForward = true;
    }

}
