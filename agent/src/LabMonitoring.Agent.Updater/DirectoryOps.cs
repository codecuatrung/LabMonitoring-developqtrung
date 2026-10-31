namespace LabMonitoring.Agent.Updater;

internal static class DirectoryOps
{
    /// <summary>Directory.Move fails across volumes (rare here — both source and dest normally live
    /// under the same drive as the install) — fall back to copy+delete so a single-drive assumption
    /// slipping isn't a hard crash.</summary>
    public static void Move(string source, string dest)
    {
        try
        {
            Directory.Move(source, dest);
        }
        catch (IOException)
        {
            Copy(source, dest);
            Directory.Delete(source, recursive: true);
        }
    }

    public static void Copy(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
