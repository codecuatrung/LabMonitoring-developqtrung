namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>Limits and rules for file transfers ("FileTransfer" configuration section).</summary>
public interface IFileTransferPolicy
{
    long MaxPushBytes { get; }
    long MaxCollectBytes { get; }

    /// <summary>Lower-case extensions including the dot (".exe"), refused for PushFile.</summary>
    IReadOnlyCollection<string> BlockedExtensions { get; }
}

/// <summary>
/// Where pushed files and collected zips live on the server. Files are addressed by transfer id
/// (and computer id) — never by a caller-supplied name — so there is no path to traverse.
/// </summary>
public interface IFileTransferStore
{
    /// <summary>Saves the pushed file; throws <see cref="ArgumentException"/> when it exceeds <paramref name="maxBytes"/>
    /// (the partial file is removed).</summary>
    Task<(string Sha256, long SizeBytes)> SavePushFileAsync(int transferId, Stream content, long maxBytes, CancellationToken ct = default);

    Stream OpenPushFile(int transferId);

    /// <summary>Saves a zip uploaded by an Agent; throws <see cref="ArgumentException"/> above <paramref name="maxBytes"/>.</summary>
    Task<(string Sha256, long SizeBytes)> SaveCollectedAsync(int transferId, int computerId, Stream content, long maxBytes, CancellationToken ct = default);

    Stream? OpenCollected(int transferId, int computerId);

    /// <summary>Removes everything stored for a transfer (retention cleanup / failed create).</summary>
    void DeleteTransfer(int transferId);

    /// <summary>Free bytes on the volume holding the store — used to warn before the disk fills up.</summary>
    long AvailableFreeBytes();
}

/// <summary>Caps how many Agents may download a pushed file at once, so pushing 500 MB to a whole
/// room doesn't saturate the server; callers over the cap get 429 and retry later.</summary>
public interface ITransferThrottle
{
    /// <summary>Returns a lease to dispose when the download ends, or null when all slots are taken.</summary>
    IDisposable? TryAcquire();
}
