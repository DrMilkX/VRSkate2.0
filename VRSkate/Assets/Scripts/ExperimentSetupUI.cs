using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// PI-facing setup screen for the exp_setup scene. Lets the PI enter/confirm an
/// incrementing experiment ID, an experimenter/participant name, and the logging
/// server's address, then test that the server is reachable before handing off to
/// the participant-facing flow. Values are stored via ExperimentSession, which
/// ExperimentLogger reads from automatically in whichever scene it runs in.
/// </summary>
public class ExperimentSetupUI : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Headset/camera transform used to position the menu. Defaults to Camera.main.")]
    [SerializeField] private Transform headset;
    [SerializeField] private float menuDistance = 1.2f;
    [SerializeField] private float menuHeightOffset = 0.1f;

    [Header("Server")]
    [Tooltip("Default server base URL suggested on first run, e.g. http://192.168.1.50:8000")]
    [SerializeField] private string defaultServerBaseUrl = "http://192.168.1.50:8000";
    [SerializeField] private int connectionTestTimeoutSeconds = 5;

    [Header("Next Scene")]
    [Tooltip("Scene to load once the PI confirms setup - typically the participant-facing tutorial/experiment entry point.")]
    [SerializeField] private string nextSceneName = "tutorial";

    private GameObject menuRoot;
    private TMP_InputField nameField;
    private TMP_InputField idField;
    private TMP_InputField serverField;
    private TextMeshProUGUI statusLabel;
    private SimpleTextKeyboard keyboard;

    private int currentId;

    private void Start()
    {
        if (headset == null && Camera.main != null)
            headset = Camera.main.transform;

        currentId = ExperimentSession.GetSuggestedNextId();

        BuildUI();
        PositionInFrontOfPlayer();
    }

    // -------------------------------------------------------------------------
    // Server test / confirm / start
    // -------------------------------------------------------------------------

    private void TestServerConnection()
    {
        StartCoroutine(PingServer());
    }

    private IEnumerator PingServer()
    {
        string baseUrl = serverField.text.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            SetStatus("Enter a server address first.", new Color(0.9f, 0.3f, 0.3f));
            yield break;
        }

        SetStatus("Checking...", Color.yellow);

        using (UnityWebRequest request = UnityWebRequest.Get(baseUrl + "/health"))
        {
            request.timeout = connectionTestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                SetStatus("Server OK - logging is reachable.", new Color(0.3f, 0.85f, 0.3f));
            else
                SetStatus($"Server unreachable: {request.error}", new Color(0.9f, 0.3f, 0.3f));
        }
    }

    private void ConfirmAndStart()
    {
        string name = nameField.text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("Enter an experimenter/participant name first.", new Color(0.9f, 0.3f, 0.3f));
            return;
        }

        if (!int.TryParse(idField.text, out int id) || id < 1)
        {
            SetStatus("Experiment ID must be a positive number.", new Color(0.9f, 0.3f, 0.3f));
            return;
        }

        ExperimentSession.BeginSession(id, name, serverField.text.Trim());

        Debug.Log($"ExperimentSetupUI: Session confirmed - ID {id:D3}, name '{name}'. Loading '{nextSceneName}'.");
        SceneManager.LoadScene(nextSceneName);
    }

    private void SetStatus(string message, Color color)
    {
        if (statusLabel == null) return;
        statusLabel.text = message;
        statusLabel.color = color;
    }

    private void AdjustId(int delta)
    {
        currentId = Mathf.Max(1, currentId + delta);
        idField.text = currentId.ToString();
    }

    private void OnIdFieldEdited(string value)
    {
        if (int.TryParse(value, out int parsed) && parsed >= 1)
            currentId = parsed;
        else
            idField.text = currentId.ToString();
    }

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    private void PositionInFrontOfPlayer()
    {
        if (headset == null) return;

        Vector3 forward = headset.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        // menuRoot.transform.position = new Vector3(
        //     headset.position.x + forward.x * menuDistance,
        //     headset.position.y + menuHeightOffset,
        //     headset.position.z + forward.z * menuDistance);

        // place at parent object location
        menuRoot.transform.position = transform.position;

        // menuRoot.transform.LookAt(headset.position, Vector3.up);
        // menuRoot.transform.Rotate(0f, 180f, 0f);
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        menuRoot = new GameObject("ExperimentSetupMenu");
        menuRoot.transform.SetParent(transform, false);

        Canvas canvas = menuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        CanvasScaler scaler = menuRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 200f;

        menuRoot.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform rt = menuRoot.GetComponent<RectTransform>();
        // Tall enough to fit the on-screen keyboard when it pops open below a field;
        // WorldSpace canvases don't clip content, so this is just for tidy default framing.
        rt.sizeDelta = new Vector2(460f, 980f);
        rt.localScale = Vector3.one * 0.001f;

        GameObject panel = MakePanel(menuRoot.transform);

        CreateLabel(panel.transform, "Experiment Setup", 28, true);
        CreateLabel(panel.transform, "For the PI to fill out before handing off to the participant.", 15, false);

        CreateDivider(panel.transform);

        CreateLabel(panel.transform, "Experimenter / Participant Name", 18, false);
        nameField = CreateInputField(panel.transform, "e.g. Alex", TMP_InputField.ContentType.Standard,
            ExperimentSession.GetLastExperimenterName());

        CreateLabel(panel.transform, "Experiment ID", 18, false);
        CreateIdRow(panel.transform);

        CreateDivider(panel.transform);

        CreateLabel(panel.transform, "Logging Server Address", 18, false);
        serverField = CreateInputField(panel.transform, "http://192.168.1.50:8000", TMP_InputField.ContentType.Standard,
            ExperimentSession.GetServerBaseUrl(defaultServerBaseUrl));

        CreateButton(panel.transform, "Test Server Connection", TestServerConnection);
        statusLabel = CreateLabel(panel.transform, "Not tested yet.", 16, false);

        CreateDivider(panel.transform);

        CreateButton(panel.transform, "Confirm & Start", ConfirmAndStart);

        // No system keyboard pops up in VR, so provide a simple on-screen one.
        // It appears inline (pushing the layout below it down) whenever a registered
        // field is selected via the VR ray, and hides again once "DONE" is pressed.
        keyboard = gameObject.AddComponent<SimpleTextKeyboard>();
        keyboard.Initialize(panel.transform);
        keyboard.RegisterField(nameField, "Experimenter / Participant Name");
        keyboard.RegisterField(serverField, "Logging Server Address");
    }

    private GameObject MakePanel(Transform parent)
    {
        GameObject go = new GameObject("Panel");
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
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;

        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return go;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize, bool bold)
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

        return tmp;
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
        cb.pressedColor = new Color(0.2f, 0.6f, 1f, 1f);
        btn.colors = cb;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick());

        go.AddComponent<LayoutElement>().preferredHeight = 52f;

        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(go.transform, false);
        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 20;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        RectTransform lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = lr.offsetMax = Vector2.zero;

        return btn;
    }

    /// <summary>
    /// Builds a minimal TMP_InputField (background + viewport + text + placeholder),
    /// mirroring the hierarchy Unity's own TMP_InputField prefab uses. Selecting it via
    /// a VR ray brings up the headset's system keyboard automatically (Quest handles this
    /// natively for any focused TMP_InputField/InputField - no extra keyboard asset needed).
    /// </summary>
    private TMP_InputField CreateInputField(Transform parent, string placeholderText,
        TMP_InputField.ContentType contentType, string initialValue)
    {
        GameObject container = new GameObject("Input_" + placeholderText);
        container.transform.SetParent(parent, false);

        Image bg = container.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.15f, 1f);
        container.AddComponent<LayoutElement>().preferredHeight = 56f;

        GameObject viewport = new GameObject("TextArea");
        viewport.transform.SetParent(container.transform, false);
        RectTransform viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(14f, 6f);
        viewportRt.offsetMax = new Vector2(-14f, -6f);
        viewport.AddComponent<RectMask2D>();

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(viewport.transform, false);
        TextMeshProUGUI text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = textRt.offsetMax = Vector2.zero;

        GameObject placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(viewport.transform, false);
        TextMeshProUGUI placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.text = placeholderText;
        placeholder.fontSize = 20;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform placeholderRt = placeholderGO.GetComponent<RectTransform>();
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = placeholderRt.offsetMax = Vector2.zero;

        TMP_InputField field = container.AddComponent<TMP_InputField>();
        field.textViewport = viewportRt;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.contentType = contentType;
        field.text = initialValue ?? "";

        return field;
    }

    private void CreateIdRow(Transform parent)
    {
        GameObject row = new GameObject("Row_ExperimentId");
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        row.AddComponent<LayoutElement>().preferredHeight = 56f;

        Button minus = CreateButton(row.transform, "-", () => AdjustId(-1));
        minus.GetComponent<LayoutElement>().preferredWidth = 56f;

        idField = CreateInputField(row.transform, "ID", TMP_InputField.ContentType.IntegerNumber, currentId.ToString());
        idField.GetComponent<LayoutElement>().flexibleWidth = 1f;
        idField.onEndEdit.AddListener(OnIdFieldEdited);

        Button plus = CreateButton(row.transform, "+", () => AdjustId(1));
        plus.GetComponent<LayoutElement>().preferredWidth = 56f;
    }
}
