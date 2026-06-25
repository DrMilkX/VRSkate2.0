using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// LocomotionSwitcher — Attach to XR Origin.
/// B button opens a floating menu with three sections:
///   • Locomotion mode selection
///   • Hoverboard settings sliders (with per-session defaults)
///   • Scene switcher
/// </summary>
public class LocomotionSwitcher : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector — Locomotion
    // -------------------------------------------------------------------------

    [System.Serializable]
    public class LocomotionOption
    {
        [Tooltip("Name shown in the menu")]
        public string displayName;

        [Tooltip("The MonoBehaviour that drives this locomotion mode")]
        public MonoBehaviour locomotionComponent;

        [Tooltip("Extra GameObjects to enable with this mode (e.g. teleport ray GO)")]
        public GameObject[] additionalObjects;
    }

    [Header("Locomotion Options")]
    public List<LocomotionOption> locomotionOptions = new List<LocomotionOption>();

    // -------------------------------------------------------------------------
    // Inspector — References
    // -------------------------------------------------------------------------

    [Header("References")]
    [Tooltip("Main Camera / headset — used to position the menu")]
    public Transform headset;

    [Tooltip("HoverboardLocomotion script — for the settings submenu")]
    public HoverboardLocomotion hoverboard;


    // -------------------------------------------------------------------------
    // Inspector — Menu
    // -------------------------------------------------------------------------

    [Header("Menu Settings")]
    [Tooltip("Distance in front of the player the menu floats (metres)")]
    public float menuDistance = 1.2f;

    [Tooltip("Vertical offset from headset position (metres)")]
    public float menuHeightOffset = 0.1f;

    [Tooltip("Ray interactor line GameObjects — shown only while menu is open")]
    public GameObject[] menuRayObjects;

    // -------------------------------------------------------------------------
    // Inspector — Scene Switcher
    // -------------------------------------------------------------------------

    [Header("Scene Switcher")]
    [Tooltip("Scene names to list in the Switch Scene submenu. " +
             "Each must match the exact scene name in Build Settings.")]
    public string[] sceneNames;

    // -------------------------------------------------------------------------
    // Private — state
    // -------------------------------------------------------------------------

    private int activeIndex = -1;
    private bool menuOpen = false;

    // B button polling
    private List<InputDevice> rightControllers = new List<InputDevice>();
    private bool bWasPressed = false;

    // Panels
    private GameObject menuRoot;
    private GameObject mainPanel;
    private GameObject settingsPanel;
    private GameObject scenePanel;

    // Locomotion button highlights
    private readonly List<Button> locomotionButtons = new List<Button>();

    // Hoverboard defaults captured at Start
    private float defaultBrakeDeceleration;
    private float defaultCrouchSpeedMultiplier;
    private float defaultCarveSensitivity;
    private float defaultCarveDamping;
    private float defaultBoardHoverHeight;
    private float defaultWallDetectionDistance;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (headset == null)
            headset = Camera.main.transform;

        if (hoverboard == null)
            hoverboard = GetComponent<HoverboardLocomotion>();

        CacheHoverboardDefaults();
        BuildMenu();

        if (menuRayObjects != null)
            foreach (var go in menuRayObjects)
                if (go != null) go.SetActive(false);

        if (locomotionOptions.Count > 0)
            ActivateLocomotion(0);

        SetMenuVisible(false);
    }

    private void Update()
    {
        PollBButton();
    }

    // -------------------------------------------------------------------------
    // Defaults
    // -------------------------------------------------------------------------

    private void CacheHoverboardDefaults()
    {
        if (hoverboard == null) return;
        defaultBrakeDeceleration      = hoverboard.brakeDeceleration;
        defaultCrouchSpeedMultiplier  = hoverboard.crouchSpeedMultiplier;
        defaultCarveSensitivity       = hoverboard.carveSensitivity;
        defaultCarveDamping           = hoverboard.carveDamping;
        defaultBoardHoverHeight       = hoverboard.boardHoverHeight;
        defaultWallDetectionDistance  = hoverboard.wallDetectionDistance;
    }

    // -------------------------------------------------------------------------
    // B button polling
    // -------------------------------------------------------------------------

    private void PollBButton()
    {
        if (rightControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightControllers);

        bool bPressed = false;
        foreach (var device in rightControllers)
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool val) && val)
                { bPressed = true; break; }

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
        {
            ShowPanel(mainPanel);
            PositionMenuInFrontOfPlayer();
        }
    }

    private void PositionMenuInFrontOfPlayer()
    {
        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        menuRoot.transform.position = new Vector3(
            headset.position.x + forward.x * menuDistance,
            headset.position.y + menuHeightOffset,
            headset.position.z + forward.z * menuDistance);

        menuRoot.transform.LookAt(headset.position, Vector3.up);
        menuRoot.transform.Rotate(0f, 180f, 0f);
    }

    private void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        settingsPanel.SetActive(panel == settingsPanel);
        scenePanel.SetActive(panel == scenePanel);
    }

    // -------------------------------------------------------------------------
    // Locomotion switching
    // -------------------------------------------------------------------------

    private void ActivateLocomotion(int index)
    {
        if (index < 0 || index >= locomotionOptions.Count) return;

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

        activeIndex = index;
        RefreshLocomotionHighlights();
        Debug.Log($"LocomotionSwitcher: '{locomotionOptions[index].displayName}'");
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildMenu()
    {
        menuRoot = new GameObject("LocomotionMenu");

        Canvas canvas = menuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        CanvasScaler scaler = menuRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 200f;

        menuRoot.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform rt = menuRoot.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(420f, 700f);
        rt.localScale = Vector3.one * 0.001f;

        mainPanel     = BuildMainPanel(menuRoot.transform);
        settingsPanel = BuildSettingsPanel(menuRoot.transform);
        scenePanel    = BuildScenePanel(menuRoot.transform);
    }

    // ── Main panel ────────────────────────────────────────────────────────────

    private GameObject BuildMainPanel(Transform parent)
    {
        GameObject panel = MakePanel(parent, "MainPanel");

        CreateLabel(panel.transform, "Menu", 30, true);

        // Locomotion buttons
        CreateLabel(panel.transform, "Locomotion Mode", 22, false);
        locomotionButtons.Clear();
        for (int i = 0; i < locomotionOptions.Count; i++)
        {
            int idx = i;
            Button btn = CreateButton(panel.transform, locomotionOptions[i].displayName, () =>
            {
                ActivateLocomotion(idx);
                SetMenuVisible(false);
            });
            locomotionButtons.Add(btn);
        }

        CreateDivider(panel.transform);

        // Submenus
        CreateButton(panel.transform, "Board Settings  >", () => ShowPanel(settingsPanel));
        CreateButton(panel.transform, "Switch Scene  >",   () => ShowPanel(scenePanel));

        CreateDivider(panel.transform);
        CreateButton(panel.transform, "[ Close ]", () => SetMenuVisible(false));

        return panel;
    }

    // ── Settings panel ────────────────────────────────────────────────────────

    private GameObject BuildSettingsPanel(Transform parent)
    {
        GameObject panel = MakePanel(parent, "SettingsPanel");

        CreateLabel(panel.transform, "Board Settings", 30, true);

        if (hoverboard != null)
        {
            CreateSliderRow(panel.transform, "Brake Decel",      0.1f, 10f,  hoverboard.brakeDeceleration,
                v => hoverboard.brakeDeceleration = v);
            CreateSliderRow(panel.transform, "Crouch Speed",     0f,   8f,   hoverboard.crouchSpeedMultiplier,
                v => hoverboard.crouchSpeedMultiplier = v);
            CreateSliderRow(panel.transform, "Carve Sensitivity",0f,   3f,   hoverboard.carveSensitivity,
                v => hoverboard.carveSensitivity = v);
            CreateSliderRow(panel.transform, "Carve Damping",    0f,   20f,  hoverboard.carveDamping,
                v => hoverboard.carveDamping = v);
            CreateSliderRow(panel.transform, "Hover Height",     -1f,   1f, hoverboard.boardHoverHeight,
                v => hoverboard.boardHoverHeight = v);
            CreateSliderRow(panel.transform, "Wall Detect Dist", 0.1f, 3f,   hoverboard.wallDetectionDistance,
                v => hoverboard.wallDetectionDistance = v);
        }
        else
        {
            CreateLabel(panel.transform, "(No HoverboardLocomotion found)", 18, false);
        }

        CreateDivider(panel.transform);

        // Default + Back
        CreateButton(panel.transform, "Reset to Defaults", ResetHoverboardDefaults);
        CreateButton(panel.transform, "<  Back", () => ShowPanel(mainPanel));

        return panel;
    }

    private void ResetHoverboardDefaults()
    {
        if (hoverboard == null) return;
        hoverboard.brakeDeceleration     = defaultBrakeDeceleration;
        hoverboard.crouchSpeedMultiplier  = defaultCrouchSpeedMultiplier;
        hoverboard.carveSensitivity       = defaultCarveSensitivity;
        hoverboard.carveDamping           = defaultCarveDamping;
        hoverboard.boardHoverHeight       = defaultBoardHoverHeight;
        hoverboard.wallDetectionDistance  = defaultWallDetectionDistance;

        // Rebuild settings panel so sliders snap back to default positions
        Destroy(settingsPanel);
        settingsPanel = BuildSettingsPanel(menuRoot.transform);
        ShowPanel(settingsPanel);
    }

    // ── Scene panel ───────────────────────────────────────────────────────────

    private GameObject BuildScenePanel(Transform parent)
    {
        GameObject panel = MakePanel(parent, "ScenePanel");

        CreateLabel(panel.transform, "Switch Scene", 30, true);

        if (sceneNames != null && sceneNames.Length > 0)
        {
            foreach (string sceneName in sceneNames)
            {
                string captured = sceneName;
                CreateButton(panel.transform, captured, () =>
                {
                    SetMenuVisible(false);
                    SceneManager.LoadScene(captured);
                });
            }
        }
        else
        {
            CreateLabel(panel.transform, "(No scenes configured in Inspector)", 18, false);
        }

        CreateDivider(panel.transform);
        CreateButton(panel.transform, "<  Back", () => ShowPanel(mainPanel));

        return panel;
    }

    // -------------------------------------------------------------------------
    // UI helpers
    // -------------------------------------------------------------------------

    private GameObject MakePanel(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.93f);

        VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 10f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;

        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return go;
    }

    private void CreateLabel(Transform parent, string text, int fontSize, bool bold)
    {
        GameObject go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        go.AddComponent<LayoutElement>().preferredHeight = fontSize + 14f;
    }

    private void CreateDivider(Transform parent)
    {
        GameObject go = new GameObject("Divider");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);
        go.AddComponent<LayoutElement>().preferredHeight = 2f;
    }

    private Button CreateButton(Transform parent, string label, System.Action onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.18f, 0.22f, 1f);

        Button btn = go.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.3f, 0.3f, 0.9f, 1f);
        cb.pressedColor     = new Color(0.2f, 0.6f, 1f, 1f);
        btn.colors = cb;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick());

        go.AddComponent<LayoutElement>().preferredHeight = 52f;

        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(go.transform, false);
        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        RectTransform lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = lr.offsetMax = Vector2.zero;

        return btn;
    }

    /// <summary>
    /// Creates a labeled slider row:
    ///   [Label  0.00] ←——●——→
    /// The value label updates live as the slider moves.
    /// </summary>
    private void CreateSliderRow(Transform parent, string label,
                                 float min, float max, float initial,
                                 System.Action<float> onChange)
    {
        // Container
        GameObject container = new GameObject("Row_" + label);
        container.transform.SetParent(parent, false);
        VerticalLayoutGroup vl = container.AddComponent<VerticalLayoutGroup>();
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.childControlHeight = true;
        vl.spacing = 2f;
        container.AddComponent<LayoutElement>().preferredHeight = 72f;

        // Top row: name + value
        GameObject headerRow = new GameObject("Header");
        headerRow.transform.SetParent(container.transform, false);
        HorizontalLayoutGroup hl = headerRow.AddComponent<HorizontalLayoutGroup>();
        hl.childForceExpandWidth = true;
        hl.childForceExpandHeight = false;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        headerRow.AddComponent<LayoutElement>().preferredHeight = 28f;

        // Name label (left — stretches to fill available space)
        GameObject nameGO = new GameObject("Name");
        nameGO.transform.SetParent(headerRow.transform, false);
        TextMeshProUGUI nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text = label;
        nameTmp.fontSize = 18;
        nameTmp.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        nameTmp.alignment = TextAlignmentOptions.Left;
        LayoutElement nle = nameGO.AddComponent<LayoutElement>();
        nle.flexibleWidth = 1f;

        // Value label (right — fixed narrow column)
        GameObject valGO = new GameObject("Value");
        valGO.transform.SetParent(headerRow.transform, false);
        TextMeshProUGUI valTmp = valGO.AddComponent<TextMeshProUGUI>();
        valTmp.text = initial.ToString("F2");
        valTmp.fontSize = 18;
        valTmp.color = Color.white;
        valTmp.alignment = TextAlignmentOptions.Right;
        LayoutElement vle = valGO.AddComponent<LayoutElement>();
        vle.preferredWidth = 60f;
        vle.flexibleWidth = 0f;

        // Slider
        GameObject sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(container.transform, false);
        sliderGO.AddComponent<LayoutElement>().preferredHeight = 36f;

        Slider slider = BuildSlider(sliderGO, min, max, initial);
        slider.onValueChanged.AddListener(v =>
        {
            valTmp.text = v.ToString("F2");
            onChange(v);
        });
    }

    private Slider BuildSlider(GameObject go, float min, float max, float value)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.25f);
        bgRt.anchorMax = new Vector2(1f, 0.75f);
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // Fill area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        RectTransform faRt = fillArea.AddComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.25f);
        faRt.anchorMax = new Vector2(1f, 0.75f);
        faRt.offsetMin = new Vector2(5f, 0f);
        faRt.offsetMax = new Vector2(-15f, 0f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.6f, 1f, 1f);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

        // Handle area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        RectTransform haRt = handleArea.AddComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(10f, 0f);
        haRt.offsetMax = new Vector2(-10f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        RectTransform hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(20f, 0f);
        hRt.anchorMin = new Vector2(0f, 0f);
        hRt.anchorMax = new Vector2(0f, 1f);

        Slider slider = go.AddComponent<Slider>();
        slider.fillRect   = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handleImg;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value    = value;
        slider.wholeNumbers = false;

        ColorBlock cb = slider.colors;
        cb.highlightedColor = new Color(0.9f, 0.9f, 1f, 1f);
        slider.colors = cb;

        return slider;
    }

    private void RefreshLocomotionHighlights()
    {
        for (int i = 0; i < locomotionButtons.Count; i++)
        {
            if (locomotionButtons[i] == null) continue;
            locomotionButtons[i].GetComponent<Image>().color = (i == activeIndex)
                ? new Color(0.15f, 0.45f, 0.15f, 1f)
                : new Color(0.18f, 0.18f, 0.22f, 1f);
        }
    }
}
