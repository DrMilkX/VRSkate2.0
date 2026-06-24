using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
public class AutoLocomotion : MonoBehaviour
{
    public GameObject[] waypoints;
    int currentWP = 0;

    public float speed = 10.0f;

    [Tooltip("How close the rig must get to a waypoint before it waits for trigger input.")]
    public float arrivalDistance = 0.15f;

    [Tooltip("If true, the route loops back to the first waypoint after the last one.")]
    public bool loopWaypoints = true;

    private bool waitingForTrigger = false;
    private bool triggerWasPressed = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("AutoLocomotion: No waypoints assigned.", this);
            enabled = false;
            return;
        }

        currentWP = Mathf.Clamp(currentWP, 0, waypoints.Length - 1);
    }

    // Update is called once per frame
    void Update()
    {

        if (waypoints == null || waypoints.Length == 0)
            return;

        bool triggerPressed = IsAnyTriggerPressed();

        if (waitingForTrigger)
        {
            if (triggerPressed && !triggerWasPressed)
                AdvanceWaypoint();

            triggerWasPressed = triggerPressed;
            return;
        }

        Transform target = waypoints[currentWP].transform;
        Vector3 targetPosition = target.position;

        if (Vector3.Distance(transform.position, targetPosition) <= arrivalDistance)
        {
            transform.position = targetPosition;
            waitingForTrigger = true;
            triggerWasPressed = triggerPressed;
            return;
        }

        transform.LookAt(targetPosition);
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
        triggerWasPressed = triggerPressed;
    }

    private void AdvanceWaypoint()
    {
        if (currentWP + 1 >= waypoints.Length)
        {
            if (!loopWaypoints)
            {
                waitingForTrigger = true;
                return;
            }

            currentWP = 0;
        }
        else
        {
            currentWP++;
        }

        waitingForTrigger = false;
    }

    private static bool IsAnyTriggerPressed()
    {
        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, devices);
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right, devices);

        foreach (var device in devices)
        {
            if (!device.isValid)
                continue;

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton) && triggerButton)
                return true;

            if (device.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue) && triggerValue > 0.5f)
                return true;
        }

        return false;
    }
}
