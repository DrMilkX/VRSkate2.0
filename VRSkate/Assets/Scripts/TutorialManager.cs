using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using TMPro;

/// <summary>
/// Drives the in-world locomotion tutorial: builds one floating popup
/// (instructions + looping demo video + controller diagram) per stage,
/// and advances between stages as the player walks into each WaypointArea.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [System.Serializable]
    public class TutorialStage
    {
        public string stageName;

        [TextArea]
        public string instructions;

        public VideoClip demoClip;
        public Sprite diagramSprite;
        public string diagramCaption;

        [Tooltip("MonoBehaviour to enable while this stage is active (e.g. ArmSwingLocomotion, HoverboardLocomotion). Leave empty for the intro stage.")]
        public Behaviour locomotionToEnable;

        [Tooltip("Check for the smooth/joystick-move stage — toggles ControllerInputActionManager.smoothMotionEnabled instead of a single Behaviour.")]
        public bool isSmoothMotionStage;

        [Tooltip("Trigger volume that completes THIS stage and advances to the next one.")]
        public WaypointArea advanceTrigger;
    }

    [Header("Respawn")]
    public GameObject player;
    public Transform respawn;

    [Header("Tutorial Stages")]
    public List<TutorialStage> stages = new List<TutorialStage>();

    [Header("Locomotion Lock")]
    [Tooltip("Every locomotion technique in the scene. All but the current stage's target are force-disabled while the tutorial runs.")]
    public List<Behaviour> allLocomotionBehaviours = new List<Behaviour>();

    [Tooltip("The player's usual B-menu — disabled for the duration of the tutorial so they can't switch modes, and handed back after the last stage. Auto-found on Awake if left empty.")]
    public LocomotionSwitcher locomotionSwitcherToDisable;

    [Header("Controller Input Managers")]
    [Tooltip("Used to toggle smooth motion for the joystick stage. Auto-found on Start if left empty.")]
    public ControllerInputActionManager[] controllerInputManagers;

    [Header("Popup Follow")]
    [Tooltip("Popup floats in front of this transform, same as the usual locomotion menu. Defaults to Camera.main.")]
    public Transform headset;
    public float popupDistance = 1.2f;
    public float popupHeightOffset = 0.1f;

    [Header("Popup Layout")]
    public float canvasScale = 0.00033f;
    public float dynamicPixelsPerUnit = 200f;

    private static readonly Color BorderColor = new Color(1f, 0.1f, 0.7f);
    private static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.05f, 0.93f);

    private int currentStageIndex = -1;
    private bool popupVisible = false;
    private readonly List<GameObject> popupRoots = new List<GameObject>();
    private readonly List<VideoPlayer> videoPlayers = new List<VideoPlayer>();
    private readonly List<RenderTexture> renderTextures = new List<RenderTexture>();

    // B/Y button polling — same input LocomotionSwitcher's menu toggle uses
    private readonly List<InputDevice> leftControllers = new List<InputDevice>();
    private readonly List<InputDevice> rightControllers = new List<InputDevice>();
    private bool toggleButtonWasPressed = false;

    // -------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------

    void Awake()
    {
        // Runs before any Start() in the scene, so LocomotionSwitcher's own
        // Start()/BuildMenu()/ActivateLocomotion() never fires while it's
        // disabled here — it fires later, once CompleteTutorial() re-enables it.
        if (locomotionSwitcherToDisable == null)
            locomotionSwitcherToDisable = FindAnyObjectByType<LocomotionSwitcher>();
        if (locomotionSwitcherToDisable != null)
            locomotionSwitcherToDisable.enabled = false;
    }

    void Start()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            Debug.Log("[TUTORIAL] Keyboard detected");
        }
        else
        {
            Debug.Log("[TUTORIAL] Keyboard not detected");
        }

        if (headset == null)
            headset = Camera.main.transform;

        if (controllerInputManagers == null || controllerInputManagers.Length == 0)
            controllerInputManagers = FindObjectsByType<ControllerInputActionManager>();

        BuildAllPopups();

        if (stages.Count > 0)
            ActivateStage(0);


        // add all locomotion behaviours in the scene to the list if not already present
        if (allLocomotionBehaviours == null || allLocomotionBehaviours.Count == 0){
            allLocomotionBehaviours = new List<Behaviour>();
            // get from the stages
            foreach (var stage in stages){
                if (stage.locomotionToEnable != null && !allLocomotionBehaviours.Contains(stage.locomotionToEnable)){
                    allLocomotionBehaviours.Add(stage.locomotionToEnable);
                }
            }
        }
            
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current?.rKey.wasPressedThisFrame == true)
        {
            Debug.Log("[TUTORIAL] Respawn key pressed");
            RespawnPlayer();
        }

        PollToggleButton();
    }

    private void PollToggleButton()
    {
        if (currentStageIndex < 0) return;

        if (leftControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
                leftControllers);

        if (rightControllers.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightControllers);

        bool pressed = false;
        foreach (var device in leftControllers)
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool val) && val)
            { pressed = true; break; }

        if (!pressed)
            foreach (var device in rightControllers)
                if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool val) && val)
                { pressed = true; break; }

        if (pressed && !toggleButtonWasPressed)
            SetPopupVisible(!popupVisible);

        toggleButtonWasPressed = pressed;
    }

    private void SetPopupVisible(bool visible)
    {
        popupVisible = visible;
        if (currentStageIndex < 0 || currentStageIndex >= popupRoots.Count) return;

        popupRoots[currentStageIndex].SetActive(visible);

        if (visible)
        {
            PositionPopupInFrontOfPlayer(popupRoots[currentStageIndex]);

            // reset the video to the beginning and play it
            videoPlayers[currentStageIndex]?.Prepare();     
            videoPlayers[currentStageIndex]?.Play();
        }
        else
        {
            videoPlayers[currentStageIndex]?.Stop();
        }
    }

    private void PositionPopupInFrontOfPlayer(GameObject popup)
    {
        if (popup == null || headset == null) return;

        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        popup.transform.position = new Vector3(
            headset.position.x + forward.x * popupDistance,
            headset.position.y + popupHeightOffset,
            headset.position.z + forward.z * popupDistance);

        popup.transform.LookAt(headset.position, Vector3.up);
        popup.transform.Rotate(0f, 180f, 0f);
    }

    public void RespawnPlayer()
    {
        if (player != null && respawn != null)
        {
            player.transform.position = respawn.position;
            player.transform.rotation = respawn.rotation;
            Debug.Log("[TUTORIAL] Player respawned");
        }
        else
        {
            Debug.LogWarning("[TUTORIAL] Player or respawn point not assigned");
        }
    }

    // -------------------------------------------------------------------
    // Stage progression
    // -------------------------------------------------------------------

    private void BuildAllPopups()
    {
        for (int i = 0; i < stages.Count; i++)
        {
            TutorialStage stage = stages[i];
            GameObject popup = BuildStagePopup(stage, out VideoPlayer vp, out RenderTexture rt);
            popupRoots.Add(popup);
            videoPlayers.Add(vp);
            renderTextures.Add(rt);
            popup.SetActive(false);

            if (stage.advanceTrigger == null) continue;

            if (i == stages.Count - 1)
            {
                stage.advanceTrigger.onPlayerEnter.AddListener(CompleteTutorial);
            }
            else
            {
                int nextIndex = i + 1;
                stage.advanceTrigger.onPlayerEnter.AddListener(() => AdvanceTo(nextIndex));
            }
        }
    }

    public void AdvanceTo(int index)
    {
        if (index < 0 || index >= stages.Count || index == currentStageIndex) return;

        DeactivateCurrentPopup();
        ActivateStage(index);
        Debug.Log($"TutorialManager: Advanced to stage {index + 1} - {stages[index].stageName}");
    }

    public void CompleteTutorial()
    {
        DeactivateCurrentPopup();
        currentStageIndex = -1;

        // Re-enabling fires LocomotionSwitcher's Start() for the first time
        // (it never ran while disabled from Awake), which re-establishes
        // normal mode switching via its own ActivateLocomotion(0).
        if (locomotionSwitcherToDisable != null)
            locomotionSwitcherToDisable.enabled = true;
    }

    private void ActivateStage(int index)
    {
        currentStageIndex = index;
        ApplyLocomotionLock(stages[index]);
        SetPopupVisible(true); // shown automatically when a new stage begins; B/X toggles it after that
    }

    private void DeactivateCurrentPopup()
    {
        if (currentStageIndex < 0) return;
        SetPopupVisible(false);
    }

    private void ApplyLocomotionLock(TutorialStage stage)
    {
        foreach (var behaviour in allLocomotionBehaviours)
            if (behaviour != null) behaviour.enabled = false;

        // isSmoothMotionStage only toggles the extra input-routing flag below —
        // it doesn't replace enabling the stage's own locomotion Behaviour
        // (e.g. a DynamicMoveProvider still needs .enabled = true to move at all).
        if (stage.locomotionToEnable != null)
            stage.locomotionToEnable.enabled = true;

        if (controllerInputManagers != null)
            foreach (var m in controllerInputManagers)
                if (m != null) m.smoothMotionEnabled = stage.isSmoothMotionStage;

        Debug.Log($"TutorialManager: Locomotion lock applied for stage {currentStageIndex + 1} - {stage.stageName}");
    }

    // -------------------------------------------------------------------
    // Popup construction
    // -------------------------------------------------------------------

    private const float MediaHeight = 240f;
    private const float DiagramWidth = 180f;
    private const float RowSpacing = 20f;

    private GameObject BuildStagePopup(TutorialStage stage, out VideoPlayer videoPlayer, out RenderTexture renderTexture)
    {
        float clipAspect = 16f / 9f;
        if (stage.demoClip != null && stage.demoClip.width > 0 && stage.demoClip.height > 0)
            clipAspect = (float)stage.demoClip.width / stage.demoClip.height;

        float videoWidth = MediaHeight * clipAspect;
        // + one panel's worth of border/padding chrome so the text row roughly
        // lines up with the video+diagram row underneath it (each bordered
        // panel adds ~40px of chrome that raw content widths don't account for).
        float textWidth = videoWidth + RowSpacing + DiagramWidth + 40f;

        GameObject root = new GameObject("TutorialPopup_" + stage.stageName);
        root.transform.SetParent(transform, false);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = dynamicPixelsPerUnit;

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * canvasScale;

        VerticalLayoutGroup rootLayout = root.AddComponent<VerticalLayoutGroup>();
        rootLayout.spacing = RowSpacing;
        rootLayout.childForceExpandWidth = false;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter rootFitter = root.AddComponent<ContentSizeFitter>();
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Row 1: title + instructions, sized to wrap at roughly the width of the media row below
        GameObject textPanel = BuildBorderedPanel(root.transform, "TextPanel");
        CreateLabel(textPanel.transform, stage.stageName, 28, true, textWidth);
        CreateLabel(textPanel.transform, stage.instructions, 20, false, textWidth);

        // Row 2: video + diagram, each sized to its own content, not stretched to match
        GameObject mediaRow = new GameObject("MediaRow");
        mediaRow.transform.SetParent(root.transform, false);
        mediaRow.AddComponent<RectTransform>();
        HorizontalLayoutGroup mediaLayout = mediaRow.AddComponent<HorizontalLayoutGroup>();
        mediaLayout.spacing = RowSpacing;
        mediaLayout.childControlWidth = true;
        mediaLayout.childControlHeight = true;
        ContentSizeFitter mediaFitter = mediaRow.AddComponent<ContentSizeFitter>();
        mediaFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        mediaFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Row 3: instructions to pop up the menu again, sized to wrap at roughly the width of the media row above
        GameObject footerPanel = BuildBorderedPanel(root.transform, "FooterPanel");
        CreateLabel(footerPanel.transform, "Press B/Y to toggle this menu", 18, false, textWidth);

        GameObject videoPanel = BuildBorderedPanel(mediaRow.transform, "VideoPanel");
        videoPlayer = BuildVideoSurface(videoPanel.transform, stage.demoClip, videoWidth, MediaHeight, out renderTexture);

        GameObject diagramPanel = BuildBorderedPanel(mediaRow.transform, "DiagramPanel");
        BuildDiagramImage(diagramPanel.transform, stage.diagramSprite, stage.diagramCaption, DiagramWidth, MediaHeight);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        return root;
    }

    private GameObject BuildBorderedPanel(Transform parent, string name)
    {
        GameObject outer = new GameObject(name);
        outer.transform.SetParent(parent, false);
        Image border = outer.AddComponent<Image>();
        border.color = BorderColor;

        VerticalLayoutGroup outerLayout = outer.AddComponent<VerticalLayoutGroup>();
        outerLayout.padding = new RectOffset(6, 6, 6, 6);
        outerLayout.childForceExpandWidth = true;
        outerLayout.childForceExpandHeight = true;
        outerLayout.childControlWidth = true;
        outerLayout.childControlHeight = true;

        ContentSizeFitter outerFitter = outer.AddComponent<ContentSizeFitter>();
        outerFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        outerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject inner = new GameObject("Content");
        inner.transform.SetParent(outer.transform, false);
        Image bg = inner.AddComponent<Image>();
        bg.color = PanelColor;

        VerticalLayoutGroup innerLayout = inner.AddComponent<VerticalLayoutGroup>();
        innerLayout.padding = new RectOffset(14, 14, 12, 12);
        innerLayout.spacing = 8f;
        innerLayout.childForceExpandWidth = true;
        innerLayout.childForceExpandHeight = false;
        innerLayout.childControlWidth = true;
        innerLayout.childControlHeight = true;
        innerLayout.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter innerFitter = inner.AddComponent<ContentSizeFitter>();
        innerFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        innerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return inner;
    }

    private VideoPlayer BuildVideoSurface(Transform parent, VideoClip clip, float displayWidth, float displayHeight, out RenderTexture renderTexture)
    {
        renderTexture = null;
        if (clip == null)
        {
            Debug.LogWarning("TutorialManager: A video panel has no VideoClip assigned.", this);
            return null;
        }

        int texWidth = clip.width > 0 ? (int)clip.width : 1920;
        int texHeight = clip.height > 0 ? (int)clip.height : 1080;
        renderTexture = new RenderTexture(texWidth, texHeight, 0, RenderTextureFormat.ARGB32);
        renderTexture.Create();

        GameObject playerGO = new GameObject("VideoPlayer_" + clip.name);
        playerGO.transform.SetParent(parent, false);
        VideoPlayer videoPlayer = playerGO.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.source = VideoSource.VideoClip;
        videoPlayer.clip = clip;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;
        videoPlayer.Prepare();

        GameObject surface = new GameObject("VideoSurface");
        surface.transform.SetParent(parent, false);
        RawImage raw = surface.AddComponent<RawImage>();
        raw.texture = renderTexture;

        LayoutElement le = surface.AddComponent<LayoutElement>();
        le.preferredWidth = displayWidth;
        le.preferredHeight = displayHeight;

        return videoPlayer;
    }

    private void BuildDiagramImage(Transform parent, Sprite sprite, string caption, float width, float height)
    {
        GameObject imgGO = new GameObject("DiagramImage");
        imgGO.transform.SetParent(parent, false);
        Image img = imgGO.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;

        LayoutElement le = imgGO.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height - 30f; // leave room for the caption label below

        if (!string.IsNullOrEmpty(caption))
            CreateLabel(parent, caption, 20, true, width);
    }

    private void CreateLabel(Transform parent, string text, int fontSize, bool bold, float preferredWidth)
    {
        GameObject go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = bold ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        tmp.color = Color.white;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = preferredWidth;
    }
}
