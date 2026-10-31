using System.Security.Cryptography;
using System.Text;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Infrastructure.Security;

internal sealed class HmacCommandSigner(LabMonitoringDbContext db, AgentSecretProtector protector) : ICommandSigner
{
    public async Task<string> SignForComputerAsync(int computerId, string payloadJson, CancellationToken ct = default)
    {
        var protectedSecret = await db.Computers
            .Where(c => c.Id == computerId)
            .Select(c => c.AgentSecretHash)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Computer), computerId);

        var secret = protector.Unprotect(protectedSecret);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToBase64String(signature);
    }
}
