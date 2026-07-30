using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using TMPro;

public class ExperimentManager : MonoBehaviour
{
    [Header("Debug")]
    public GameObject player;

    [Tooltip("The controller managing marker visibility.")]
    public WaypointCheckpointVisibilityController waypointManager;

    [Tooltip("The sequence holding the physical waypoint transforms.")]
    public WaypointCheckpointSequence checkpointSequence;

    public Transform headset;
    public Behaviour locoswitcher;

    public Transform helpPopup;
    public TextMeshProUGUI helperTextTitle;
    public TextMeshProUGUI helperTextInstr;
    public float popupDistance = 1.2f;
    public float popupHeightOffset = 0.1f;

    private List<Behaviour> allLocomotionBehaviours = new List<Behaviour>();

    [Header("Controller Input Managers")]
    [Tooltip("Used to toggle smooth motion for the joystick stage. Auto-found on Start if left empty.")]
    public ControllerInputActionManager[] controllerInputManagers;

    public Dictionary<string, string> locoHelper = new Dictionary<string, string>()
    {
        { "automove", "You will be moved to the waypoint automatically in 5 seconds." },
        { "teleport", "Tilt the joystick up, aim, release" },
        { "joystick", "Tilt the left joystick in the direction to move" },
        { "walking", "Alternate moving the left and right controllers up and down in front of you" },
        { "skateboard", "Step forward and press the trigger button\nHold the trigger to slow down\nCrouch to speed up\nHold the grip and tilt the controller to turn" },
    };

    [System.Serializable]
    public class LocCheck
    {
        public string locomotionName;
        public Behaviour locomotionBehaviour;
        public bool isSmoothMotionStage;
        public bool used = false;

        // constructor
        public LocCheck(string locomotionName, Behaviour locomotionBehaviour, bool isSmoothMotionStage, bool used)
        {
            this.locomotionName = locomotionName;
            this.locomotionBehaviour = locomotionBehaviour;
            this.isSmoothMotionStage = isSmoothMotionStage;
            this.used = used;
        }
    }

    public List<LocCheck> locomotionChecks = new List<LocCheck>();
    private int currentLocomotionIndex = 0;

    public bool isExperimentRunning = false;
    public bool useAutoMoveFirst = true;

