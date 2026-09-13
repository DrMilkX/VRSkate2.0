using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Samples the player's position + active locomotion method every N frames and writes one
/// row per sample to a CSV time-series. Waypoint arrivals are written as extra "event" rows
/// so the delivery points can be marked in playback.
///
/// The CSV is the source of truth (flushed after every row). When the session ends it is also
/// POSTed to a local server if uploading is enabled, so data can be collected without cabling
/// the headset to a PC.
///
/// Columns: Time,Frame,Event,LocomotionMode,PosX,PosY,PosZ,RotY
///   Time  = seconds since logging started
///   Frame = Unity frame count at the sample
///   Event = empty for normal samples, waypoint name on arrival rows
///   RotY  = player yaw in degrees (handy for orienting a playback marker)
///
/// Re-import with ExperimentVisualizer (scene: Exp_Visualizer) to redraw the path.
/// </summary>
public class ExperimentLogger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TransformWaypoint waypointSystem;
    [SerializeField] private QuickLocoSwitch locomotionSwitcher;
    [SerializeField] private ExperimentManager experimentManager;
    [SerializeField] private Transform playerTransform;

    [Header("Sampling")]
    [Tooltip("Write one position/locomotion sample every this many frames. 1 = every frame.")]
    [SerializeField] private int sampleEveryNFrames = 5;

    [Header("Session")]
    [SerializeField] private string participantId = "P00";
    [SerializeField] private string outputFolderName = "ExperimentLogs";

    [Header("Network Upload")]
    [Tooltip("Enable POSTing the CSV to a local server when the session ends.")]
    [SerializeField] private bool enableUpload = true;

    [Tooltip("URL of the receiver server, e.g. http://192.168.1.50:8000/upload. Must be reachable on the same Wi-Fi network as the headset.")]
    [SerializeField] private string uploadUrl = "http://192.168.1.50:8000/upload";

    [Tooltip("Seconds to wait for the upload to complete before giving up.")]
    [SerializeField] private int uploadTimeoutSeconds = 10;

    private StreamWriter writer;
    private string logFilePath;
    private string effectiveParticipantId;
    private string effectiveUploadUrl;
    private bool isUploading;
    private float startTime;
    private int frameCounter;

    // Invariant culture so decimals are always '.' regardless of the device locale.
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private void Awake()
    {
        if (waypointSystem == null)
            waypointSystem = FindAnyObjectByType<TransformWaypoint>();

        if (locomotionSwitcher == null)
            locomotionSwitcher = FindAnyObjectByType<QuickLocoSwitch>();

        if (experimentManager == null)
            experimentManager = FindAnyObjectByType<ExperimentManager>();

        if (playerTransform == null && waypointSystem != null)
            playerTransform = waypointSystem.playerTransform;
    }

    private void OnEnable()
    {
        if (waypointSystem != null)
            waypointSystem.OnWaypointReached += HandleWaypointReached;
    }

    private void OnDisable()
    {
        if (waypointSystem != null)
            waypointSystem.OnWaypointReached -= HandleWaypointReached;
    }

    private void Start()
    {
        startTime = Time.time;
        OpenLogFile();
        WriteSample("start");   // anchor row at the spawn position
    }

    private void Update()
    {
        if (playerTransform == null || writer == null)
            return;

        if (sampleEveryNFrames < 1)
            sampleEveryNFrames = 1;

        frameCounter++;
        if (frameCounter % sampleEveryNFrames == 0)
            WriteSample(null);
    }

    // A waypoint arrival is written immediately (regardless of the sampling cadence) so the
    // delivery point is captured exactly, tagged with the waypoint name in the Event column.
    private void HandleWaypointReached(int waypointIndex, Transform waypoint)
    {
        WriteSample(waypoint != null ? waypoint.name : $"waypoint_{waypointIndex}");

        // Non-looping waypoint systems have no further "reached" events after the last one,
        // so this is the natural end of the session - upload now instead of waiting for quit.
        bool isFinalWaypoint = waypointSystem != null && !waypointSystem.loopWaypoints
            && waypointIndex == waypointSystem.waypoints.Length - 1;
        if (isFinalWaypoint)
            EndSession();
    }

    private string GetLocomotionMode()
    {
        // The experiment drives the mode while running; otherwise fall back to the free-play menu.
        if (experimentManager != null && experimentManager.isExperimentRunning)
            return experimentManager.GetCurrentLocomotionName();
        if (locomotionSwitcher != null)
            return locomotionSwitcher.CurrentLocomotionName;
        return "Unknown";
    }

    private void OpenLogFile()
    {
        // Prefer the ID/name the PI entered in the exp_setup scene; fall back to the
        // Inspector value (e.g. when testing this scene directly, without a setup session).
        string sessionParticipantId = ExperimentSession.GetParticipantId();
        effectiveParticipantId = string.IsNullOrEmpty(sessionParticipantId) ? participantId : sessionParticipantId;

        string sessionServerUrl = ExperimentSession.GetServerBaseUrl();
        effectiveUploadUrl = string.IsNullOrEmpty(sessionServerUrl) ? uploadUrl : sessionServerUrl.TrimEnd('/') + "/upload";

        string directory = Path.Combine(Application.persistentDataPath, outputFolderName);
        Directory.CreateDirectory(directory);

        string fileName = $"{effectiveParticipantId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        logFilePath = Path.Combine(directory, fileName);

        writer = new StreamWriter(logFilePath, append: false);
        writer.WriteLine("Time,Frame,Event,LocomotionMode,PosX,PosY,PosZ,RotY");
        writer.Flush();

        Debug.Log($"ExperimentLogger: Logging samples (every {sampleEveryNFrames} frame(s)) to {logFilePath}");
    }

    private void WriteSample(string eventName)
    {
        if (writer == null || playerTransform == null)
            return;

        Vector3 p = playerTransform.position;
        float rotY = playerTransform.eulerAngles.y;
        float t = Time.time - startTime;

        string line = string.Join(",",
            t.ToString("F3", Inv),
            Time.frameCount.ToString(Inv),
            CsvField(eventName ?? string.Empty),
            CsvField(GetLocomotionMode()),
            p.x.ToString("F4", Inv),
            p.y.ToString("F4", Inv),
            p.z.ToString("F4", Inv),
            rotY.ToString("F2", Inv));

        writer.WriteLine(line);
        writer.Flush();
    }

    // Wraps a field in quotes if it contains a comma or quote, so names can't break the CSV.
    private static string CsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void OnDestroy() => CloseLogFile();

    // Android may kill the app shortly after backgrounding without ever calling
    // OnApplicationQuit, so pause is treated as a possible premature end too.
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            EndSession();
    }

    private void OnApplicationQuit() => EndSession();

    // Best-effort: attempts to send whatever has been logged so far. Safe to call more
    // than once (e.g. a pause followed by the real quit) - it just re-sends the latest file.
    private void EndSession()
    {
        if (enableUpload)
            StartCoroutine(UploadLogFile());
    }

    private IEnumerator UploadLogFile()
    {
        if (isUploading || string.IsNullOrEmpty(effectiveUploadUrl) || string.IsNullOrEmpty(logFilePath))
            yield break;

        isUploading = true;

        byte[] fileBytes = null;
        try
        {
            // The StreamWriter keeps the file open with FileShare.Read, so reading it
            // concurrently while logging continues is safe.
            fileBytes = File.ReadAllBytes(logFilePath);
        }
        catch (IOException e)
        {
            Debug.LogWarning($"ExperimentLogger: Could not read log file for upload - {e.Message}");
        }

        if (fileBytes != null)
        {
            using (UnityWebRequest request = new UnityWebRequest(effectiveUploadUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(fileBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/csv");
                request.SetRequestHeader("X-Filename", Path.GetFileName(logFilePath));
                request.timeout = uploadTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    Debug.Log($"ExperimentLogger: Uploaded log to {effectiveUploadUrl}");
                else
                    Debug.LogWarning($"ExperimentLogger: Upload failed - {request.error}. Data is still saved locally at {logFilePath}");
            }
        }

        isUploading = false;
    }

    private void CloseLogFile()
    {
        if (writer == null)
            return;

        writer.Flush();
        writer.Close();
        writer = null;
    }
}
