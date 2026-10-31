using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Infrastructure.Identity;

/// <summary>
/// Maps a Keycloak identity to a local <c>Users</c> row (keyed on <c>sub</c>), creating it on first
/// sign-in. Grants Administrator to bootstrap accounts that still have no roles.
/// The first authenticated burst runs several requests in parallel, so writes are retried on the
/// unique/PK violation that a losing racer hits.
/// </summary>
internal sealed class UserProvisioner(
    LabMonitoringDbContext db,
    IOptions<AuthOptions> authOptions,
    ILogger<UserProvisioner> logger) : IUserProvisioner
{
    public async Task<ProvisionedUser> EnsureProvisionedAsync(KeycloakIdentity identity, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ProvisionOnceAsync(identity, ct);
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // A concurrent request created the same user / role first. Drop tracked state and re-read.
                db.ChangeTracker.Clear();
                logger.LogDebug("Provisioning race for {Subject}; retrying (attempt {Next}).", identity.Subject, attempt + 1);
            }
        }
    }

    private async Task<ProvisionedUser> ProvisionOnceAsync(KeycloakIdentity identity, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.KeycloakUserId == identity.Subject, ct);

        if (user is null)
        {
            user = new User
            {
                KeycloakUserId = identity.Subject,
                Username = identity.Username,
                FullName = identity.FullName ?? identity.Username,
                Email = identity.Email,
                IsActive = true,
            };
            db.Users.Add(user);
            logger.LogInformation("Provisioned new user {Username} ({Subject})", identity.Username, identity.Subject);
        }
        else
        {
            // Keep the mutable profile fields in step with Keycloak.
            user.Username = identity.Username;
            user.FullName = identity.FullName ?? user.FullName;
            user.Email = identity.Email;
        }

        if (user.UserRoles.Count == 0 && IsBootstrapAdmin(identity))
        {
            user.UserRoles.Add(new UserRole { RoleId = SeededRoles.AdministratorId });
            logger.LogWarning("Granted Administrator to bootstrap account {Username}", identity.Username);
        }

        await db.SaveChangesAsync(ct);

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.Role.Name)
            .ToListAsync(ct);

        return new ProvisionedUser(user.Id, roles);
    }

    private bool IsBootstrapAdmin(KeycloakIdentity identity)
    {
        var list = authOptions.Value.BootstrapAdmins;
        return list.Contains(identity.Username, StringComparer.OrdinalIgnoreCase)
            || (identity.Email is not null && list.Contains(identity.Email, StringComparer.OrdinalIgnoreCase));
    }
}
