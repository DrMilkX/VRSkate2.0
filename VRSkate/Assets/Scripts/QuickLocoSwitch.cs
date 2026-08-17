using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class QuickLocoSwitch : MonoBehaviour
{
    // menu
    private GameObject menuWindow;
    public bool canUseMenu = true;

    [System.Serializable]
    public class LocomotionOption
    {
        public enum ControllerMotionMode { Auto, SmoothMotion, Teleport }

        [Tooltip("Name shown in the menu")]
        public string name;

        [Tooltip("The MonoBehaviour that drives this locomotion mode")]
        public MonoBehaviour behavior;

    }
    public List<LocomotionOption> locos;
    public string CurrentLocomotionName = "Unknown";

    
    [Header("Player")]
    public InputActionReference advanceAction;
    private bool ispressed = false;
    public float menuDistance = 1.2f;
    public float menuHeightOffset = 0.1f;
    public Transform headset;


    void Start()
    {
        menuWindow = transform.Find("Menu").gameObject;
        // Subscribe to the advance action
        if (advanceAction != null){
            advanceAction.action.Enable();
        }
    }
    void Update()
    {
       
        if (advanceAction.action.IsPressed()){
            if(!ispressed && canUseMenu){
                // Debug.Log("[QuickLocoSwitch] Menu button pressed");
                ToggleMenu(!menuWindow.activeSelf);
            }
            ispressed = true;
        }
        else
        {
            ispressed = false;
        }

    }



    // change the locomotion on the player by name
    public void SwitchLocomotion(string name){
        foreach (LocomotionOption l in locos){
            if(l.name == name){
                l.behavior.enabled = true;
                Debug.Log("[QuickLocoSwitch] Switched to " + name);
                CurrentLocomotionName = name;
            }else
                l.behavior.enabled = false;
        }
        
    }

    // toggle showing the menu
    public void ToggleMenu(bool show)
    {
        if (show){
            // Debug.Log("[QuickLocoSwitch] Menu ON!");
            menuWindow.SetActive(true);
            PositionMenuInFrontOfPlayer();
        }else{
            // Debug.Log("[QuickLocoSwitch] Menu OFF!");
            menuWindow.SetActive(false);
        }
    }


    
    private void PositionMenuInFrontOfPlayer()
    {
        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        transform.position = new Vector3(
            headset.position.x + forward.x * menuDistance,
            headset.position.y + menuHeightOffset,
            headset.position.z + forward.z * menuDistance);

        transform.LookAt(headset.position, Vector3.up);
        transform.Rotate(0f, 180f, 0f);
    }

}
