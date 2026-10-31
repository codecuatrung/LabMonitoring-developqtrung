using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Identity;

namespace LabMonitoring.Domain.Transfers;

public enum TransferDirection
{
    /// <summary>Server → client: an uploaded file is copied into an allowed folder on the machine(s).</summary>
    Push,

    /// <summary>Client → server: an allowed folder is zipped by the Agent and uploaded to the server.</summary>
    Collect,
}

/// <summary>
/// A folder on the client machines that file-transfer commands may write into (<see cref="TransferDirection.Push"/>)
/// or read from (<see cref="TransferDirection.Collect"/>). Only folders listed here are ever touched — the
/// admin UI picks from this list, the server re-checks it, and the Agent re-checks it against its
/// synced config (defense in depth). <see cref="RoomId"/> null = applies to every room.
/// </summary>
public class TransferFolder
{
    public int Id { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    /// <summary>Path as seen by the Agent (LocalSystem). Absolute drive path, or one using
    /// <c>%PUBLIC%</c>/<c>%ProgramData%</c> style variables the Agent expands.</summary>
    public string Path { get; set; } = null!;

    public TransferDirection Direction { get; set; }
    public string Label { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>One push or collect job (fanned out to one machine or a whole room as a single signed command).</summary>
public class FileTransfer
{
    public int Id { get; set; }
    public TransferDirection Direction { get; set; }

    /// <summary>The <c>Commands.Id</c> that carries this job to the Agents; set right after issuing.</summary>
    public long? CommandId { get; set; }

    // Plain columns (no FKs): a job's history must survive deleting a room/machine/folder.
    public int? RoomId { get; set; }
    public int? ComputerId { get; set; }
    public int? TransferFolderId { get; set; }

    /// <summary>Snapshot of the folder path at issue time (the payload the Agents received).</summary>
    public string FolderPath { get; set; } = null!;

    /// <summary>Push: original file name. Collect: null.</summary>
    public string? FileName { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public bool Overwrite { get; set; }

    /// <summary>Collect: file-name filter such as <c>*.docx;*.pdf</c>. Null/empty = every file.</summary>
    public string? Filter { get; set; }

    /// <summary>Collect: per-machine size cap the Agent enforces while zipping.</summary>
    public long MaxBytes { get; set; }

    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<CollectedFile> Collected { get; set; } = [];
}

/// <summary>The zip a machine uploaded for a <see cref="TransferDirection.Collect"/> job.</summary>
public class CollectedFile
{
    public int Id { get; set; }
    public int FileTransferId { get; set; }
    public FileTransfer FileTransfer { get; set; } = null!;

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = null!;
    public int FileCount { get; set; }

    /// <summary>True when the Agent stopped early because the size cap was reached.</summary>
    public bool Truncated { get; set; }
    public DateTime ReceivedAt { get; set; }
}
