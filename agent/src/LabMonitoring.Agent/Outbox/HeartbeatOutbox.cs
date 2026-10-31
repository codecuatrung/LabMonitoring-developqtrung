using System.Collections.Concurrent;
using System.Text.Json;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Agent.Outbox;

/// <summary>
/// Bounded, file-backed queue of heartbeats that could not be delivered (SRS NFR "Độ tin cậy Agent":
/// up to 500 records, resent on reconnect with backoff). Oldest records are dropped when full.
/// </summary>
public sealed class HeartbeatOutbox
{
    private readonly int _capacity;
    private readonly AgentPaths _paths;
    private readonly ILogger<HeartbeatOutbox> _logger;
    private readonly ConcurrentQueue<AgentHeartbeatRequest> _queue = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public HeartbeatOutbox(IOptions<AgentOptions> options, AgentPaths paths, ILogger<HeartbeatOutbox> logger)
    {
        _capacity = options.Value.OutboxCapacity;
        _paths = paths;
        _logger = logger;
        LoadFromDisk();
    }

    public int Count => _queue.Count;

    public void Enqueue(AgentHeartbeatRequest item)
    {
        _queue.Enqueue(item);
        while (_queue.Count > _capacity && _queue.TryDequeue(out _))
        {
            _logger.LogWarning("Outbox full ({Capacity}); dropped the oldest heartbeat.", _capacity);
        }
        PersistSafe();
    }

    /// <summary>Delivers queued items in order via <paramref name="send"/>; stops at the first failure.</summary>
    public async Task<int> DrainAsync(Func<AgentHeartbeatRequest, Task<bool>> send, CancellationToken ct)
    {
        var delivered = 0;
        while (!ct.IsCancellationRequested && _queue.TryPeek(out var item))
        {
            if (!await send(item))
                break;

            _queue.TryDequeue(out _);
            delivered++;
        }

        if (delivered > 0)
        {
            _logger.LogInformation("Flushed {Count} queued heartbeat(s); {Remaining} remaining.", delivered, _queue.Count);
            PersistSafe();
        }

        return delivered;
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_paths.OutboxFile)) return;
        try
        {
            foreach (var line in File.ReadAllLines(_paths.OutboxFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<AgentHeartbeatRequest>(line);
                if (item is not null) _queue.Enqueue(item);
            }
            _logger.LogInformation("Restored {Count} queued heartbeat(s) from disk.", _queue.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore outbox; starting empty.");
        }
    }

    private void PersistSafe()
    {
        _fileLock.Wait();
        try
        {
            var lines = _queue.ToArray().Select(i => JsonSerializer.Serialize(i));
            File.WriteAllLines(_paths.OutboxFile, lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist outbox.");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
