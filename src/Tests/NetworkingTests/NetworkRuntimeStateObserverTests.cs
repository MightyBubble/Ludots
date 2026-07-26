using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeStateObserverTests
{
    private static readonly ContentFingerprint Fingerprint =
        ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1 });

    [Test]
    public void ClientAdmission_DoesNotRegressOrRepublishSemanticDuplicates()
    {
        var observer = CreateObserver();
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome scheduled = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.NetworkIntake, NetworkCommandAdmissionCode.NetworkScheduled);
        NetworkCommandAdmissionOutcome global = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.GlobalIntake, NetworkCommandAdmissionCode.Queued);
        NetworkCommandAdmissionOutcome waiting = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.Queued);
        NetworkCommandAdmissionOutcome terminal = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.Activated);

        observer.OnClientAdmission(in scheduled);
        observer.OnClientAdmission(in global);
        observer.OnClientAdmission(in waiting);
        observer.OnClientAdmission(in terminal);
        Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(4));

        NetworkCommandAdmissionOutcome replayGlobal = global.AsReplay();
        NetworkCommandAdmissionOutcome replayTerminal = terminal.AsReplay();
        observer.OnClientAdmission(in replayGlobal);
        observer.OnClientAdmission(in replayTerminal);
        observer.OnClientAdmission(in waiting);
        observer.OnClientAdmission(in scheduled);

        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(4));
            Assert.That(observer.LastClientAdmission.Stage, Is.EqualTo(NetworkCommandAdmissionStage.EntityIntake));
            Assert.That(observer.LastClientAdmission.Result, Is.EqualTo(NetworkCommandAdmissionCode.Activated));
        });
    }

    [Test]
    public void ClientAdmission_SameFrameProgress_RemainsAvailableAsOrderedFixedHistory()
    {
        var observer = CreateObserver();
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome scheduled = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.NetworkIntake, NetworkCommandAdmissionCode.NetworkScheduled);
        NetworkCommandAdmissionOutcome global = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.GlobalIntake, NetworkCommandAdmissionCode.Queued);
        NetworkCommandAdmissionOutcome queued = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.Queued);
        NetworkCommandAdmissionOutcome activated = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.Activated);

        observer.OnClientAdmission(in scheduled);
        observer.OnClientAdmission(in global);
        observer.OnClientAdmission(in queued);
        observer.OnClientAdmission(in activated);

        var progress = new NetworkCommandAdmissionOutcome[
            observer.ClientAdmissionProgressCapacityPerBatch];
        Assert.That(
            observer.TryCopyClientAdmissionProgress(1, progress, out int count),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(4));
            Assert.That(progress[0].Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(progress[1].Stage, Is.EqualTo(NetworkCommandAdmissionStage.GlobalIntake));
            Assert.That(progress[2].Result, Is.EqualTo(NetworkCommandAdmissionCode.Queued));
            Assert.That(progress[3].Result, Is.EqualTo(NetworkCommandAdmissionCode.Activated));
        });
    }

    [Test]
    public void ClientAdmission_CanObserveMoreThanLegacyBufferCapacityWithoutFault()
    {
        var observer = CreateObserver(historyCapacity: 8);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        for (ulong sequence = 1; sequence <= 512; sequence++)
        {
            NetworkCommandAdmissionOutcome outcome = Outcome(
                in seat,
                sequence,
                NetworkCommandAdmissionStage.EntityIntake,
                NetworkCommandAdmissionCode.Activated);
            observer.OnClientAdmission(in outcome);
        }

        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(512));
            Assert.That(observer.LastClientAdmission.ClientBatchSequence, Is.EqualTo(512));
            Assert.That(observer.ClientAdmissionHistoryEvictionCount, Is.EqualTo(504));
            Assert.That(observer.FaultCount, Is.Zero);
        });
    }

    [Test]
    public void ClientAdmission_ExplicitRejectionSupersedesEarlierSuccessForSameBatch()
    {
        var observer = CreateObserver();
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome activated = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.Activated);
        NetworkCommandAdmissionOutcome rejected = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.NetworkIntake, NetworkCommandAdmissionCode.NetworkActorNotControlled);

        observer.OnClientAdmission(in activated);
        observer.OnClientAdmission(in rejected);

        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(2));
            Assert.That(observer.LastClientAdmission.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkActorNotControlled));
        });
    }

    [Test]
    public void ClientAdmission_PreservesIndependentProgressForMultipleBatches()
    {
        var observer = CreateObserver();
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome firstScheduled = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.NetworkIntake, NetworkCommandAdmissionCode.NetworkScheduled);
        NetworkCommandAdmissionOutcome secondScheduled = Outcome(
            in seat, sequence: 2, NetworkCommandAdmissionStage.NetworkIntake, NetworkCommandAdmissionCode.NetworkScheduled);
        NetworkCommandAdmissionOutcome firstRejected = Outcome(
            in seat, sequence: 1, NetworkCommandAdmissionStage.EntityIntake, NetworkCommandAdmissionCode.RejectedValidation);

        observer.OnClientAdmission(in firstScheduled);
        observer.OnClientAdmission(in secondScheduled);
        observer.OnClientAdmission(in firstRejected);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(1, out NetworkCommandAdmissionOutcome first), Is.True);
            Assert.That(first.Result, Is.EqualTo(NetworkCommandAdmissionCode.RejectedValidation));
            Assert.That(observer.TryGetClientAdmission(2, out NetworkCommandAdmissionOutcome second), Is.True);
            Assert.That(second.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(observer.LastClientAdmission.ClientBatchSequence, Is.EqualTo(2));
            Assert.That(observer.ClientAdmissionHistoryMissCount, Is.Zero);
        });
    }

    [Test]
    public void ClientAdmission_PreservesMixedActorOutcomesAndSummarizesRejection()
    {
        var observer = CreateObserver(maxActorsPerBatch: 2);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome firstActivated = Outcome(
            in seat,
            sequence: 7,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.Activated,
            actorCount: 2,
            admissionBatchIndex: 0);
        NetworkCommandAdmissionOutcome secondRejected = Outcome(
            in seat,
            sequence: 7,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.RejectedInvalidActor,
            actorCount: 2,
            admissionBatchIndex: 1);

        observer.OnClientAdmission(in firstActivated);
        observer.OnClientAdmission(in secondRejected);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientActorAdmission(7, 0, out NetworkCommandAdmissionOutcome first), Is.True);
            Assert.That(first.Result, Is.EqualTo(NetworkCommandAdmissionCode.Activated));
            Assert.That(observer.TryGetClientActorAdmission(7, 1, out NetworkCommandAdmissionOutcome second), Is.True);
            Assert.That(second.Result, Is.EqualTo(NetworkCommandAdmissionCode.RejectedInvalidActor));
            Assert.That(observer.TryGetClientAdmission(7, out NetworkCommandAdmissionOutcome summary), Is.True);
            Assert.That(summary.Result, Is.EqualTo(NetworkCommandAdmissionCode.RejectedInvalidActor));
        });
    }

    [Test]
    public void ClientHandshake_WhenSessionEpochChanges_ClearsAdmissionSessionState()
    {
        var observer = CreateObserver(historyCapacity: 1, maxActorsPerBatch: 2);
        SessionHandshakeResponse firstHandshake = AcceptedHandshake(sessionEpoch: 10);
        observer.OnClientHandshake(in firstHandshake);

        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome first = Outcome(
            in seat,
            sequence: 8,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.Activated,
            actorCount: 2,
            admissionBatchIndex: 0);
        NetworkCommandAdmissionOutcome second = Outcome(
            in seat,
            sequence: 9,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.RejectedInvalidActor,
            actorCount: 2,
            admissionBatchIndex: 1);
        NetworkCommandAdmissionOutcome stale = Outcome(
            in seat,
            sequence: 7,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkScheduled);
        observer.OnClientAdmission(in first);
        observer.OnClientAdmission(in second);
        observer.OnClientAdmission(in stale);
        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientAdmissionHistoryEvictionCount, Is.EqualTo(1));
            Assert.That(observer.ClientAdmissionHistoryMissCount, Is.EqualTo(1));
        });

        SessionHandshakeResponse changedEpoch = SessionHandshakeResponse.Reject(
            HandshakeRejectReason.SessionEpochMismatch,
            new ProtocolVersion(1, 0),
            Fingerprint,
            new SessionEpoch(11));
        observer.OnClientHandshake(in changedEpoch);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(9, out _), Is.False);
            Assert.That(observer.TryGetClientActorAdmission(9, 1, out _), Is.False);
            Assert.That(observer.LastClientAdmission, Is.EqualTo(default(NetworkCommandAdmissionOutcome)));
            Assert.That(observer.ClientAdmissionRevision, Is.Zero);
            Assert.That(observer.ClientAdmissionHistoryEvictionCount, Is.Zero);
            Assert.That(observer.ClientAdmissionHistoryMissCount, Is.Zero);
        });

        NetworkCommandAdmissionOutcome newSessionFirst = Outcome(
            in seat,
            sequence: 1,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkScheduled);
        observer.OnClientAdmission(in newSessionFirst);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(1, out NetworkCommandAdmissionOutcome observed), Is.True);
            Assert.That(observed.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(observer.LastClientAdmission.ClientBatchSequence, Is.EqualTo(1));
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(1));
            Assert.That(observer.ClientAdmissionHistoryMissCount, Is.Zero);
        });
    }

    [Test]
    public void ClientHandshake_WhenSessionEpochChanges_DoesNotReuseSameSequenceAdmission()
    {
        var observer = CreateObserver();
        SessionHandshakeResponse firstHandshake = AcceptedHandshake(sessionEpoch: 10);
        observer.OnClientHandshake(in firstHandshake);

        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome oldSessionRejection = Outcome(
            in seat,
            sequence: 1,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkActorNotControlled);
        observer.OnClientAdmission(in oldSessionRejection);

        SessionHandshakeResponse nextHandshake = AcceptedHandshake(sessionEpoch: 11);
        observer.OnClientHandshake(in nextHandshake);
        NetworkCommandAdmissionOutcome newSessionAdmission = Outcome(
            in seat,
            sequence: 1,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkScheduled);
        observer.OnClientAdmission(in newSessionAdmission);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(1, out NetworkCommandAdmissionOutcome observed), Is.True);
            Assert.That(observed.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(observer.LastClientAdmission.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClientHandshake_WhenSessionEpochIsUnchanged_PreservesAdmissionHistory()
    {
        var observer = CreateObserver();
        SessionHandshakeResponse initialHandshake = AcceptedHandshake(sessionEpoch: 10);
        observer.OnClientHandshake(in initialHandshake);

        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome admission = Outcome(
            in seat,
            sequence: 3,
            NetworkCommandAdmissionStage.EntityIntake,
            NetworkCommandAdmissionCode.Activated);
        observer.OnClientAdmission(in admission);

        SessionHandshakeResponse reconnectHandshake = AcceptedHandshake(sessionEpoch: 10);
        observer.OnClientHandshake(in reconnectHandshake);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(3, out NetworkCommandAdmissionOutcome observed), Is.True);
            Assert.That(observed.Result, Is.EqualTo(NetworkCommandAdmissionCode.Activated));
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClientHandshake_WhenSeatGenerationChanges_DoesNotReusePriorCommandAdmission()
    {
        var observer = CreateObserver();
        SessionHandshakeResponse firstHandshake = AcceptedHandshake(sessionEpoch: 10, seatGeneration: 1);
        observer.OnClientHandshake(in firstHandshake);

        var firstSeat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        NetworkCommandAdmissionOutcome oldGenerationRejection = Outcome(
            in firstSeat,
            sequence: 1,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkActorNotControlled);
        observer.OnClientAdmission(in oldGenerationRejection);

        SessionHandshakeResponse nextGenerationHandshake = AcceptedHandshake(sessionEpoch: 10, seatGeneration: 2);
        observer.OnClientHandshake(in nextGenerationHandshake);
        var nextSeat = new NetworkCommandSeat(slot: 0, generation: 2, playerId: 1);
        NetworkCommandAdmissionOutcome nextGenerationAdmission = Outcome(
            in nextSeat,
            sequence: 1,
            NetworkCommandAdmissionStage.NetworkIntake,
            NetworkCommandAdmissionCode.NetworkScheduled);
        observer.OnClientAdmission(in nextGenerationAdmission);

        Assert.Multiple(() =>
        {
            Assert.That(observer.TryGetClientAdmission(1, out NetworkCommandAdmissionOutcome observed), Is.True);
            Assert.That(observed.SeatGeneration, Is.EqualTo(2));
            Assert.That(observed.Result, Is.EqualTo(NetworkCommandAdmissionCode.NetworkScheduled));
            Assert.That(observer.ClientAdmissionRevision, Is.EqualTo(1));
        });
    }

    private static NetworkRuntimeStateObserver CreateObserver(
        int historyCapacity = 8,
        int maxActorsPerBatch = 4) =>
        new(
            seatCapacity: 1,
            clientAdmissionHistoryCapacity: historyCapacity,
            maxActorsPerCommandBatch: maxActorsPerBatch);

    private static NetworkCommandAdmissionOutcome Outcome(
        in NetworkCommandSeat seat,
        ulong sequence,
        NetworkCommandAdmissionStage stage,
        NetworkCommandAdmissionCode code,
        int actorCount = 1,
        ushort admissionBatchIndex = 0) =>
        new(
            in seat,
            sequence,
            targetTick: 10,
            actorCount,
            orderId: 20,
            admissionBatchId: 30,
            admissionBatchIndex,
            stage,
            code,
            isReplay: false,
            committedTick: 10);

    private static SessionHandshakeResponse AcceptedHandshake(ulong sessionEpoch, uint seatGeneration = 1)
    {
        var seat = new SessionSeatBinding(0, seatGeneration, new PlayerId(1));
        return SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(1, 2),
            new ProtocolVersion(1, 0),
            Fingerprint,
            new SessionEpoch(sessionEpoch),
            nextClientBatchSequence: 1);
    }
}
