using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using TMPro;

/// <summary>
/// LocomotionSwitcher — Attach to XR Origin.
///
/// Press the B button (right controller) to open a floating menu and pick a
/// locomotion mode. Each mode is a MonoBehaviour component (and optional extra
/// GameObjects such as a teleport ray) that gets enabled/disabled on switch.
///
/// To add a new locomotion type: add an entry to the Locomotion Options list
/// in the Inspector — no code changes required.
/// </summary>
public class LocomotionSwitcher : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [System.Serializable]
    public class LocomotionOption
    {
        public enum ControllerMotionMode
        {
            Auto,
            SmoothMotion,
            Teleport
        }

        [Tooltip("Name shown in the menu")]
        public string displayName;

        [Tooltip("The MonoBehaviour that drives this locomotion mode")]
        public MonoBehaviour locomotionComponent;

        [Tooltip("How the controller locomotion actions should behave in this mode. Auto infers teleport vs smooth movement from the assigned component/name.")]
        public ControllerMotionMode controllerMotionMode = ControllerMotionMode.Auto;

        [Tooltip("Extra GameObjects to enable with this mode (e.g. teleport ray interactor GO)")]
        public GameObject[] additionalObjects;
    }

    // No InputActionReference needed for B button — polled directly via XR Input API

    [Header("Locomotion Options")]
    [Tooltip("Add one entry per locomotion mode. Order determines menu order.")]
    public List<LocomotionOption> locomotionOptions = new List<LocomotionOption>();

    [Header("References")]
    [Tooltip("Main Camera / headset transform — used to position the menu")]
    public Transform headset;

    [Header("Menu Settings")]
    [Tooltip("Distance in front of the player the menu floats")]
    public float menuDistance = 1.2f;

    [Tooltip("Height offset from headset position")]
    public float menuHeightOffset = 0.1f;

    [Tooltip("Ray interactor GameObjects to show while the menu is open (and hide otherwise). " +
             "Drag in your left/right Ray Interactor GameObjects here.")]
    public GameObject[] menuRayObjects;

    [Header("Controller Input Managers")]
    [Tooltip("ControllerInputActionManager components that should follow the selected locomotion mode. Leave empty to auto-find them under this XR Origin.")]
    public ControllerInputActionManager[] controllerInputManagers;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private int activeIndex = -1;
    private bool menuOpen = false;
    private GameObject menuRoot;
    private readonly List<Button> menuButtons = new List<Button>();

    // B button polling
    private List<InputDevice> rightControllers = new List<InputDevice>();
    private bool bWasPressed = false;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (headset == null)
            headset = Camera.main.transform;

        if (controllerInputManagers == null || controllerInputManagers.Length == 0)
            controllerInputManagers = GetComponentsInChildren<ControllerInputActionManager>(true);

        BuildMenu();

        // Hide menu rays immediately so they don't flash on the first frame
        if (menuRayObjects != null)
            foreach (var go in menuRayObjects)
                if (go != null) go.SetActive(false);

        // Activate the first locomotion option by default
        if (locomotionOptions.Count > 0)
            ActivateLocomotion(0);

        SetMenuVisible(false);
    }

    private void Update()
    {
        PollBButton();
    }

    private void PollBButton()
    {
        // Refresh device list if empty (controllers connect after Start)
        if (rightControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightControllers);

        bool bPressed = false;
        foreach (var device in rightControllers)
        {
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool val) && val)
            {
                bPressed = true;
                break;
            }
        }

        // Rising edge — toggle menu on press, not hold
        if (bPressed && !bWasPressed)
            SetMenuVisible(!menuOpen);

        bWasPressed = bPressed;
    }

    // -------------------------------------------------------------------------
    // Menu visibility & positioning
    // -------------------------------------------------------------------------

    private void SetMenuVisible(bool visible)
    {
        menuOpen = visible;
        menuRoot.SetActive(visible);

        if (menuRayObjects != null)
            foreach (var go in menuRayObjects)
                if (go != null) go.SetActive(visible);

        if (visible)
            PositionMenuInFrontOfPlayer();
    }

    private void PositionMenuInFrontOfPlayer()
    {
        // Flatten headset forward to horizontal so the menu doesn't tilt with head pitch
        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 targetPos = headset.position
                            + forward * menuDistance
                            + Vector3.up * menuHeightOffset;

        menuRoot.transform.position = targetPos;

        // Point +Z at headset then flip 180° so text faces the player un-mirrored
        menuRoot.transform.LookAt(headset.position, Vector3.up);
        menuRoot.transform.Rotate(0f, 180f, 0f);
    }

    // -------------------------------------------------------------------------
    // Locomotion switching
    // -------------------------------------------------------------------------

    private void ActivateLocomotion(int index)
    {
        if (index < 0 || index >= locomotionOptions.Count) return;

        // Disable all, then enable the chosen one
        for (int i = 0; i < locomotionOptions.Count; i++)
        {
            bool active = (i == index);
            var opt = locomotionOptions[i];

            if (opt.locomotionComponent != null)
                opt.locomotionComponent.enabled = active;

            if (opt.additionalObjects != null)
                foreach (var go in opt.additionalObjects)
                    if (go != null) go.SetActive(active);
        }

        SyncControllerInputManagers(index);

        activeIndex = index;
        RefreshButtonHighlights();

        Debug.Log($"LocomotionSwitcher: Switched to '{locomotionOptions[index].displayName}'");
    }

    private void SyncControllerInputManagers(int index)
    {
        if (controllerInputManagers == null)
            return;

        bool useSmoothMotion = ResolveSmoothMotionEnabled(locomotionOptions[index]);

        foreach (var manager in controllerInputManagers)
        {
            if (manager == null)
                continue;

            manager.smoothMotionEnabled = useSmoothMotion;
        }
    }

    private static bool ResolveSmoothMotionEnabled(LocomotionOption option)
    {
        if (option.controllerMotionMode == LocomotionOption.ControllerMotionMode.SmoothMotion)
            return true;

        if (option.controllerMotionMode == LocomotionOption.ControllerMotionMode.Teleport)
            return false;

        if (option.locomotionComponent != null)
        {
            string componentName = option.locomotionComponent.GetType().Name;
            if (componentName.IndexOf("teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (componentName.IndexOf("move", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                componentName.IndexOf("locomotion", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (!string.IsNullOrWhiteSpace(option.displayName) &&
            option.displayName.IndexOf("teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return true;
    }

    // -------------------------------------------------------------------------
    // UI construction — procedural world-space canvas
    // -------------------------------------------------------------------------

    private void BuildMenu()
    {
        // Root canvas
        menuRoot = new GameObject("LocomotionMenu");
        Canvas canvas = menuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        CanvasScaler scaler = menuRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 200f;

        menuRoot.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform canvasRect = menuRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(400f, 600f);  // pixels; scale converts to ~0.4m × 0.6m
        canvasRect.localScale = Vector3.one * 0.001f;    // 1px = 1mm in world space

        // Background panel
        GameObject panel = CreatePanel(menuRoot.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Vertical layout for buttons
        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 12f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;


        // Title
        CreateLabel(panel.transform, "Locomotion Mode", 28, FontStyle.Bold);

        // One button per locomotion option
        menuButtons.Clear();
        for (int i = 0; i < locomotionOptions.Count; i++)
        {
            int capturedIndex = i;  // capture for closure
            Button btn = CreateButton(panel.transform, locomotionOptions[i].displayName, () =>
            {
                ActivateLocomotion(capturedIndex);
                SetMenuVisible(false);
            });
            menuButtons.Add(btn);
        }

        // Close button
        CreateButton(panel.transform, "[ Close ]", () => SetMenuVisible(false));
    }

    private GameObject CreatePanel(Transform parent)
    {
        GameObject go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.92f);
        return go;
    }

    private void CreateLabel(Transform parent, string text, int fontSize, FontStyle style)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = fontSize + 16f;
    }

    private Button CreateButton(Transform parent, string label, System.Action onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.18f, 0.22f, 1f);

        Button btn = go.AddComponent<Button>();

        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.9f, 1f);
        colors.pressedColor = new Color(0.2f, 0.6f, 1f, 1f);
        btn.colors = colors;
        btn.targetGraphic = bg;

        btn.onClick.AddListener(() => onClick());

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 52f;

        // Button label
        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(go.transform, false);

        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return btn;
    }

    private void RefreshButtonHighlights()
    {
        for (int i = 0; i < menuButtons.Count; i++)
        {
            if (menuButtons[i] == null) continue;
            Image bg = menuButtons[i].GetComponent<Image>();
            bg.color = (i == activeIndex)
                ? new Color(0.15f, 0.45f, 0.15f, 1f)   // green = active
                : new Color(0.18f, 0.18f, 0.22f, 1f);  // dark = inactive
        }
    }
}
