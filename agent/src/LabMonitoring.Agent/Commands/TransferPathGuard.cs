using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Decides which client folders a file-transfer command may touch. The Agent runs as LocalSystem, so this is the
/// last line of defense: a folder is usable only if the server-synced config (<c>TransferFolders</c>) lists it for the
/// command's direction under the same id AND the same path as the signed payload, the path is a plain local
/// drive path (no UNC/device paths), and no directory on the way to it is a reparse point (junction/symlink) that could
/// redirect writes outside the allowed folder.
/// </summary>
public static class TransferPathGuard
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Resolves and validates the folder for a command. <paramref name="direction"/> is "Push" or "Collect".</summary>
    public static bool TryResolveFolder(
        int folderId, string? folderPath, string direction, AgentConfigResponse? config, out string fullPath, out string error)
    {
        fullPath = string.Empty;

        var allowed = config?.TransferFolders.FirstOrDefault(f =>
            f.Id == folderId && string.Equals(f.Direction, direction, StringComparison.OrdinalIgnoreCase));
        if (allowed is null)
        {
            error = "This folder is not in the locally synced list of allowed folders (config may be stale; try again shortly).";
            return false;
        }

        if (!string.Equals(Normalize(allowed.Path), Normalize(folderPath), StringComparison.OrdinalIgnoreCase))
        {
            error = "The folder path in the command does not match the allowed folder.";
            return false;
        }

        if (!TryExpand(allowed.Path, out fullPath, out error))
            return false;

        return NoReparsePointOnPath(fullPath, out error);
    }

    /// <summary>Expands environment variables and requires a plain local absolute path.</summary>
    public static bool TryExpand(string path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(path ?? string.Empty);
        if (expanded.Contains('%'))
        {
            error = "The folder path uses an environment variable that is not defined on this machine.";
            return false;
        }
        if (expanded.StartsWith(@"\\", StringComparison.Ordinal) || expanded.StartsWith("//", StringComparison.Ordinal))
        {
            error = "UNC and device paths are not allowed.";
            return false;
        }
        if (expanded.Split('\\', '/').Contains(".."))
        {
            error = "Paths containing '..' are not allowed.";
            return false;
        }

        // GetFullPath would silently resolve a relative path against the service's current directory.
        if (!Path.IsPathFullyQualified(expanded))
        {
            error = "Only absolute local drive paths are allowed.";
            return false;
        }

        string full;
        try { full = Path.GetFullPath(expanded); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid folder path: {ex.Message}";
            return false;
        }

        if (!Path.IsPathRooted(full) || (OperatingSystem.IsWindows() && !(full.Length >= 3 && char.IsLetter(full[0]) && full[1] == ':')))
        {
            error = "Only absolute local drive paths are allowed.";
            return false;
        }

        fullPath = full.TrimEnd('\\', '/');
        if (fullPath.Length == 2 && fullPath[1] == ':')
            fullPath += '\\'; // a bare drive root keeps its trailing separator
        return true;
    }

    /// <summary>A plain leaf file name that is safe to create inside the allowed folder.</summary>
    public static bool IsSafeFileName(string? name, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            error = "Invalid file name.";
            return false;
        }
        if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(':') || name.EndsWith('.') || name.EndsWith(' ') || name is "." or "..")
        {
            error = "The file name contains characters that are not allowed.";
            return false;
        }
        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(name)))
        {
            error = "The file name is a reserved Windows device name.";
            return false;
        }
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> equals or lies inside <paramref name="root"/> (no traversal).</summary>
    public static bool IsInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(candidate);
        return (c + Path.DirectorySeparatorChar).StartsWith(r, comparison);
    }

    /// <summary>Rejects the path if any existing directory from the drive root down to it is a reparse point.</summary>
    private static bool NoReparsePointOnPath(string fullPath, out string error)
    {
        error = string.Empty;
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        foreach (var part in fullPath[root.Length..].Split('\\', '/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                var info = new DirectoryInfo(current);
                if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    error = $"'{current}' is a junction/symbolic link, which is not allowed in a transfer path.";
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Cannot inspect '{current}': {ex.Message}";
                return false;
            }
        }
        return true;
    }

    private static string Normalize(string? path) => (path ?? string.Empty).Trim().TrimEnd('\\', '/');
}
