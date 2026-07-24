using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Runtime;

namespace Ludots.App.LoadClients;

public sealed class LoadClientRunEvidence
{
    public LoadClientRunOutcome Outcome { get; init; }
    public LoadClientFaultKind FaultKind { get; init; }
    public string FaultDetail { get; init; } = string.Empty;
    public int ConfiguredClients { get; init; }
    public int ConnectedClients { get; init; }
    public int ReadyClients { get; init; }
    public int UniqueLocalEndpoints { get; init; }
    public long FixedInputsGenerated { get; init; }
    public long FixedInputsPulsed { get; init; }
    public uint MaxFixedInputAcknowledgedCommittedTick { get; init; }
    public int DisconnectsAfterReady { get; init; }
    public int Rejections { get; init; }
    public int RuntimeFaultCode { get; init; }
    public double ElapsedSeconds { get; init; }
    public bool HeldThirtyHzContract { get; init; }
    public string ServerAdoptionObservabilityGap { get; init; } = string.Empty;

    public string ToMachineReadableLine()
    {
        var builder = new StringBuilder(512);
        builder.Append("outcome=").Append(Outcome.ToString());
        builder.Append(";faultKind=").Append(FaultKind.ToString());
        builder.Append(";faultDetail=").Append(Escape(FaultDetail));
        builder.Append(";configuredClients=").Append(ConfiguredClients.ToString(CultureInfo.InvariantCulture));
        builder.Append(";connectedClients=").Append(ConnectedClients.ToString(CultureInfo.InvariantCulture));
        builder.Append(";readyClients=").Append(ReadyClients.ToString(CultureInfo.InvariantCulture));
        builder.Append(";uniqueLocalEndpoints=").Append(UniqueLocalEndpoints.ToString(CultureInfo.InvariantCulture));
        builder.Append(";fixedInputsGenerated=").Append(FixedInputsGenerated.ToString(CultureInfo.InvariantCulture));
        builder.Append(";fixedInputsPulsed=").Append(FixedInputsPulsed.ToString(CultureInfo.InvariantCulture));
        builder.Append(";maxFixedInputAckCommittedTick=")
            .Append(MaxFixedInputAcknowledgedCommittedTick.ToString(CultureInfo.InvariantCulture));
        builder.Append(";disconnectsAfterReady=").Append(DisconnectsAfterReady.ToString(CultureInfo.InvariantCulture));
        builder.Append(";rejections=").Append(Rejections.ToString(CultureInfo.InvariantCulture));
        builder.Append(";runtimeFaultCode=").Append(RuntimeFaultCode.ToString(CultureInfo.InvariantCulture));
        builder.Append(";elapsedSeconds=").Append(ElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append(";heldThirtyHzContract=").Append(HeldThirtyHzContract ? "true" : "false");
        builder.Append(";serverAdoptionObservabilityGap=").Append(Escape(ServerAdoptionObservabilityGap));
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace('\\', '/').Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
    }
}

public sealed class LoadClientHost
{
    private readonly LoadClientHostConfig _config;
    private readonly ILoadClientSlotFactory _slotFactory;
    private readonly string _credentialDirectory;

    public LoadClientHost(
        LoadClientHostConfig config,
        ILoadClientSlotFactory? slotFactory = null,
        string? baseDirectory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();
        _slotFactory = slotFactory ?? new LiteNetLibLoadClientSlotFactory();
        string root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);
        _credentialDirectory = Path.IsPathRooted(_config.CredentialDirectory)
            ? Path.GetFullPath(_config.CredentialDirectory)
            : Path.GetFullPath(Path.Combine(root, _config.CredentialDirectory));
    }

    public LoadClientRunEvidence Run(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_credentialDirectory);
        LoadClientSlot[] slots = new LoadClientSlot[_config.ClientCount];
        int constructed = 0;
        var stopwatch = Stopwatch.StartNew();
        LoadClientFaultKind faultKind = LoadClientFaultKind.None;
        string faultDetail = string.Empty;
        int runtimeFaultCode = 0;
        int connectedClients = 0;
        int readyClients = 0;
        int uniqueEndpoints = 0;
        long generated = 0;
        long pulsed = 0;
        uint maxAck = 0;
        int disconnectsAfterReady = 0;
        int rejections = 0;
        bool heldThirtyHz = false;
        bool cancelled = false;

