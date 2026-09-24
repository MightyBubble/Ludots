using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Engine;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Adapter.LiteNetLib;

public sealed class NetworkRuntimeProofDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("healthy")]
    public bool Healthy { get; set; }

    [JsonPropertyName("faultCount")]
    public int FaultCount { get; set; }

    [JsonPropertyName("lastFaultCode")]
    public string? LastFaultCode { get; set; }

    [JsonPropertyName("triggerErrorCount")]
    public int TriggerErrorCount { get; set; }

    [JsonPropertyName("connectedSeatCount")]
    public int ConnectedSeatCount { get; set; }

    [JsonPropertyName("roomPhase")]
    public string RoomPhase { get; set; } = string.Empty;

    [JsonPropertyName("committedTick")]
    public uint CommittedTick { get; set; }

    [JsonPropertyName("commandsObserved")]
    public int CommandsObserved { get; set; }

    [JsonPropertyName("snapshotsObserved")]
    public int SnapshotsObserved { get; set; }

    [JsonPropertyName("outboundBytesPerSecondPerClient")]
    public long[] OutboundBytesPerSecondPerClient { get; set; } = Array.Empty<long>();

    [JsonPropertyName("tickP95Microseconds")]
    public long TickP95Microseconds { get; set; }

    [JsonPropertyName("tickP99Microseconds")]
    public long TickP99Microseconds { get; set; }

    [JsonPropertyName("budgetViolation")]
    public string? BudgetViolation { get; set; }
}

/// <summary>
/// Periodically writes an atomic proof JSON document for acceptance evidence.
/// </summary>
public sealed class NetworkAcceptanceProofService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _proofPath;
    private readonly string _parentDirectory;
    private readonly string _role;
    private readonly int _intervalMilliseconds;
    private readonly NetworkAdapterMetricsSampler _metrics;
    private readonly NetworkRuntimeStateObserver _observer;
    private readonly GameEngine _engine;
    private readonly AcceptanceBudgetEnforcer? _enforcer;
    private readonly long[] _outboundScratch;
    private long _lastWriteTimestamp;
    private int _commandsObserved;
    private int _snapshotsObserved;

    public NetworkAcceptanceProofService(
        string proofPath,
        NetworkProcessRole role,
        int intervalMilliseconds,
        NetworkAdapterMetricsSampler metrics,
        NetworkRuntimeStateObserver observer,
        GameEngine engine,
        AcceptanceBudgetEnforcer? enforcer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proofPath);
        if (intervalMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));

        _proofPath = Path.GetFullPath(proofPath);
        _parentDirectory = Path.GetDirectoryName(_proofPath)
            ?? throw new ArgumentException("Proof path must have a parent directory.", nameof(proofPath));
        _role = role switch
        {
            NetworkProcessRole.AuthoritativeServer => "authoritativeServer",
            NetworkProcessRole.ReplicatedClient => "replicatedClient",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        _intervalMilliseconds = intervalMilliseconds;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _enforcer = enforcer;
        _outboundScratch = new long[metrics.ClientCapacity];
        _lastWriteTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public NetworkAdapterMetricsSampler Metrics => _metrics;

    public AcceptanceBudgetEnforcer? Enforcer => _enforcer;

    public void ObserveCommand() => _commandsObserved++;

    public void ObserveSnapshot() => _snapshotsObserved++;

    public void ObserveFullTickMicroseconds(ulong microseconds)
    {
        _metrics.AdvanceWallClock();
        _metrics.RecordTickMicroseconds(microseconds);
        _enforcer?.EnforceAfterTickSample();
        MaybeWriteProof();
    }

    public void PumpProof()
    {
        _metrics.AdvanceWallClock();
        MaybeWriteProof();
    }

    public void WriteProofNow()
    {
        var document = BuildDocument();
        WriteAtomic(document);
        _lastWriteTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void MaybeWriteProof()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastWriteTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (elapsedMs < _intervalMilliseconds)
        {
            return;
        }

        WriteAtomic(BuildDocument());
        _lastWriteTimestamp = now;
    }

    private NetworkRuntimeProofDocument BuildDocument()
    {
        _metrics.CopyCurrentOutboundBytesPerSecond(_outboundScratch);
        int connected = 0;
        for (int seat = 0; seat < _observer.SeatCapacity; seat++)
        {
            if (_observer.GetSeatState(seat) == NetworkSeatConnectionState.Connected)
            {
                connected++;
            }
        }

        string? lastFault = _observer.FaultCount == 0
            ? null
            : _observer.LastFault.Code.ToString();
        string roomPhase = _observer.HasRoomSnapshot
            ? _observer.LastRoomSnapshot.Phase.ToString()
            : string.Empty;
        uint committed = _observer.HasRoomSnapshot
            ? _observer.LastRoomSnapshot.CommittedTick
            : 0u;
        string? violation = _enforcer?.BudgetViolation;
        bool healthy = _observer.FaultCount == 0 &&
            _engine.TriggerManager.Errors.Count == 0 &&
            violation == null;

        var outbound = new long[_outboundScratch.Length];
        _outboundScratch.CopyTo(outbound, 0);

        return new NetworkRuntimeProofDocument
        {
            SchemaVersion = 1,
            Role = _role,
            Healthy = healthy,
            FaultCount = _observer.FaultCount,
            LastFaultCode = lastFault,
            TriggerErrorCount = _engine.TriggerManager.Errors.Count,
            ConnectedSeatCount = connected,
            RoomPhase = roomPhase,
            CommittedTick = committed,
            CommandsObserved = _commandsObserved,
            SnapshotsObserved = _snapshotsObserved,
            OutboundBytesPerSecondPerClient = outbound,
            TickP95Microseconds = _metrics.GetTickP95Microseconds(),
            TickP99Microseconds = _metrics.GetTickP99Microseconds(),
            BudgetViolation = violation,
        };
    }

    private void WriteAtomic(NetworkRuntimeProofDocument document)
    {
        Directory.CreateDirectory(_parentDirectory);
        string temporaryPath = Path.Combine(
            _parentDirectory,
            $".{Path.GetFileName(_proofPath)}.{Guid.NewGuid():N}.tmp");
        bool committed = false;
        try
        {
            string json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _proofPath, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
