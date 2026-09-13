using UnityEngine;

/// <summary>
/// Small PlayerPrefs-backed bridge that carries PI-entered setup data (experiment ID,
/// experimenter name, server address) from the exp_setup scene into whichever scene
/// ExperimentLogger runs in. Values persist across scene loads and app restarts, so the
/// next-suggested ID keeps incrementing between sessions.
/// </summary>
public static class ExperimentSession
{
    private const string LastIdKey = "ExperimentSession_LastId";
    private const string CurrentIdKey = "ExperimentSession_CurrentId";
    private const string ExperimenterNameKey = "ExperimentSession_ExperimenterName";
    private const string ServerBaseUrlKey = "ExperimentSession_ServerBaseUrl";

    /// <summary>The next experiment ID to suggest in the setup UI (last used + 1, or 1 if none yet).</summary>
    public static int GetSuggestedNextId()
    {
        return PlayerPrefs.GetInt(LastIdKey, 0) + 1;
    }

    public static string GetLastExperimenterName()
    {
        return PlayerPrefs.GetString(ExperimenterNameKey, "");
    }

    public static string GetServerBaseUrl(string fallback = "")
    {
        return PlayerPrefs.GetString(ServerBaseUrlKey, fallback);
    }

    /// <summary>True once the PI has confirmed a setup via BeginSession.</summary>
    public static bool HasSession()
    {
        return PlayerPrefs.HasKey(CurrentIdKey);
    }

    /// <summary>Call from the setup scene once the PI confirms the values for this participant run.</summary>
    public static void BeginSession(int experimentId, string experimenterName, string serverBaseUrl)
    {
        PlayerPrefs.SetInt(CurrentIdKey, experimentId);
        PlayerPrefs.SetInt(LastIdKey, experimentId);
        PlayerPrefs.SetString(ExperimenterNameKey, experimenterName ?? "");
        PlayerPrefs.SetString(ServerBaseUrlKey, serverBaseUrl ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The participant/session identifier ExperimentLogger should log under, e.g. "E007_Alex".
    /// Empty if no session has been set up yet (ExperimentLogger falls back to its own Inspector value).
    /// </summary>
    public static string GetParticipantId()
    {
        if (!HasSession())
            return "";

        int id = PlayerPrefs.GetInt(CurrentIdKey);
        string name = PlayerPrefs.GetString(ExperimenterNameKey, "");
        string safeName = SanitizeForFileName(name);

        return string.IsNullOrEmpty(safeName) ? $"E{id:D3}" : $"E{id:D3}_{safeName}";
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
                chars[i] = '_';
        }

        return new string(chars);
    }
}
