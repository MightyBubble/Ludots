using System;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

internal enum MassNavSpawnReceiptKind : byte
{
    Agent = 1,
    Blocker = 2,
    WorldMarker = 3,
}

internal readonly struct MassNavSpawnReceiptBinding
{
    public MassNavSpawnReceiptBinding(
        MassNavSpawnReceiptKind kind,
        int unitIndex,
        int expectedTeamId,
        bool heavy,
        float navMass,
        float visualScale,
        float blockerRadiusCm,
        string templateId)
    {
        Kind = kind;
        UnitIndex = unitIndex;
        ExpectedTeamId = expectedTeamId;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
        BlockerRadiusCm = blockerRadiusCm;
        TemplateId = templateId ?? string.Empty;
    }

    public MassNavSpawnReceiptKind Kind { get; }
    public int UnitIndex { get; }
    public int ExpectedTeamId { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
    public float BlockerRadiusCm { get; }
    public string TemplateId { get; }
}

internal sealed class MassNavSpawnReceiptRuntime
{
    private readonly System.Collections.Generic.Dictionary<int, MassNavSpawnReceiptBinding> _pendingByReceiptId = new();
    private int _nextReceiptId = 1;

    public int PendingCount => _pendingByReceiptId.Count;

    public int Allocate(in MassNavSpawnReceiptBinding binding)
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

    public bool TryConsume(int receiptId, out MassNavSpawnReceiptBinding binding)
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
