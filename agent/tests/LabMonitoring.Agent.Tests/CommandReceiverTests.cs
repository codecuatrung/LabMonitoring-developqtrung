using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LabMonitoring.Agent;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabMonitoring.Agent.Tests;

public sealed class CommandReceiverTests
{
    private const string Secret = "unit-test-agent-secret";

    private static (CommandReceiver receiver, CredentialStore store) NewReceiver()
    {
        var dir = Path.Combine(Path.GetTempPath(), "labagent-tests", Guid.NewGuid().ToString("N"));
        var paths = new AgentPaths(dir);
        var store = new CredentialStore(paths, NullLogger<CredentialStore>.Instance);
        store.Save(new AgentCredentials(1, "PC-01", "token", Secret));
        return (new CommandReceiver(store, NullLogger<CommandReceiver>.Instance), store);
    }

    private static JsonElement Envelope(string payloadJson, string secret, DateTimeOffset? issuedAt = null, Guid? nonce = null)
    {
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadJson)));

        var envelope = new CommandEnvelope
        {
            CommandId = 12345L,
            Type = CommandType.Message,
            PayloadJson = payloadJson,
            IssuedAt = issuedAt ?? DateTimeOffset.UtcNow,
            Nonce = nonce ?? Guid.NewGuid(),
            Signature = signature,
        };
        return JsonSerializer.SerializeToElement(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
    }

    [Fact]
    public async Task Accepts_a_correctly_signed_command()
    {
        var (receiver, _) = NewReceiver();

        var result = await receiver.VerifyAsync(Envelope("{\"text\":\"hi\"}", Secret));

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Rejects_a_command_signed_with_the_wrong_secret()
    {
        var (receiver, _) = NewReceiver();

        var result = await receiver.VerifyAsync(Envelope("{\"text\":\"hi\"}", "wrong-secret"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_replayed_nonce()
    {
        var (receiver, _) = NewReceiver();
        var nonce = Guid.NewGuid();

        (await receiver.VerifyAsync(Envelope("{\"a\":1}", Secret, nonce: nonce))).Should().NotBeNull();
        (await receiver.VerifyAsync(Envelope("{\"a\":1}", Secret, nonce: nonce))).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_command_with_a_stale_timestamp()
    {
        var (receiver, _) = NewReceiver();

        var result = await receiver.VerifyAsync(
            Envelope("{\"a\":1}", Secret, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-30)));

        result.Should().BeNull();
    }
}
