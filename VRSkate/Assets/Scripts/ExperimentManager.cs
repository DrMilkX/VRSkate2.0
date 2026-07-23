using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using TMPro;


public class ExperimentManager : MonoBehaviour
{
   
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


    void Start()
    {

        if (controllerInputManagers == null || controllerInputManagers.Length == 0)
            controllerInputManagers = FindObjectsByType<ControllerInputActionManager>(FindObjectsSortMode.None);


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

        // select a random locomotion for the first stage of the experiment
        if(isExperimentRunning)
        {
            RandomPickLocomotion();
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
        GameObject.Find("Waypoints").GetComponent<TransformWaypoint>().ShowParticularWaypoint(-1);
        GameObject lastpopup = GameObject.Find("StartExperimentPopup");
        lastpopup.GetComponent<Canvas>().enabled = true;
        // put the popup in front of the player

        PositionPopupInFrontOfPlayer(lastpopup);
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

        // isSmoothMotionStage only toggles the extra input-routing flag below —
        // it doesn't replace enabling the stage's own locomotion Behaviour
        // (e.g. a DynamicMoveProvider still needs .enabled = true to move at all).
        if (lc != null)
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

    public void AllowLocoSwitcher()
    {
        locoswitcher.enabled = true;
    }

}
