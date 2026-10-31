namespace LabMonitoring.Application.FileTransfers;

/// <summary>
/// Validation of a file name that will become a file on a client machine. Deliberately strict: it must be a
/// plain Windows-safe leaf name (no directories, no alternate data streams, no reserved device names).
/// </summary>
public static class FileNameRules
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Returns null when valid, otherwise the reason.</summary>
    public static string? Validate(string? name, IEnumerable<string> blockedExtensions)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "File name is required.";
        if (name.Length > 200)
            return "File name is too long (max 200 characters).";
        if (name != name.Trim() || name.EndsWith('.') || name.EndsWith(' '))
            return "File name may not start/end with spaces or end with a dot.";
        if (name.IndexOfAny(Forbidden) >= 0 || name.Any(char.IsControl))
            return "File name contains characters that are not allowed (\\ / : * ? \" < > |).";
        if (name is "." or "..")
            return "Invalid file name.";

        var stem = Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(stem))
            return $"'{stem}' is a reserved Windows device name.";

        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (blockedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return $"Files of type '{ext}' are not allowed to be pushed.";

        return null;
    }
}
