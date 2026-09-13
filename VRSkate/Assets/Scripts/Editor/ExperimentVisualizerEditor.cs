using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Inspector buttons for ExperimentVisualizer + a menu that spins up the Exp_Visualizer scene.
/// </summary>
[CustomEditor(typeof(ExperimentVisualizer))]
public class ExperimentVisualizerInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var viz = (ExperimentVisualizer)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Assign a CSV (TextAsset) or pick a file, then Load & Rebuild. Drag the Playback slider to scrub through the session.\n\n" +
            "Logs are written to:\n" + Path.Combine(Application.persistentDataPath, "ExperimentLogs"),
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Pick CSV File…"))
            {
                string start = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
                if (!Directory.Exists(start)) start = Application.persistentDataPath;
                string picked = EditorUtility.OpenFilePanel("Select experiment CSV", start, "csv");
                if (!string.IsNullOrEmpty(picked))
                {
                    Undo.RecordObject(viz, "Set CSV Path");
                    viz.csvAsset = null;
                    viz.csvAbsolutePath = picked;
                    EditorUtility.SetDirty(viz);
                    viz.Rebuild();
                }
            }

            if (GUILayout.Button("Load & Rebuild"))
                viz.Rebuild();

            if (GUILayout.Button("Clear"))
                viz.ClearGenerated();
        }

        if (GUILayout.Button("Reveal Path Folder"))
            EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "ExperimentLogs"));
    }

    // Draws the colour legend in the Scene view (the component's OnGUI only shows in the Game view).
    private void OnSceneGUI()
    {
        var viz = (ExperimentVisualizer)target;
        var methods = viz.LegendMethods;
        var colors = viz.LegendColors;
        if (methods == null || methods.Count == 0) return;

        Handles.BeginGUI();
        const int pad = 10, sw = 22, rowH = 20;
        int w = 200, h = pad * 2 + rowH * (methods.Count + 1);
        GUI.Box(new Rect(pad, pad, w, h), GUIContent.none);
        GUI.Label(new Rect(pad + 8, pad + 3, w, rowH), "Locomotion", EditorStyles.boldLabel);

        for (int i = 0; i < methods.Count; i++)
        {
            float y = pad + rowH * (i + 1) + 3;
            Color prev = GUI.color;
            GUI.color = colors[i];
            GUI.DrawTexture(new Rect(pad + 8, y + 3, sw, 11), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(pad + 8 + sw + 8, y, w, rowH), methods[i]);
        }
        Handles.EndGUI();
    }
}

public static class ExperimentVisualizerScene
{
    private const string ExperimentScene = "Assets/Scenes/Experiment.unity";
    private const string VisualizerScene = "Assets/Scenes/Exp_Visualizer.unity";

    [MenuItem("Tools/VRSkate/Visualizer/Create or Open Exp_Visualizer Scene", priority = 0)]
    private static void CreateOrOpen()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // First time: copy the Experiment scene so the path draws on the real map.
        if (!File.Exists(VisualizerScene))
        {
            if (File.Exists(ExperimentScene))
            {
                if (!AssetDatabase.CopyAsset(ExperimentScene, VisualizerScene))
                {
                    EditorUtility.DisplayDialog("Exp_Visualizer",
                        "Could not copy Experiment.unity. Creating an empty visualizer scene instead.", "OK");
                    NewEmptyScene();
                    return;
                }
                AssetDatabase.Refresh();
                var scene = EditorSceneManager.OpenScene(VisualizerScene, OpenSceneMode.Single);
                StripRuntimeExperimentObjects();
                AddVisualizer();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Created Exp_Visualizer from a copy of the Experiment scene (experiment/runtime components disabled).");
                return;
            }

            NewEmptyScene();
            return;
        }

        EditorSceneManager.OpenScene(VisualizerScene, OpenSceneMode.Single);
        AddVisualizer();
    }

    [MenuItem("Tools/VRSkate/Visualizer/Add Visualizer To Current Scene", priority = 20)]
    private static void AddVisualizerMenu()
    {
        AddVisualizer();
    }

    private static void NewEmptyScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        AddVisualizer();
        EditorSceneManager.SaveScene(scene, VisualizerScene);
        Debug.Log("Created an empty Exp_Visualizer scene. Add your map/environment for context if you want it.");
    }

    private static void AddVisualizer()
    {
        var existing = Object.FindAnyObjectByType<ExperimentVisualizer>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            return;
        }

        var go = new GameObject("ExperimentVisualizer");
        Undo.RegisterCreatedObjectUndo(go, "Create ExperimentVisualizer");
        go.AddComponent<ExperimentVisualizer>();   // Reset() auto-populates the default colours
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // The copied scene still contains the live experiment rig; disable the driving components so
    // pressing Play in the visualizer scene doesn't start a session or write a new log file.
    private static void StripRuntimeExperimentObjects()
    {
        DisableAll<ExperimentManager>();
        DisableAll<ExperimentLogger>();
        DisableAll<TutorialManager>();
    }

    private static void DisableAll<T>() where T : MonoBehaviour
    {
        foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            c.enabled = false;
    }
}
