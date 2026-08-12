using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes one CSV file per sequence run and appends one line per target:
/// 
///   UserID;Condition;TargetNo;ResponseTime;PrecedingPauseLength
///
/// ResponseTime          = seconds between target appearance and the hit that
///                         triggers its destruction.
/// PrecedingPauseLength  = seconds of the random pause between the previous
///                         target's destruction and this target's appearance
///                         (0 for the first target of a sequence).
/// TargetNo              = running count of targets in this session (1, 2, ...).
///
/// UserID and Condition are normally set at runtime by the SetupDialog via
/// SetSession(); the inspector value of UserID is the fallback when no dialog
/// is used. Files are created in Application.persistentDataPath (on the Quest:
/// /sdcard/Android/data/(package name)/files/), one new timestamped file per
/// StartSession() call. Numbers use a decimal point (invariant culture).
/// </summary>
public class ResponseTimeLogger : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("Fallback participant ID; overwritten by the SetupDialog at runtime.")]
    [SerializeField] private string userId = "P00";

    [Header("CSV")]
    [Tooltip("Column separator used in the file.")]
    [SerializeField] private string separator = ";";

    /// <summary>Full path of the file of the current session (null before the first session).</summary>
    public string CurrentFilePath { get; private set; }

    /// <summary>Number of targets logged in the current session.</summary>
    public int TargetCount { get; private set; }

    private string condition = "";

    /// <summary>Sets participant ID and condition for the next session (called by the SetupDialog).</summary>
    public void SetSession(string newUserId, string newCondition)
    {
        userId = newUserId;
        condition = newCondition ?? "";
    }

    /// <summary>Creates a new CSV file with a header line and resets the target counter.</summary>
    public void StartSession()
    {
        TargetCount = 0;

        string conditionPart = string.IsNullOrEmpty(condition) ? "" : "_" + Sanitize(condition);
        string fileName = $"responses_{Sanitize(userId)}{conditionPart}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        CurrentFilePath = Path.Combine(Application.persistentDataPath, fileName);

        WriteLine(string.Join(separator, "UserID", "Condition", "TargetNo", "ResponseTime", "PrecedingPauseLength"));
        Debug.Log($"[ResponseTimeLogger] Logging to {CurrentFilePath}");
    }

    /// <summary>Appends one line for a completed target.</summary>
    public void LogTarget(float responseTimeSeconds, float precedingPauseSeconds)
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            Debug.LogWarning("[ResponseTimeLogger] LogTarget called before StartSession - starting a session now.");
            StartSession();
        }

        TargetCount++;

        string line = string.Join(separator,
            userId,
            condition,
            TargetCount.ToString(CultureInfo.InvariantCulture),
            responseTimeSeconds.ToString("F3", CultureInfo.InvariantCulture),
            precedingPauseSeconds.ToString("F3", CultureInfo.InvariantCulture));

        WriteLine(line);
    }

    private void WriteLine(string line)
    {
        try
        {
            File.AppendAllText(CurrentFilePath, line + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResponseTimeLogger] Could not write to '{CurrentFilePath}': {e.Message}");
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "user";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