    void Start()
    {
        if (controllerInputManagers == null || controllerInputManagers.Length == 0)
            controllerInputManagers = FindObjectsByType<ControllerInputActionManager>();

        // set all locomotion behaviours to disabled, and add them to the list
        foreach (var locCheck in locomotionChecks)
        {
            if (locCheck.locomotionBehaviour != null)
            {
                locCheck.locomotionBehaviour.enabled = false;
                locCheck.used = false;
                allLocomotionBehaviours.Add(locCheck.locomotionBehaviour);
            }
        }

        // use the automover first or random locomotion
        if (isExperimentRunning)
        {
            if (useAutoMoveFirst)
            {
                ApplyLocomotionLock(locomotionChecks[0]);
                ShowHelperTextForCurrentLocomotion();

                // Immediately disable AutoMove so the player doesn't move during the 5-second popup phase
                locomotionChecks[0].locomotionBehaviour.enabled = false;
                locomotionChecks[0].used = true;

                StartCoroutine(ActivateAutoMove());
            }
            else
            {
                RandomPickLocomotion();
            }
        }
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            // Stop the experiment if the Escape key is pressed
            if (Keyboard.current.escapeKey.wasPressedThisFrame == true)
                StopExperiment();

            // move the player to the current waypoint position, but slightly behind it
            if (Keyboard.current.wKey.wasPressedThisFrame == true && waypointManager != null && checkpointSequence != null)
            {
                Transform targetWp = checkpointSequence.GetCheckpoint(waypointManager.CurrentWaypointIndex);
                if (targetWp != null)
                {
                    player.transform.position = targetWp.position + new Vector3(0, 0, 2f);
                }
            }
        }
    }

    private void PositionPopupInFrontOfPlayer(GameObject popup)
    {
        if (popup == null || headset == null) return;

        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        popup.gameObject.SetActive(true);

        popup.transform.position = new Vector3(
            headset.position.x + forward.x * popupDistance,
            headset.position.y + popupHeightOffset,
            headset.position.z + forward.z * popupDistance);

        popup.transform.LookAt(headset.position, Vector3.up);
        popup.transform.Rotate(0f, 180f, 0f);
    }

    // ----- tutorial functions ------
    public void PreExperimentStart()
    {
        if (waypointManager != null)
        {
            waypointManager.DeactivateAllWaypoints();
        }

        GameObject lastpopup = GameObject.Find("StartExperimentPopup");
        if (lastpopup != null)
        {
            lastpopup.GetComponent<Canvas>().enabled = true;
            // put the popup in front of the player

            PositionPopupInFrontOfPlayer(lastpopup);
        }
    }

    public void StartExperiment()
    {
        // Load the "Experiment" scene
        UnityEngine.SceneManagement.SceneManager.LoadScene("Experiment");
    }

    // ----- experiment functions ------
    private void ApplyLocomotionLock(LocCheck lc)
    {
        bool isSmoothMotionStage = lc.isSmoothMotionStage;
        foreach (var behaviour in allLocomotionBehaviours)
            if (behaviour != null) behaviour.enabled = false;

        if (lc != null && lc.locomotionBehaviour != null)
            lc.locomotionBehaviour.enabled = true;

        if (controllerInputManagers != null)
            foreach (var m in controllerInputManagers)
                if (m != null) m.smoothMotionEnabled = isSmoothMotionStage;
    }

    public void RandomPickLocomotion()
    {
        // pick a random locomotion from the list that hasn't been used yet
        List<LocCheck> unusedLocomotions = locomotionChecks.FindAll(lc => !lc.used);
        if (unusedLocomotions.Count == 0)
        {
            Debug.LogWarning("ExperimentManager: All locomotion stages have been used. Resetting usage flags.");
            foreach (var lc in locomotionChecks)
                lc.used = false;
            unusedLocomotions = locomotionChecks;
        }

        LocCheck randomLocomotion = unusedLocomotions[Random.Range(0, unusedLocomotions.Count)];
        randomLocomotion.used = true;

        ApplyLocomotionLock(randomLocomotion);

        // set the current locomotion index to the index of the randomly picked locomotion
        currentLocomotionIndex = locomotionChecks.IndexOf(randomLocomotion);
        ShowHelperTextForCurrentLocomotion();

        Debug.Log($"ExperimentManager: Randomly picked locomotion '{randomLocomotion.locomotionName}' for this stage.");
    }

    public void ShowHelperTextForCurrentLocomotion()
    {
        PositionPopupInFrontOfPlayer(helpPopup.gameObject);
        LocCheck currentLocomotion = locomotionChecks.Find(lc => lc.locomotionBehaviour.enabled);

        if (currentLocomotion != null && locoHelper.ContainsKey(currentLocomotion.locomotionName))
        {
            string helperText = locoHelper[currentLocomotion.locomotionName];
            helperText = helperText.Replace("\\n", "\n"); // replace escaped newlines with actual newlines
            helpPopup.gameObject.SetActive(true);
            helperTextInstr.GetComponent<TMPro.TextMeshProUGUI>().text = helperText;
            helperTextTitle.GetComponent<TMPro.TextMeshProUGUI>().text = $"[ {currentLocomotion.locomotionName.ToUpper()} ] MODE ACTIVE";
            Debug.Log($"ExperimentManager: Helper text for '{currentLocomotion.locomotionName}': {helperText}");
        }
        else
        {
            Debug.LogWarning("ExperimentManager: No helper text found for the current locomotion.");
        }
    }

    public void ShowFreePlayHelperText()
    {
        PositionPopupInFrontOfPlayer(helpPopup.gameObject);
        string helperText = "You are now in free play mode.\n\nPress B to switch locomotion modes in the menu and select with trigger.";
        helpPopup.gameObject.SetActive(true);
        helperTextInstr.GetComponent<TMPro.TextMeshProUGUI>().text = helperText;
        helperTextTitle.GetComponent<TMPro.TextMeshProUGUI>().text = $"[ FREE PLAY ] MODE ACTIVE";
        Debug.Log($"ExperimentManager: Free play helper text: {helperText}");
    }

    public void ShowExperimentEndHelperText()
    {
        PositionPopupInFrontOfPlayer(helpPopup.gameObject);
        string helperText = "The experiment is now complete!\n\nYou may remove the headset.";
        helpPopup.gameObject.SetActive(true);
        helperTextInstr.GetComponent<TMPro.TextMeshProUGUI>().text = helperText;
        helperTextTitle.GetComponent<TMPro.TextMeshProUGUI>().text = $"[ EXPERIMENT COMPLETE ]";
        Debug.Log($"ExperimentManager: Experiment end helper text: {helperText}");
    }

    public void AllowLocoSwitcher()
    {
        locoswitcher.enabled = true;
        ShowFreePlayHelperText();
    }

    IEnumerator ActivateAutoMove()
    {
        yield return new WaitForSeconds(5f);
        if (locomotionChecks.Count > 0 && locomotionChecks[0].locomotionName == "automove" && locomotionChecks[0].locomotionBehaviour != null)
        {
            // Enabling the script triggers Start() and AlignToCurrentWaypoint() natively
            locomotionChecks[0].locomotionBehaviour.enabled = true;
        }
    }

    public string GetCurrentLocomotionName()
    {
        LocCheck currentLocomotion = locomotionChecks.Find(lc => lc.locomotionBehaviour.enabled);
        if (currentLocomotion != null)
        {
            return currentLocomotion.locomotionName;
        }
        else
        {
            return "Unknown";
        }
    }

    public void StopExperiment()
    {
        // disable all locomotion behaviours
        foreach (var behaviour in allLocomotionBehaviours)
            if (behaviour != null) behaviour.enabled = false;

        // disable the loco switcher
        if (locoswitcher != null) locoswitcher.enabled = false;
        // hide the helper popup
        ShowExperimentEndHelperText();

        Debug.Log("ExperimentManager: Experiment stopped. All locomotion disabled.");
    }
}