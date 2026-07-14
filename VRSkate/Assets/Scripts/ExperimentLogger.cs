using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Logs one row per waypoint-to-waypoint segment: time taken, locomotion mode used,
/// and how directly the player traveled (straight-line vs. actual distance covered).
/// Subscribes to TransformWaypoint.OnWaypointReached, so it does not need any changes
/// to how the waypoint system itself works.
///
/// The CSV is written to disk in real time (flushed after every row) as the source of
/// truth. On top of that, the whole file is opportunistically POSTed to a local server
/// on the same network whenever the session ends (normally or prematurely) so it can be
/// collected without cabling the headset to a PC.
/// </summary>
public class ExperimentLogger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TransformWaypoint waypointSystem;
    [SerializeField] private LocomotionSwitcher locomotionSwitcher;
    [SerializeField] private Transform playerTransform;

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
    private bool isUploading;
    private int segmentIndex;
    private float segmentStartTime;
    private DateTime segmentStartTimestamp;
    private Vector3 segmentStartPosition;
    private Vector3 lastSampledPosition;
    private float traveledDistance;

    private void Awake()
    {
        if (waypointSystem == null)
            waypointSystem = FindAnyObjectByType<TransformWaypoint>();

        if (locomotionSwitcher == null)
            locomotionSwitcher = FindAnyObjectByType<LocomotionSwitcher>();

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
        OpenLogFile();
        BeginSegment();
    }

    private void Update()
    {
        if (playerTransform == null)
            return;

        traveledDistance += Vector3.Distance(playerTransform.position, lastSampledPosition);
        lastSampledPosition = playerTransform.position;
    }

    private void BeginSegment()
    {
        segmentStartTime = Time.time;
        segmentStartTimestamp = DateTime.Now;
        traveledDistance = 0f;

        if (playerTransform != null)
        {
            segmentStartPosition = playerTransform.position;
            lastSampledPosition = playerTransform.position;
        }
    }

    private void HandleWaypointReached(int waypointIndex, Transform waypoint)
    {
        float segmentTime = Time.time - segmentStartTime;
        DateTime segmentEndTimestamp = DateTime.Now;
        float straightLineDistance = playerTransform != null
            ? Vector3.Distance(segmentStartPosition, waypoint.position)
            : 0f;
        float pathEfficiency = traveledDistance > 0.0001f ? straightLineDistance / traveledDistance : 0f;
        string locomotionMode = locomotionSwitcher != null ? locomotionSwitcher.CurrentLocomotionName : "Unknown";

        WriteRow(waypointIndex, waypoint.name, segmentTime, segmentStartTimestamp, segmentEndTimestamp,
            locomotionMode, straightLineDistance, traveledDistance, pathEfficiency,
            segmentStartPosition, waypoint.position);

        segmentIndex++;
        BeginSegment();

        // Non-looping waypoint systems have no further "reached" events after the last one,
        // so this is the natural end of the session - upload now instead of waiting for quit.
        bool isFinalWaypoint = waypointSystem != null && !waypointSystem.loopWaypoints
            && waypointIndex == waypointSystem.waypoints.Length - 1;
        if (isFinalWaypoint)
            EndSession();
    }

    private void OpenLogFile()
    {
        string directory = Path.Combine(Application.persistentDataPath, outputFolderName);
        Directory.CreateDirectory(directory);

        string fileName = $"{participantId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        logFilePath = Path.Combine(directory, fileName);

        writer = new StreamWriter(logFilePath, append: false);
        writer.WriteLine("ParticipantId,SegmentIndex,WaypointIndex,WaypointName,SegmentStartTimestamp,SegmentEndTimestamp,SegmentTimeSeconds,LocomotionMode," +
                          "StraightLineDistance,TraveledDistance,PathEfficiency,StartX,StartY,StartZ,TargetX,TargetY,TargetZ");
        writer.Flush();

        Debug.Log($"ExperimentLogger: Logging to {logFilePath}");
    }

    private void WriteRow(int waypointIndex, string waypointName, float segmentTime,
        DateTime startTimestamp, DateTime endTimestamp, string locomotionMode,
        float straightLineDistance, float distanceTraveled, float pathEfficiency,
        Vector3 startPosition, Vector3 targetPosition)
    {
        if (writer == null)
            return;

        string line = string.Join(",",
            CsvField(participantId),
            segmentIndex,
            waypointIndex,
            CsvField(waypointName),
            startTimestamp.ToString("o"),
            endTimestamp.ToString("o"),
            segmentTime.ToString("F3"),
            CsvField(locomotionMode),
            straightLineDistance.ToString("F3"),
            distanceTraveled.ToString("F3"),
            pathEfficiency.ToString("F3"),
            startPosition.x.ToString("F3"),
            startPosition.y.ToString("F3"),
            startPosition.z.ToString("F3"),
            targetPosition.x.ToString("F3"),
            targetPosition.y.ToString("F3"),
            targetPosition.z.ToString("F3"));

        writer.WriteLine(line);
        writer.Flush();
    }

    // Wraps a field in quotes if it contains a comma or quote, so waypoint/object names can't break the CSV.
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
        if (isUploading || string.IsNullOrEmpty(uploadUrl) || string.IsNullOrEmpty(logFilePath))
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
            using (UnityWebRequest request = new UnityWebRequest(uploadUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(fileBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/csv");
                request.SetRequestHeader("X-Filename", Path.GetFileName(logFilePath));
                request.timeout = uploadTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    Debug.Log($"ExperimentLogger: Uploaded log to {uploadUrl}");
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