        try
        {
            // Preallocate every slot before opening the connect wave.
            for (int i = 0; i < slots.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    faultKind = LoadClientFaultKind.Cancelled;
                    faultDetail = "Cancelled during slot construction.";
                    break;
                }

                slots[i] = _slotFactory.Create(i, _config, _credentialDirectory);
                constructed++;
            }

            if (cancelled)
            {
                return Finalize(
                    LoadClientRunOutcome.Cancelled,
                    faultKind,
                    faultDetail,
                    slots,
                    constructed,
                    connectedClients,
                    readyClients,
                    uniqueEndpoints,
                    generated,
                    pulsed,
                    maxAck,
                    disconnectsAfterReady,
                    rejections,
                    runtimeFaultCode,
                    stopwatch.Elapsed.TotalSeconds,
                    heldThirtyHzContract: false);
            }

            uniqueEndpoints = CountUniqueBoundPorts(slots, constructed);
            if (uniqueEndpoints != constructed)
            {
                return Finalize(
                    LoadClientRunOutcome.Failed,
                    LoadClientFaultKind.Construction,
                    $"Expected {constructed} distinct LiteNetLib local endpoints; observed {uniqueEndpoints}.",
                    slots,
                    constructed,
                    connectedClients,
                    readyClients,
                    uniqueEndpoints,
                    generated,
                    pulsed,
                    maxAck,
                    disconnectsAfterReady,
                    rejections,
                    runtimeFaultCode,
                    stopwatch.Elapsed.TotalSeconds,
                    heldThirtyHzContract: false);
            }

            for (int i = 0; i < constructed; i++)
            {
                if (!slots[i].TryConnect())
                {
                    return Finalize(
                        LoadClientRunOutcome.Failed,
                        LoadClientFaultKind.PartialConnect,
                        $"Client {i} rejected TryConnectNow before the connect wave completed.",
                        slots,
                        constructed,
                        connectedClients,
                        readyClients,
                        uniqueEndpoints,
                        generated,
                        pulsed,
                        maxAck,
                        disconnectsAfterReady,
                        rejections,
                        runtimeFaultCode,
                        stopwatch.Elapsed.TotalSeconds,
                        heldThirtyHzContract: false);
                }
            }

