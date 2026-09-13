using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Re-imports an ExperimentLogger CSV (Time,Frame,Event,LocomotionMode,PosX,PosY,PosZ,RotY)
/// and redraws the participant's path as coloured line segments — one colour per locomotion
/// method — with a time scrubber that reveals the path and moves a marker over time.
///
/// Works in the Editor without entering Play mode (ExecuteAlways). Drop this on an object in
/// the Exp_Visualizer scene, assign a CSV (as a TextAsset or an absolute file path), and press
/// "Load & Rebuild" in the inspector, then drag the Playback slider.
///
/// Generated line/marker objects are marked DontSave, so they never get baked into the scene —
/// press Rebuild after a reload.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ExperimentVisualizer : MonoBehaviour
{
    [System.Serializable]
    public struct MethodColor
    {
        public string method;
        public Color color;
    }

    [Header("Data source (use one)")]
    [Tooltip("A CSV imported into the project as a text asset. Rename the .csv to .csv.txt or just drag it here if Unity imported it as a TextAsset.")]
    public TextAsset csvAsset;

    [Tooltip("Absolute path to a CSV on disk (e.g. the file from persistentDataPath/ExperimentLogs). Used when no TextAsset is assigned.")]
    public string csvAbsolutePath = "";

    [Header("Appearance")]
    [Tooltip("Raise the drawn path this far above the recorded Y so it doesn't z-fight the ground.")]
    public float heightOffset = 0.5f;
    public float lineWidth = 0.35f;

    [Tooltip("Colour per locomotion method (case-insensitive). Unlisted methods get an auto-assigned colour.")]
    public List<MethodColor> methodColors = new List<MethodColor>();
    public Color fallbackColor = Color.white;

    [Header("Playback")]
    [Range(0f, 1f)]
    [Tooltip("Scrub through the session: reveals the path up to this point in time and moves the marker.")]
    public float playback = 1f;
    public bool showMarker = true;
    public float markerSize = 1.5f;

    [Tooltip("Auto-advance the scrubber in Play mode.")]
    public bool autoPlay = false;
    [Tooltip("Playback speed multiplier relative to real recorded time.")]
    public float playbackSpeed = 1f;

    [Header("Legend")]
    public bool showLegend = true;

    // ---- parsed data ----
    private struct Sample { public float time; public Vector3 pos; public string method; public string evt; }
    private readonly List<Sample> _samples = new List<Sample>();
    private Vector3[] _worldPos;                 // sample positions with height offset
    private float _duration;

    private struct Run { public int a, z; public Color color; public LineRenderer lr; }
    private readonly List<Run> _runs = new List<Run>();
    private readonly List<string> _legendMethods = new List<string>();
    private readonly List<Color> _legendColors = new List<Color>();

    private Transform _marker;
    private Renderer _markerRenderer;
    private Material _lineMat;
    private float _lastPlayback = -1f;

    private const string GeneratedRoot = "__viz_generated";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private void Reset()
    {
        methodColors = new List<MethodColor>
        {
            new MethodColor { method = "automove",   color = new Color(0.60f, 0.60f, 0.60f) },
            new MethodColor { method = "teleport",   color = new Color(0.20f, 0.80f, 1.00f) },
            new MethodColor { method = "joystick",   color = new Color(0.30f, 0.90f, 0.35f) },
            new MethodColor { method = "walking",    color = new Color(1.00f, 0.85f, 0.15f) },
            new MethodColor { method = "skateboard", color = new Color(1.00f, 0.30f, 0.80f) },
            new MethodColor { method = "freeplay",   color = new Color(0.85f, 0.85f, 0.85f) },
        };
    }

    private void OnEnable()
    {
        if (_samples.Count > 0) { ApplyPlayback(true); return; }

#if UNITY_EDITOR
        // Rebuild after OnEnable finishes — creating/destroying objects during OnEnable is unsafe.
        if (!Application.isPlaying)
            UnityEditor.EditorApplication.delayCall += DeferredRebuild;
#endif
    }

    private void Start()
    {
        if (Application.isPlaying && _samples.Count == 0)
            Rebuild();
    }

#if UNITY_EDITOR
    private void DeferredRebuild()
    {
        if (this == null || !isActiveAndEnabled) return;
        if (_samples.Count == 0) Rebuild();
    }
#endif

    /// <summary>Locomotion methods present in the loaded data, in first-seen order (for a legend).</summary>
    public IReadOnlyList<string> LegendMethods => _legendMethods;
    public IReadOnlyList<Color> LegendColors => _legendColors;

    private void Update()
    {
        if (autoPlay && Application.isPlaying && _duration > 0f)
        {
            playback += (Time.deltaTime * playbackSpeed) / _duration;
            if (playback > 1f) playback = 0f;
        }

        if (!Mathf.Approximately(playback, _lastPlayback))
            ApplyPlayback(false);
    }

    private void OnValidate()
    {
        // Re-apply the scrubber live when values are typed in the inspector (edit mode).
        if (_worldPos != null && _worldPos.Length >= 2)
            ApplyPlayback(false);
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    [ContextMenu("Load & Rebuild")]
    public void Rebuild()
    {
        ClearGenerated();
        _samples.Clear();
        _runs.Clear();
        _legendMethods.Clear();
        _legendColors.Clear();

        string text = LoadText();
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("ExperimentVisualizer: No CSV data. Assign a TextAsset or a valid csvAbsolutePath.", this);
            return;
        }

        ParseCsv(text);
        if (_samples.Count < 2)
        {
            Debug.LogWarning($"ExperimentVisualizer: Parsed only {_samples.Count} sample(s) — nothing to draw.", this);
            return;
        }

        _duration = _samples[_samples.Count - 1].time - _samples[0].time;

        _worldPos = new Vector3[_samples.Count];
        for (int i = 0; i < _samples.Count; i++)
            _worldPos[i] = _samples[i].pos + Vector3.up * heightOffset;

        BuildRuns();
        BuildMarker();
        _lastPlayback = -1f;    // force a playback apply
        ApplyPlayback(true);

        Debug.Log($"ExperimentVisualizer: Loaded {_samples.Count} samples, {_runs.Count} coloured segment(s), {_duration:F1}s.", this);
    }

    [ContextMenu("Clear")]
    public void ClearGenerated()
    {
        _runs.Clear();
        _marker = null;
        _markerRenderer = null;

        Transform root = transform.Find(GeneratedRoot);
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }
    }

    private Transform GetGeneratedRoot()
    {
        Transform root = transform.Find(GeneratedRoot);
        if (root == null)
        {
            var go = new GameObject(GeneratedRoot);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            root = go.transform;
        }
        return root;
    }

    private void BuildRuns()
    {
        EnsureLineMaterial();
        Transform root = GetGeneratedRoot();

        // A run is a maximal stretch of samples sharing one locomotion method. Runs overlap by
        // one sample (run k ends on the sample that starts run k+1) so the coloured lines connect.
        int n = _samples.Count;
        int runStart = 0;
        for (int i = 1; i <= n; i++)
        {
            bool boundary = (i == n) || _samples[i].method != _samples[runStart].method;
            if (!boundary) continue;

            int a = runStart;
            int z = (i == n) ? n - 1 : i;   // include the changeover sample as the run's last point
            AddRun(root, a, z, _samples[runStart].method);
            RegisterLegend(_samples[runStart].method);
            runStart = i;
        }
    }

    private void AddRun(Transform root, int a, int z, string method)
    {
        var go = new GameObject($"seg_{a}_{z}_{method}");
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = _lineMat;
        lr.widthMultiplier = lineWidth;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Color c = ColorFor(method);
        lr.startColor = c;
        lr.endColor = c;

        _runs.Add(new Run { a = a, z = z, color = c, lr = lr });
    }

    private void BuildMarker()
    {
        if (!showMarker) return;

        Transform root = GetGeneratedRoot();
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PlaybackMarker";
        sphere.hideFlags = HideFlags.DontSave;
        var col = sphere.GetComponent<Collider>();
        if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
        sphere.transform.SetParent(root, false);
        sphere.transform.localScale = Vector3.one * markerSize;

        _marker = sphere.transform;
        _markerRenderer = sphere.GetComponent<Renderer>();
        if (_markerRenderer != null)
        {
            _markerRenderer.sharedMaterial = new Material(PickShader()) { hideFlags = HideFlags.DontSave };
        }
    }

    // -------------------------------------------------------------------------
    // Playback / reveal
    // -------------------------------------------------------------------------

    private void ApplyPlayback(bool force)
    {
        _lastPlayback = playback;
        if (_worldPos == null || _worldPos.Length < 2) return;

        int n = _worldPos.Length;
        float c = Mathf.Clamp01(playback) * (n - 1);
        int ci = Mathf.Clamp(Mathf.FloorToInt(c), 0, n - 1);
        float frac = c - ci;

        // Reveal each run up to the cutoff.
        var buffer = new List<Vector3>(64);
        foreach (var run in _runs)
        {
            if (run.lr == null) continue;
            buffer.Clear();

            for (int i = run.a; i <= run.z && i <= ci; i++)
                buffer.Add(_worldPos[i]);

            // Partial edge inside this run.
            if (ci >= run.a && ci < run.z && frac > 0f)
                buffer.Add(Vector3.Lerp(_worldPos[ci], _worldPos[ci + 1], frac));

            run.lr.positionCount = buffer.Count;
            if (buffer.Count > 0)
                run.lr.SetPositions(buffer.ToArray());
        }

        // Marker.
        if (_marker != null)
        {
            Vector3 mpos = (ci < n - 1) ? Vector3.Lerp(_worldPos[ci], _worldPos[ci + 1], frac) : _worldPos[n - 1];
            _marker.position = mpos;
            if (_markerRenderer != null)
                SetMatColor(_markerRenderer.sharedMaterial, ColorFor(_samples[ci].method));
            _marker.gameObject.SetActive(showMarker);
        }
    }

    // -------------------------------------------------------------------------
    // CSV parsing
    // -------------------------------------------------------------------------

    private string LoadText()
    {
        if (csvAsset != null)
            return csvAsset.text;

        if (!string.IsNullOrEmpty(csvAbsolutePath) && File.Exists(csvAbsolutePath))
        {
            try { return File.ReadAllText(csvAbsolutePath); }
            catch (System.Exception e) { Debug.LogWarning($"ExperimentVisualizer: Could not read '{csvAbsolutePath}' — {e.Message}", this); }
        }
        return null;
    }

    private void ParseCsv(string text)
    {
        string[] lines = text.Split('\n');
        if (lines.Length < 2) return;

        // Map header columns by name so column order can change safely.
        var header = SplitCsvLine(lines[0]);
        int iTime = IndexOf(header, "Time");
        int iEvent = IndexOf(header, "Event");
        int iMode = IndexOf(header, "LocomotionMode");
        int iX = IndexOf(header, "PosX");
        int iY = IndexOf(header, "PosY");
        int iZ = IndexOf(header, "PosZ");

        if (iX < 0 || iY < 0 || iZ < 0)
        {
            Debug.LogWarning("ExperimentVisualizer: CSV is missing PosX/PosY/PosZ columns.", this);
            return;
        }

        for (int li = 1; li < lines.Length; li++)
        {
            string raw = lines[li].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var f = SplitCsvLine(raw);
            if (f.Count <= iZ) continue;

            if (!TryFloat(f, iX, out float x) || !TryFloat(f, iY, out float y) || !TryFloat(f, iZ, out float z))
                continue;

            var s = new Sample
            {
                pos = new Vector3(x, y, z),
                time = TryFloat(f, iTime, out float t) ? t : li,
                method = Get(f, iMode, "Unknown"),
                evt = Get(f, iEvent, "")
            };
            _samples.Add(s);
        }
    }

    private static bool TryFloat(List<string> f, int i, out float v)
    {
        v = 0f;
        return i >= 0 && i < f.Count && float.TryParse(f[i], NumberStyles.Float, Inv, out v);
    }

    private static string Get(List<string> f, int i, string fallback)
        => (i >= 0 && i < f.Count && !string.IsNullOrEmpty(f[i])) ? f[i] : fallback;

    private static int IndexOf(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Trim().Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // Minimal CSV splitter honouring double-quoted fields (matches ExperimentLogger's CsvField).
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    // -------------------------------------------------------------------------
    // Colours / materials
    // -------------------------------------------------------------------------

    private static readonly Color[] AutoPalette =
    {
        new Color(0.95f,0.45f,0.20f), new Color(0.55f,0.45f,0.95f), new Color(0.20f,0.75f,0.65f),
        new Color(0.85f,0.75f,0.30f), new Color(0.90f,0.35f,0.45f), new Color(0.45f,0.65f,0.95f),
    };
    private readonly Dictionary<string, Color> _autoAssigned = new Dictionary<string, Color>();

    private Color ColorFor(string method)
    {
        string key = (method ?? "").Trim().ToLowerInvariant();
        foreach (var mc in methodColors)
            if (!string.IsNullOrEmpty(mc.method) && mc.method.Trim().ToLowerInvariant() == key)
                return mc.color;

        if (_autoAssigned.TryGetValue(key, out Color c)) return c;
        c = string.IsNullOrEmpty(key) ? fallbackColor : AutoPalette[_autoAssigned.Count % AutoPalette.Length];
        _autoAssigned[key] = c;
        return c;
    }

    private void RegisterLegend(string method)
    {
        if (!_legendMethods.Contains(method))
        {
            _legendMethods.Add(method);
            _legendColors.Add(ColorFor(method));
        }
    }

    private void EnsureLineMaterial()
    {
        if (_lineMat != null) return;
        _lineMat = new Material(Shader.Find("Sprites/Default") ?? PickShader()) { hideFlags = HideFlags.DontSave };
    }

    private static Shader PickShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
    }

    private static void SetMatColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.color = c;
    }

    // -------------------------------------------------------------------------
    // Legend overlay
    // -------------------------------------------------------------------------

    private void OnGUI()
    {
        if (!showLegend || _legendMethods.Count == 0) return;

        const int pad = 10, sw = 22, rowH = 22;
        int w = 210, h = pad * 2 + rowH * (_legendMethods.Count + 1);
        GUI.Box(new Rect(pad, pad, w, h), GUIContent.none);

        var title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(pad + 8, pad + 4, w, rowH), "Locomotion", title);

        for (int i = 0; i < _legendMethods.Count; i++)
        {
            float y = pad + rowH * (i + 1) + 4;
            Color prev = GUI.color;
            GUI.color = _legendColors[i];
            GUI.DrawTexture(new Rect(pad + 8, y + 3, sw, 12), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(pad + 8 + sw + 8, y, w, rowH), _legendMethods[i]);
        }
    }
}
