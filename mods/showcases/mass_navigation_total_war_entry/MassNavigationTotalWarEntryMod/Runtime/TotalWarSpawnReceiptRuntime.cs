using System.Collections.Generic;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal enum TotalWarSpawnReceiptKind : byte
{
    Soldier = 1,
    FormationAgent = 2,
    ObstacleOverlay = 3,
}

internal readonly struct TotalWarSpawnReceiptBinding
{
    private TotalWarSpawnReceiptBinding(
        TotalWarSpawnReceiptKind kind,
        int massNavAgentIndex,
        int formationIndex,
        int slotIndex,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        float obstacleRadiusCm,
        string templateId)
    {
        Kind = kind;
        MassNavAgentIndex = massNavAgentIndex;
        FormationIndex = formationIndex;
        SlotIndex = slotIndex;
        TeamId = teamId;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
        BodyRadiusCm = bodyRadiusCm;
        SpeedCmPerSecond = speedCmPerSecond;
        ObstacleRadiusCm = obstacleRadiusCm;
        TemplateId = templateId;
    }

    public static TotalWarSpawnReceiptBinding ForSoldier(
        int massNavAgentIndex,
        int formationIndex,
        int slotIndex,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        string templateId)
    {
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.Soldier,
            massNavAgentIndex,
            formationIndex,
            slotIndex,
            teamId,
            heavy,
            navMass,
            visualScale,
            bodyRadiusCm,
            speedCmPerSecond,
            obstacleRadiusCm: 0f,
            templateId);
    }

    public static TotalWarSpawnReceiptBinding ForFormationAgent(
        int massNavAgentIndex,
        int formationIndex,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        string templateId)
    {
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.FormationAgent,
            massNavAgentIndex,
            formationIndex,
            slotIndex: 0,
            teamId,
            heavy,
            navMass,
            visualScale,
            bodyRadiusCm,
            speedCmPerSecond,
            obstacleRadiusCm: 0f,
            templateId);
    }

    public static TotalWarSpawnReceiptBinding ForObstacleOverlay(
        float obstacleRadiusCm,
        string templateId)
    {
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.ObstacleOverlay,
            massNavAgentIndex: 0,
            formationIndex: 0,
            slotIndex: 0,
            teamId: 0,
            heavy: false,
            navMass: 0f,
            visualScale: 0f,
            bodyRadiusCm: 0f,
            speedCmPerSecond: 0f,
            obstacleRadiusCm,
            templateId);
    }

    public TotalWarSpawnReceiptKind Kind { get; }
    public int MassNavAgentIndex { get; }
    public int FormationIndex { get; }
    public int SlotIndex { get; }
    public int TeamId { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
    public float BodyRadiusCm { get; }
    public float SpeedCmPerSecond { get; }
    public float ObstacleRadiusCm { get; }
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
