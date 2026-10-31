using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Agent;

/// <summary>Uploaded LabAgent builds (SRS H1 "auto-update") — admin-facing list/upload. The Agent's
/// own download of "whichever is current" goes through <see cref="Controllers.AgentController"/>
/// directly against <see cref="IAppDbContext.AgentPackages"/>/<see cref="IAgentPackageStore"/>, not
/// through these MediatR handlers (that endpoint authenticates with AgentToken, not a JWT/ICurrentUser).</summary>
public sealed record AgentPackageDto(int Id, string Version, string FileName, long FileSizeBytes, string Sha256, string? UploadedByName, DateTime UploadedAt);

public sealed record GetAgentPackagesQuery : IRequest<IReadOnlyList<AgentPackageDto>>;

internal sealed class GetAgentPackagesHandler(IAppDbContext db) : IRequestHandler<GetAgentPackagesQuery, IReadOnlyList<AgentPackageDto>>
{
    public async Task<IReadOnlyList<AgentPackageDto>> Handle(GetAgentPackagesQuery request, CancellationToken ct) =>
        await db.AgentPackages
            .OrderByDescending(p => p.UploadedAt)
            .Select(p => new AgentPackageDto(
                p.Id, p.Version, p.FileName, p.FileSizeBytes, p.Sha256,
                p.UploadedBy != null ? p.UploadedBy.FullName : null, p.UploadedAt))
            .ToListAsync(ct);
}

public sealed record UploadAgentPackageCommand(string Version, Stream Content) : IRequest<int>;

public sealed class UploadAgentPackageValidator : AbstractValidator<UploadAgentPackageCommand>
{
    public UploadAgentPackageValidator()
    {
        // Becomes the on-disk file name ({version}.zip) — restrict to characters that are safe
        // there and match AgentRegistrationService.AgentVersion's own convention (e.g. "1.2.3").
        RuleFor(x => x.Version).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Version may only contain letters, digits, '.', '_' and '-'.");
    }
}

internal sealed class UploadAgentPackageHandler(IAppDbContext db, IAgentPackageStore store, ICurrentUser currentUser)
    : IRequestHandler<UploadAgentPackageCommand, int>
{
    public async Task<int> Handle(UploadAgentPackageCommand request, CancellationToken ct)
    {
        if (await db.AgentPackages.AnyAsync(p => p.Version == request.Version, ct))
            throw new ConflictException($"Version '{request.Version}' has already been uploaded.");

        var fileName = $"{request.Version}.zip";
        var (sha256, sizeBytes) = await store.SaveAsync(request.Content, fileName, ct);

        var package = new AgentPackage
        {
            Version = request.Version,
            FileName = fileName,
            FileSizeBytes = sizeBytes,
            Sha256 = sha256,
            UploadedById = currentUser.UserId,
            UploadedAt = DateTime.UtcNow,
        };
        db.AgentPackages.Add(package);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "AgentPackage.Upload",
            EntityType = nameof(AgentPackage),
            EntityId = request.Version,
            Detail = System.Text.Json.JsonSerializer.Serialize(new { sha256, sizeBytes }),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return package.Id;
    }
}
