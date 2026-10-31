namespace LabMonitoring.Agent.Commands;

// Shapes of CommandEnvelope.PayloadJson per command type — mirrors how the server builds them
// (backend/src/LabMonitoring.Application/Commands/CommandHandlers.cs BuildPayload).

public sealed record MessagePayload(string? Text);
public sealed record OpenAppPayload(string? ExecutablePath);
public sealed record OpenUrlPayload(string? Url);

public sealed record PresentationPayload(Guid SessionId, bool LockInput = true);

public sealed record PushFilePayload(
    int TransferId, string FileName, long SizeBytes, string Sha256, int FolderId, string FolderPath, bool Overwrite);

public sealed record CollectFilesPayload(int TransferId, int FolderId, string FolderPath, string? Filter, long MaxBytes);
