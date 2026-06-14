using System;
using Arch.Core;

namespace MassNavigationMod.Runtime;

internal enum MassNavigationSpawnReceiptKind : byte
{
    Agent = 1,
    Blocker = 2,
    WorldMarker = 3,
}

internal readonly struct MassNavigationSpawnReceiptBinding
{
    private readonly AgentSpawnReceiptPayload? _agentPayload;
    private readonly BlockerSpawnReceiptPayload? _blockerPayload;

    private MassNavigationSpawnReceiptBinding(
        MassNavigationSpawnReceiptKind kind,
        AgentSpawnReceiptPayload? agentPayload,
        BlockerSpawnReceiptPayload? blockerPayload,
        string templateId)
    {
        Kind = kind;
        _agentPayload = agentPayload;
        _blockerPayload = blockerPayload;
        TemplateId = templateId;
    }

    public MassNavigationSpawnReceiptKind Kind { get; }
    public int AgentIndex
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(AgentIndex));
            return RequireAgentPayload().AgentIndex;
        }
    }

    public int ExpectedTeamId
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(ExpectedTeamId));
            return RequireAgentPayload().ExpectedTeamId;
        }
    }

    public bool Heavy
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(Heavy));
            return RequireAgentPayload().Heavy;
        }
    }

    public float NavMass
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(NavMass));
            return RequireAgentPayload().NavMass;
        }
    }

    public float VisualScale
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(VisualScale));
            return RequireAgentPayload().VisualScale;
        }
    }

    public float BodyRadiusCm
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(BodyRadiusCm));
            return RequireAgentPayload().BodyRadiusCm;
        }
    }

    public float SpeedCmPerSecond
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Agent, nameof(SpeedCmPerSecond));
            return RequireAgentPayload().SpeedCmPerSecond;
        }
    }

    public float BlockerRadiusCm
    {
        get
        {
            RequireKind(MassNavigationSpawnReceiptKind.Blocker, nameof(BlockerRadiusCm));
            return RequireBlockerPayload().BlockerRadiusCm;
        }
    }

    public string TemplateId { get; }

    public static MassNavigationSpawnReceiptBinding ForAgent(
        int agentIndex,
        int expectedTeamId,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        string templateId)
    {
        if (agentIndex < 0)
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires non-negative agentIndex.");
        }

        if (expectedTeamId <= 0)
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires expectedTeamId > 0.");
        }

        if (!(navMass > 0f))
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires navMass > 0.");
        }

        if (!(visualScale > 0f))
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires visualScale > 0.");
        }

        if (!(bodyRadiusCm > 0f))
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires bodyRadiusCm > 0.");
        }

        if (!(speedCmPerSecond > 0f))
        {
            throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires speedCmPerSecond > 0.");
        }

        RequireTemplateId(templateId);
        return new MassNavigationSpawnReceiptBinding(
            MassNavigationSpawnReceiptKind.Agent,
            new AgentSpawnReceiptPayload(agentIndex, expectedTeamId, heavy, navMass, visualScale, bodyRadiusCm, speedCmPerSecond),
            blockerPayload: null,
            templateId);
    }

    public static MassNavigationSpawnReceiptBinding ForBlocker(float blockerRadiusCm, string templateId)
    {
        if (!(blockerRadiusCm > 0f))
        {
            throw new InvalidOperationException("MassNavigationMod blocker spawn receipt requires blockerRadiusCm > 0.");
        }

        RequireTemplateId(templateId);
        return new MassNavigationSpawnReceiptBinding(
            MassNavigationSpawnReceiptKind.Blocker,
            agentPayload: null,
            new BlockerSpawnReceiptPayload(blockerRadiusCm),
            templateId);
    }

    public static MassNavigationSpawnReceiptBinding ForWorldMarker(string templateId)
    {
        RequireTemplateId(templateId);
        return new MassNavigationSpawnReceiptBinding(
            MassNavigationSpawnReceiptKind.WorldMarker,
            agentPayload: null,
            blockerPayload: null,
            templateId);
    }

    private void RequireKind(MassNavigationSpawnReceiptKind expected, string memberName)
    {
        if (Kind != expected)
        {
            throw new InvalidOperationException(
                $"MassNavigationMod spawn receipt member {memberName} is only valid for {expected} receipts; actual kind is {Kind}.");
        }
    }

    private static void RequireTemplateId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("MassNavigationMod spawn receipt requires a non-empty templateId.");
        }
    }

    private AgentSpawnReceiptPayload RequireAgentPayload()
    {
        return _agentPayload
            ?? throw new InvalidOperationException("MassNavigationMod agent spawn receipt requires an agent payload.");
    }

    private BlockerSpawnReceiptPayload RequireBlockerPayload()
    {
        return _blockerPayload
            ?? throw new InvalidOperationException("MassNavigationMod blocker spawn receipt requires a blocker payload.");
    }

    private readonly record struct AgentSpawnReceiptPayload(
        int AgentIndex,
        int ExpectedTeamId,
        bool Heavy,
        float NavMass,
        float VisualScale,
        float BodyRadiusCm,
        float SpeedCmPerSecond);

    private readonly record struct BlockerSpawnReceiptPayload(float BlockerRadiusCm);
}

internal sealed class MassNavigationSpawnReceiptRuntime
{
    private readonly System.Collections.Generic.Dictionary<int, MassNavigationSpawnReceiptBinding> _pendingByReceiptId = new();
    private int _nextReceiptId = 1;

    public int PendingCount => _pendingByReceiptId.Count;

    public int Allocate(in MassNavigationSpawnReceiptBinding binding)
    {
        int receiptId = _nextReceiptId++;
        if (receiptId <= 0)
        {
            _nextReceiptId = 1;
            receiptId = _nextReceiptId++;
        }

        _pendingByReceiptId.Add(receiptId, binding);
        return receiptId;
    }

    public bool TryConsume(int receiptId, out MassNavigationSpawnReceiptBinding binding)
    {
        if (!_pendingByReceiptId.TryGetValue(receiptId, out binding))
        {
            return false;
        }

        _pendingByReceiptId.Remove(receiptId);
        return true;
    }

    public void Reset()
    {
        _pendingByReceiptId.Clear();
        _nextReceiptId = 1;
    }
}

