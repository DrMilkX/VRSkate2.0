using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class AutoMoveWaypoint : MonoBehaviour
{
    public GameObject[] waypoints;
    int currentWP = 0;

    public float speed = 5.0f;

    public bool moveForward = false;
    public bool loopWaypoints = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    

    // Update is called once per frame
    void Update()
    {
        // Move the player forward if the moveForward flag is set
        if(moveForward){
            MovePlayer();
        }

    }

    void MovePlayer()
    {
        if (waypoints.Length == 0)
            return;

        // Get the current waypoint
        GameObject currentWaypoint = waypoints[currentWP];

        
        // If the player is close enough to the waypoint, move to the next one
        if (Vector3.Distance(transform.position, currentWaypoint.transform.position) < 0.1f){
            currentWP = (currentWP + 1) % waypoints.Length;
            if(currentWP == 0 && !loopWaypoints){
                moveForward = false; // Stop moving forward until the next trigger
            }
            return;
        }
        // Move towards the current waypoint
        transform.position = Vector3.MoveTowards(transform.position, currentWaypoint.transform.position, speed * Time.deltaTime);
    
    }

    public void ToggleMoveForward()
    {
        moveForward = !moveForward;
    }
}
