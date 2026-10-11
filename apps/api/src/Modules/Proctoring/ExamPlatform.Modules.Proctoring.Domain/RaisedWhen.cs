namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>Which side of its threshold a signal must fall on to count as raised.</summary>
public enum RaisedWhen
{
    /// <summary>Raised when the value is at or above the threshold, as for a count of departures.</summary>
    AtLeast,

    /// <summary>Raised when the value is at or below the threshold, as for a pace that is suspiciously fast.</summary>
    AtMost,
}
