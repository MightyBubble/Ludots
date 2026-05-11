using System.Collections.Generic;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal enum TotalWarSpawnReceiptKind : byte
{
    Soldier = 1,
    FormationAnchor = 2,
}

internal readonly struct TotalWarSpawnReceiptBinding
{
    private TotalWarSpawnReceiptBinding(
        TotalWarSpawnReceiptKind kind,
        int unitIndex,
        int formationIndex,
        int slotIndex,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        string templateId)
    {
        Kind = kind;
        UnitIndex = unitIndex;
        FormationIndex = formationIndex;
        SlotIndex = slotIndex;
        TeamId = teamId;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
        TemplateId = templateId;
    }

    public static TotalWarSpawnReceiptBinding ForSoldier(
        int unitIndex,
        int formationIndex,
        int slotIndex,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        string templateId)
    {
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.Soldier,
            unitIndex,
            formationIndex,
            slotIndex,
            teamId,
            heavy,
            navMass,
            visualScale,
            templateId);
    }

    public static TotalWarSpawnReceiptBinding ForFormationAnchor(
        int formationIndex,
        int teamId,
        string templateId)
    {
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.FormationAnchor,
            unitIndex: 0,
            formationIndex,
            slotIndex: 0,
            teamId,
            heavy: false,
            navMass: 0f,
            visualScale: 0f,
            templateId);
    }

    public TotalWarSpawnReceiptKind Kind { get; }
    public int UnitIndex { get; }
    public int FormationIndex { get; }
    public int SlotIndex { get; }
    public int TeamId { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
    public string TemplateId { get; }
}

internal sealed class TotalWarSpawnReceiptRuntime
{
    private readonly Dictionary<int, TotalWarSpawnReceiptBinding> _pendingByReceiptId = new();
    private int _nextReceiptId = 1;

    public int PendingCount => _pendingByReceiptId.Count;

    public int Allocate(in TotalWarSpawnReceiptBinding binding)
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

    public bool TryConsume(int receiptId, out TotalWarSpawnReceiptBinding binding)
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
