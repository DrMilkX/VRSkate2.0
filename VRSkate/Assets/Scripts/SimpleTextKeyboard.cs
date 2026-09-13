using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal on-screen keyboard for typing into TMP_InputFields in VR, where no OS keyboard
/// pops up. Pokemon-name-entry style: a grid of uppercase letters and digits plus
/// dash/underscore/space, driven entirely by clicking through the same VR ray interactor
/// used for the rest of the UI - no text-entry hardware or OS support required.
///
/// Usage: AddComponent, call Initialize(parentTransform) once, then RegisterField(field, label)
/// for each TMP_InputField that should use it. Selecting a registered field (via VR ray click)
/// shows the keyboard and routes key presses into that field.
/// </summary>
public class SimpleTextKeyboard : MonoBehaviour
{
    // Gameboy-style: uppercase letters + digits + dash/underscore in the grid, space/backspace/done below.
    // Also includes '.', ':', '/' so this can type things like a server address (e.g. http://192.168.1.50:8000).
    private const string GridCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_./:";
    private const int Columns = 9;
    private const float KeySize = 42f;
    private const float KeySpacing = 4f;

    private GameObject panelRoot;
    private TextMeshProUGUI targetLabel;
    private TMP_InputField activeField;

    public void Initialize(Transform parent)
    {
        BuildUI(parent);
        panelRoot.SetActive(false);
    }

    /// <summary>Wires a field so selecting it (VR ray click) opens the keyboard targeting it.</summary>
    public void RegisterField(TMP_InputField field, string displayName)
    {
        field.onSelect.AddListener(_ => Show(field, displayName));
    }

    private void Show(TMP_InputField field, string displayName)
    {
        activeField = field;
        if (targetLabel != null)
            targetLabel.text = $"Typing: {displayName}";
        panelRoot.SetActive(true);
    }

    private void InsertChar(char c)
    {
        if (activeField == null) return;
        activeField.text += c;
    }

    private void Backspace()
    {
        if (activeField == null || activeField.text.Length == 0) return;
        activeField.text = activeField.text.Substring(0, activeField.text.Length - 1);
    }

    private void Done()
    {
        activeField = null;
        panelRoot.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUI(Transform parent)
    {
        panelRoot = new GameObject("SimpleTextKeyboard");
        panelRoot.transform.SetParent(parent, false);

        panelRoot.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 0.97f);

        VerticalLayoutGroup outer = panelRoot.AddComponent<VerticalLayoutGroup>();
        outer.padding = new RectOffset(10, 10, 10, 10);
        outer.spacing = 6f;
        outer.childForceExpandWidth = true;
        outer.childForceExpandHeight = false;
        outer.childControlWidth = true;
        outer.childControlHeight = true;

        panelRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        targetLabel = CreateLabel(panelRoot.transform, "Typing:", 16);

        GameObject grid = new GameObject("Grid");
        grid.transform.SetParent(panelRoot.transform, false);
        GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(KeySize, KeySize);
        gridLayout.spacing = new Vector2(KeySpacing, KeySpacing);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = Columns;

        int rows = Mathf.CeilToInt(GridCharacters.Length / (float)Columns);
        grid.AddComponent<LayoutElement>().preferredHeight = rows * KeySize + (rows - 1) * KeySpacing;

        foreach (char c in GridCharacters)
        {
            char captured = c; // capture per-iteration value for the closure
            CreateKeyButton(grid.transform, c.ToString(), 20, () => InsertChar(captured));
        }

        GameObject bottomRow = new GameObject("BottomRow");
        bottomRow.transform.SetParent(panelRoot.transform, false);
        HorizontalLayoutGroup hl = bottomRow.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = KeySpacing;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        bottomRow.AddComponent<LayoutElement>().preferredHeight = KeySize;

        Button space = CreateKeyButton(bottomRow.transform, "SPACE", 14, () => InsertChar(' '));
        space.GetComponent<LayoutElement>().flexibleWidth = 1f;

        Button del = CreateKeyButton(bottomRow.transform, "DEL", 14, Backspace);
        del.GetComponent<LayoutElement>().preferredWidth = 70f;

        Button done = CreateKeyButton(bottomRow.transform, "DONE", 14, Done);
        done.GetComponent<LayoutElement>().preferredWidth = 70f;
    }

    private Button CreateKeyButton(Transform parent, string label, int fontSize, Action onClick)
    {
        GameObject go = new GameObject("Key_" + label);
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

        go.AddComponent<LayoutElement>().preferredHeight = KeySize;

        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(go.transform, false);
        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        RectTransform lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = lr.offsetMax = Vector2.zero;

        return btn;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize)
    {
        GameObject go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.8f, 0.8f, 0.8f, 1f);

        go.AddComponent<LayoutElement>().preferredHeight = fontSize + 10f;

        return tmp;
    }
}
