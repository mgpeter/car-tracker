namespace CarTracker.Shared;

/// <summary>Which of a document's files to send: the bytes as filed, or one of the browser-made renditions.</summary>
public enum DocumentVariant
{
    Original = 1,
    Display = 2,
    Thumbnail = 3,
}