            long previousTimestamp = Stopwatch.GetTimestamp();
            double connectDeadline = _config.ConnectTimeoutSeconds;
            double readyDeadline = _config.ConnectTimeoutSeconds + _config.ReadyTimeoutSeconds;
            // Connect/ready waiting must not consume active run duration or warmup.
            double readyAtSeconds = double.NaN;
            double runDeadlineSeconds = double.PositiveInfinity;
            bool allConnected = false;
            bool allReady = false;
            long[] measurementBaselines = new long[slots.Length];
            bool measurementStarted = false;
            double measurementElapsed = 0d;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    faultKind = LoadClientFaultKind.Cancelled;
                    faultDetail = "Cancelled by operator (Ctrl+C).";
                    break;
                }

                long now = Stopwatch.GetTimestamp();
                float deltaSeconds = (float)((now - previousTimestamp) / (double)Stopwatch.Frequency);
                previousTimestamp = now;
                double elapsed = stopwatch.Elapsed.TotalSeconds;

                if (allReady)
                {
                    double sinceReady = elapsed - readyAtSeconds;
                    if (!measurementStarted && sinceReady >= _config.WarmUpSeconds)
                    {
                        for (int baselineIndex = 0; baselineIndex < constructed; baselineIndex++)
                        {
                            measurementBaselines[baselineIndex] = slots[baselineIndex].FixedInputsGenerated;
                        }

                        measurementStarted = true;
                    }

                    if (measurementStarted)
                    {
                        measurementElapsed = sinceReady - _config.WarmUpSeconds;
                    }
                }

                for (int i = 0; i < constructed; i++)
                {
                    LoadClientSlot slot = slots[i];
                    ILoadClientSlotTestDriver? driver = slot.TestDriver;
                    if (driver != null)
                    {
                        driver.Pump(deltaSeconds);
                    }
                    else
                    {
                        slot.Runtime.PumpTransport();
                        slot.Runtime.PumpReplicatedClient(deltaSeconds);
                    }

                    bool isFaulted = driver?.IsFaulted ?? (slot.Observer.FaultCount > 0 || slot.Runtime.IsFaulted);
                    if (isFaulted)
                    {
                        NetworkRuntimeFault fault = driver != null
                            ? driver.LastFault
                            : slot.Runtime.IsFaulted
                                ? slot.Runtime.LastFault
                                : slot.Observer.LastFault;
                        runtimeFaultCode = (int)fault.Code;
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            LoadClientFaultKind.RuntimeFault,
                            $"Client {i} runtime fault code {fault.Code} severity {fault.Severity} detail {fault.Detail}.",
                            slots,
                            constructed,
                            CountConnected(slots, constructed),
                            CountReady(slots, constructed),
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            CountRejections(slots, constructed),
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }

                    ReplicatedClientConnectionState connectionState = driver?.ConnectionState ?? slot.Runtime.State;
                    if (connectionState == ReplicatedClientConnectionState.Rejected ||
                        (driver == null && slot.Observer.HandshakeSeen && !slot.Observer.HandshakeAccepted))
                    {
                        rejections = CountRejections(slots, constructed);
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            LoadClientFaultKind.Rejection,
                            $"Client {i} was rejected (reason {slot.Observer.RejectReason}).",
                            slots,
                            constructed,
                            CountConnected(slots, constructed),
                            CountReady(slots, constructed),
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            rejections,
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }

                    if (slot.IsReady &&
                        connectionState != ReplicatedClientConnectionState.Connected)
                    {
                        slot.DisconnectAfterReady = true;
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            LoadClientFaultKind.UnexpectedDisconnect,
                            $"Client {i} disconnected after readiness (state {connectionState}).",
                            slots,
                            constructed,
                            CountConnected(slots, constructed),
                            CountReady(slots, constructed),
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            CountRejections(slots, constructed),
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }

                    ReplicatedClientFixedInputClockAdvanceResult advance = driver != null
                        ? driver.Advance(deltaSeconds)
                        : slot.Clock.Advance(deltaSeconds);
                    if (!advance.IsSuccess)
                    {
                        LoadClientFaultKind kind = advance.Status switch
                        {
                            ReplicatedClientFixedInputClockAdvanceStatus.SourceFailed =>
                                LoadClientFaultKind.FixedInputSourceFailed,
                            ReplicatedClientFixedInputClockAdvanceStatus.EnqueueRejected =>
                                LoadClientFaultKind.EnqueueRejected,
                            ReplicatedClientFixedInputClockAdvanceStatus.PulseFailed =>
                                LoadClientFaultKind.PulseFailed,
                            ReplicatedClientFixedInputClockAdvanceStatus.CatchUpBacklogExceeded =>
                                LoadClientFaultKind.CatchUpBacklogExceeded,
                            _ => LoadClientFaultKind.CapacityFailure,
                        };
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            kind,
                            $"Client {i} fixed-input advance failed with {advance.Status} enqueue={advance.EnqueueStatus}.",
                            slots,
                            constructed,
                            CountConnected(slots, constructed),
                            CountReady(slots, constructed),
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            CountRejections(slots, constructed),
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }

                    if (advance.StepsEmitted > 0)
                    {
                        slot.FixedInputsGenerated += advance.StepsEmitted;
                        slot.FixedInputsPulsed += advance.StepsEmitted;
                    }

                    uint ack = driver?.FixedInputAcknowledgedCommittedTick
                        ?? slot.Runtime.FixedInputAcknowledgedCommittedTick;
                    if (ack > slot.HighestAcknowledgedCommittedTick)
                    {
                        slot.HighestAcknowledgedCommittedTick = ack;
                    }

                    bool waitingForAck = driver?.IsWaitingForAuthoritativeAcknowledgement
                        ?? slot.Clock.IsWaitingForAuthoritativeAcknowledgement;
                    ulong ackObservationVersion = driver?.FixedInputAcknowledgementObservationVersion
                        ?? slot.Runtime.FixedInputAcknowledgementObservationVersion;
                    if (!slot.IsReady &&
                        connectionState == ReplicatedClientConnectionState.Connected &&
                        !waitingForAck &&
                        ackObservationVersion > 0)
                    {
                        slot.IsReady = true;
                    }
                }

                connectedClients = CountConnected(slots, constructed);
                readyClients = CountReady(slots, constructed);

                if (!allConnected)
                {
                    if (connectedClients == constructed)
                    {
                        allConnected = true;
                    }
                    else if (elapsed >= connectDeadline)
                    {
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            LoadClientFaultKind.ConnectTimeout,
                            $"Connect timeout: {connectedClients}/{constructed} clients reached Connected.",
                            slots,
                            constructed,
                            connectedClients,
                            readyClients,
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            CountRejections(slots, constructed),
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }
                }

                if (allConnected && !allReady)
                {
                    if (readyClients == constructed)
                    {
                        allReady = true;
                        readyAtSeconds = elapsed;
                        // Warm-up begins at readyAt; measurement window is DurationSeconds after warm-up.
                        runDeadlineSeconds = readyAtSeconds + _config.WarmUpSeconds + _config.DurationSeconds;
                        if (!measurementStarted && _config.WarmUpSeconds <= 0d)
                        {
                            for (int baselineIndex = 0; baselineIndex < constructed; baselineIndex++)
                            {
                                measurementBaselines[baselineIndex] = slots[baselineIndex].FixedInputsGenerated;
                            }

                            measurementStarted = true;
                            measurementElapsed = 0d;
                        }
                    }
                    else if (elapsed >= readyDeadline)
                    {
                        return Finalize(
                            LoadClientRunOutcome.Failed,
                            LoadClientFaultKind.ReadyTimeout,
                            $"Ready timeout: {readyClients}/{constructed} clients observed post-connect fixed-input ACK.",
                            slots,
                            constructed,
                            connectedClients,
                            readyClients,
                            uniqueEndpoints,
                            SumGenerated(slots, constructed),
                            SumPulsed(slots, constructed),
                            MaxAck(slots, constructed),
                            CountDisconnects(slots, constructed),
                            CountRejections(slots, constructed),
                            runtimeFaultCode,
                            elapsed,
                            heldThirtyHzContract: false);
                    }
                }

                // Completion is readyAt + WarmUpSeconds + DurationSeconds.
                if (allReady && elapsed >= runDeadlineSeconds)
                {
                    break;
                }

                // Yield briefly so 149 real UDP endpoints can be polled without a tight spin.
                Thread.Sleep(0);
            }

            generated = SumGenerated(slots, constructed);
            pulsed = SumPulsed(slots, constructed);
            maxAck = MaxAck(slots, constructed);
            disconnectsAfterReady = CountDisconnects(slots, constructed);
            rejections = CountRejections(slots, constructed);
            connectedClients = CountConnected(slots, constructed);
            readyClients = CountReady(slots, constructed);

            if (cancelled)
            {
                return Finalize(
                    LoadClientRunOutcome.Cancelled,
                    faultKind,
                    faultDetail,
                    slots,
                    constructed,
                    connectedClients,
                    readyClients,
                    uniqueEndpoints,
                    generated,
                    pulsed,
                    maxAck,
                    disconnectsAfterReady,
                    rejections,
                    runtimeFaultCode,
                    stopwatch.Elapsed.TotalSeconds,
                    heldThirtyHzContract: false);
            }

            if (!allReady || readyClients != constructed || connectedClients != constructed)
            {
                return Finalize(
                    LoadClientRunOutcome.Failed,
                    LoadClientFaultKind.PartialConnect,
                    $"Run ended without full readiness: connected={connectedClients} ready={readyClients} configured={constructed}.",
                    slots,
                    constructed,
                    connectedClients,
                    readyClients,
                    uniqueEndpoints,
                    generated,
                    pulsed,
                    maxAck,
                    disconnectsAfterReady,
                    rejections,
                    runtimeFaultCode,
                    stopwatch.Elapsed.TotalSeconds,
                    heldThirtyHzContract: false);
            }

            double expectedTicks = measurementElapsed * _config.SimulationTickRateHz;
            // Allow one tick of quantization slack on wall-clock measurement; never invent missing load.
            long minExpected = (long)Math.Floor(expectedTicks);
            long maxExpected = (long)Math.Ceiling(expectedTicks) + _config.MaxStepsPerAdvance;
            if (!(measurementStarted && measurementElapsed > 0d))
            {
                return Finalize(
                    LoadClientRunOutcome.Failed,
                    LoadClientFaultKind.TickRateContractBroken,
                    $"30Hz contract broken: measurement window was empty (measurementStarted={measurementStarted} measurementElapsed={measurementElapsed.ToString("0.###", CultureInfo.InvariantCulture)}s).",
                    slots,
                    constructed,
                    connectedClients,
                    readyClients,
                    uniqueEndpoints,
                    generated,
                    pulsed,
                    maxAck,
                    disconnectsAfterReady,
                    rejections,
                    runtimeFaultCode,
                    stopwatch.Elapsed.TotalSeconds,
                    heldThirtyHzContract: false);
            }

            for (int i = 0; i < constructed; i++)
            {
                long delta = slots[i].FixedInputsGenerated - measurementBaselines[i];
                if (delta < minExpected || delta > maxExpected)
                {
                    return Finalize(
                        LoadClientRunOutcome.Failed,
                        LoadClientFaultKind.TickRateContractBroken,
                        $"30Hz contract broken: client {i} measurementGenerated={delta.ToString(CultureInfo.InvariantCulture)} expected=[{minExpected.ToString(CultureInfo.InvariantCulture)},{maxExpected.ToString(CultureInfo.InvariantCulture)}] over {measurementElapsed.ToString("0.###", CultureInfo.InvariantCulture)}s.",
                        slots,
                        constructed,
                        connectedClients,
                        readyClients,
                        uniqueEndpoints,
                        generated,
                        pulsed,
                        maxAck,
                        disconnectsAfterReady,
                        rejections,
                        runtimeFaultCode,
                        stopwatch.Elapsed.TotalSeconds,
                        heldThirtyHzContract: false);
                }
            }

            heldThirtyHz = true;

            return Finalize(
                LoadClientRunOutcome.Passed,
                LoadClientFaultKind.None,
                string.Empty,
                slots,
                constructed,
                connectedClients,
                readyClients,
                uniqueEndpoints,
                generated,
                pulsed,
                maxAck,
                disconnectsAfterReady,
                rejections,
                runtimeFaultCode,
                stopwatch.Elapsed.TotalSeconds,
                heldThirtyHz);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Finalize(
                LoadClientRunOutcome.Failed,
                faultKind == LoadClientFaultKind.None ? LoadClientFaultKind.Construction : faultKind,
                exception.Message,
                slots,
                constructed,
                CountConnected(slots, constructed),
                CountReady(slots, constructed),
                uniqueEndpoints,
                SumGenerated(slots, constructed),
                SumPulsed(slots, constructed),
                MaxAck(slots, constructed),
                CountDisconnects(slots, constructed),
                CountRejections(slots, constructed),
                runtimeFaultCode,
                stopwatch.Elapsed.TotalSeconds,
                heldThirtyHzContract: false);
        }
    }

    private static LoadClientRunEvidence Finalize(
        LoadClientRunOutcome outcome,
        LoadClientFaultKind faultKind,
        string faultDetail,
        LoadClientSlot[] slots,
        int constructed,
        int connectedClients,
        int readyClients,
        int uniqueEndpoints,
        long generated,
        long pulsed,
        uint maxAck,
        int disconnectsAfterReady,
        int rejections,
        int runtimeFaultCode,
        double elapsedSeconds,
        bool heldThirtyHzContract)
    {
        LoadClientFaultKind disposeFault = LoadClientFaultKind.None;
        string disposeDetail = string.Empty;
        for (int i = 0; i < constructed; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            try
            {
                slot.Dispose();
            }
            catch (Exception exception)
            {
                disposeFault = LoadClientFaultKind.DisposalFailure;
                disposeDetail = $"Client {i} disposal failed: {exception.Message}";
                if (outcome == LoadClientRunOutcome.Passed)
                {
                    outcome = LoadClientRunOutcome.Failed;
                    faultKind = disposeFault;
                    faultDetail = disposeDetail;
                }
            }

            slots[i] = null!;
        }

        if (disposeFault != LoadClientFaultKind.None && outcome != LoadClientRunOutcome.Cancelled)
        {
            outcome = LoadClientRunOutcome.Failed;
            faultKind = disposeFault;
            if (string.IsNullOrEmpty(faultDetail))
            {
                faultDetail = disposeDetail;
            }
        }

        return new LoadClientRunEvidence
        {
            Outcome = outcome,
            FaultKind = faultKind,
            FaultDetail = faultDetail,
            ConfiguredClients = slots.Length,
            ConnectedClients = connectedClients,
            ReadyClients = readyClients,
            UniqueLocalEndpoints = uniqueEndpoints,
            FixedInputsGenerated = generated,
            FixedInputsPulsed = pulsed,
            MaxFixedInputAcknowledgedCommittedTick = maxAck,
            DisconnectsAfterReady = disconnectsAfterReady,
            Rejections = rejections,
            RuntimeFaultCode = runtimeFaultCode,
            ElapsedSeconds = elapsedSeconds,
            HeldThirtyHzContract = heldThirtyHzContract,
            // ReplicatedClientNetworkRuntime exposes FixedInputAcknowledgedCommittedTick (client-observed ACK),
            // not authoritative server-side adoption/commit of each submitted frame.
            ServerAdoptionObservabilityGap =
                "Public ReplicatedClientNetworkRuntime exposes FixedInputAcknowledgedCommittedTick and FixedInputAcknowledgementObservationVersion only; it does not expose server-side per-frame adoption counts. Evidence reports client-observed ACK progress only.",
        };
    }

    private static int CountUniqueBoundPorts(LoadClientSlot[] slots, int count)
    {
        // Preallocated scan — no HashSet growth on the construction path beyond this one-time check.
        int unique = 0;
        for (int i = 0; i < count; i++)
        {
            int port = slots[i].BoundPort;
            bool seen = false;
            for (int j = 0; j < i; j++)
            {
                if (slots[j].BoundPort == port)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                unique++;
            }
        }

        return unique;
    }

    private static int CountConnected(LoadClientSlot[] slots, int count)
    {
        int connected = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            ReplicatedClientConnectionState state = slot.TestDriver?.ConnectionState ?? slot.Runtime.State;
            if (state == ReplicatedClientConnectionState.Connected)
            {
                connected++;
            }
        }

        return connected;
    }

    private static int CountReady(LoadClientSlot[] slots, int count)
    {
        int ready = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot is { IsReady: true })
            {
                ready++;
            }
        }

        return ready;
    }

    private static long SumGenerated(LoadClientSlot[] slots, int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot != null)
            {
                total += slot.FixedInputsGenerated;
            }
        }

        return total;
    }

    private static long SumPulsed(LoadClientSlot[] slots, int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot != null)
            {
                total += slot.FixedInputsPulsed;
            }
        }

        return total;
    }

    private static uint MaxAck(LoadClientSlot[] slots, int count)
    {
        uint max = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot != null && slot.HighestAcknowledgedCommittedTick > max)
            {
                max = slot.HighestAcknowledgedCommittedTick;
            }
        }

        return max;
    }

    private static int CountDisconnects(LoadClientSlot[] slots, int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot is { DisconnectAfterReady: true })
            {
                total++;
            }
        }

        return total;
    }

    private static int CountRejections(LoadClientSlot[] slots, int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            LoadClientSlot? slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            ReplicatedClientConnectionState state = slot.TestDriver?.ConnectionState ?? slot.Runtime.State;
            if (state == ReplicatedClientConnectionState.Rejected ||
                (slot.TestDriver == null && slot.Observer.HandshakeSeen && !slot.Observer.HandshakeAccepted))
            {
                total++;
            }
        }

        return total;
    }
}
