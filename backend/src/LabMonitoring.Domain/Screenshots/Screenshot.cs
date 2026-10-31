using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Sessions;

namespace LabMonitoring.Domain.Screenshots;

/// <summary>
/// Screenshot metadata only — the image file lives on NAS / object storage, never in the DB
/// (SRS FR-3.3, constraint 5.4). Rows past <see cref="ExpiresAt"/> are purged by a job (SRS FR-3.4).
/// </summary>
public class Screenshot
{
    public long Id { get; set; }

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    /// <summary>Null only for captures outside a session (which FR-3.2 normally blocks).</summary>
    public int? SessionId { get; set; }
    public Session? Session { get; set; }

    public DateTime CapturedAt { get; set; }

    public StorageProvider StorageProvider { get; set; }

    /// <summary>Path on the NAS / bucket key. No image bytes are stored in the database.</summary>
    public string FilePath { get; set; } = null!;

    public int? FileSizeKB { get; set; }

    public DateTime ExpiresAt { get; set; }
}
