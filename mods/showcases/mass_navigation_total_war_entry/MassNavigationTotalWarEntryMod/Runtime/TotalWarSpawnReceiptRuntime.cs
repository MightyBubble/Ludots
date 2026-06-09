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
    private readonly SoldierSpawnReceiptPayload? _soldierPayload;
    private readonly FormationAgentSpawnReceiptPayload? _formationAgentPayload;
    private readonly ObstacleOverlaySpawnReceiptPayload? _obstacleOverlayPayload;

    private TotalWarSpawnReceiptBinding(
        TotalWarSpawnReceiptKind kind,
        SoldierSpawnReceiptPayload? soldierPayload,
        FormationAgentSpawnReceiptPayload? formationAgentPayload,
        ObstacleOverlaySpawnReceiptPayload? obstacleOverlayPayload,
        string templateId)
    {
        Kind = kind;
        _soldierPayload = soldierPayload;
        _formationAgentPayload = formationAgentPayload;
        _obstacleOverlayPayload = obstacleOverlayPayload;
        TemplateId = templateId;
    }

    public TotalWarSpawnReceiptKind Kind { get; }
    public int MassNavAgentIndex => Kind switch
    {
        TotalWarSpawnReceiptKind.Soldier => RequireSoldierPayload().MassNavAgentIndex,
        TotalWarSpawnReceiptKind.FormationAgent => RequireFormationAgentPayload().MassNavAgentIndex,
        _ => throw InvalidMemberForKind(nameof(MassNavAgentIndex)),
    };

    public int FormationIndex => Kind switch
    {
        TotalWarSpawnReceiptKind.Soldier => RequireSoldierPayload().FormationIndex,
        TotalWarSpawnReceiptKind.FormationAgent => RequireFormationAgentPayload().FormationIndex,
        _ => throw InvalidMemberForKind(nameof(FormationIndex)),
    };

    public int SlotIndex
    {
        get
        {
            RequireKind(TotalWarSpawnReceiptKind.Soldier, nameof(SlotIndex));
            return RequireSoldierPayload().SlotIndex;
        }
    }

    public float ObstacleRadiusCm
    {
        get
        {
            RequireKind(TotalWarSpawnReceiptKind.ObstacleOverlay, nameof(ObstacleRadiusCm));
            return RequireObstacleOverlayPayload().ObstacleRadiusCm;
        }
    }

    public string TemplateId { get; }

    public static TotalWarSpawnReceiptBinding ForSoldier(
        int massNavAgentIndex,
        int formationIndex,
        int slotIndex,
        string templateId)
    {
        ValidateAgentPayload(
            massNavAgentIndex,
            formationIndex,
            templateId,
            "soldier");
        if (slotIndex < 0)
        {
            throw new System.InvalidOperationException("Total War soldier spawn receipt requires non-negative slotIndex.");
        }

        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.Soldier,
            new SoldierSpawnReceiptPayload(
                massNavAgentIndex,
                formationIndex,
                slotIndex),
            formationAgentPayload: null,
            obstacleOverlayPayload: null,
            templateId);
    }

    public static TotalWarSpawnReceiptBinding ForFormationAgent(
        int massNavAgentIndex,
        int formationIndex,
        string templateId)
    {
        ValidateAgentPayload(
            massNavAgentIndex,
            formationIndex,
            templateId,
            "formation agent");
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.FormationAgent,
            soldierPayload: null,
            new FormationAgentSpawnReceiptPayload(
                massNavAgentIndex,
                formationIndex),
            obstacleOverlayPayload: null,
            templateId);
    }

    public static TotalWarSpawnReceiptBinding ForObstacleOverlay(
        float obstacleRadiusCm,
        string templateId)
    {
        if (!(obstacleRadiusCm > 0f))
        {
            throw new System.InvalidOperationException("Total War obstacle overlay spawn receipt requires obstacleRadiusCm > 0.");
        }

        RequireTemplateId(templateId);
        return new TotalWarSpawnReceiptBinding(
            TotalWarSpawnReceiptKind.ObstacleOverlay,
            soldierPayload: null,
            formationAgentPayload: null,
            new ObstacleOverlaySpawnReceiptPayload(obstacleRadiusCm),
            templateId);
    }

    private static void ValidateAgentPayload(
        int massNavAgentIndex,
        int formationIndex,
        string templateId,
        string label)
    {
        if (massNavAgentIndex < 0)
        {
            throw new System.InvalidOperationException($"Total War {label} spawn receipt requires non-negative massNavAgentIndex.");
        }

        if (formationIndex < 0)
        {
            throw new System.InvalidOperationException($"Total War {label} spawn receipt requires non-negative formationIndex.");
        }

        RequireTemplateId(templateId);
    }

    private void RequireKind(TotalWarSpawnReceiptKind expected, string memberName)
    {
        if (Kind != expected)
        {
            throw InvalidMemberForKind(memberName);
        }
    }

    private System.InvalidOperationException InvalidMemberForKind(string memberName)
    {
        return new System.InvalidOperationException(
            $"Total War spawn receipt member {memberName} is not valid for {Kind} receipts.");
    }

    private static void RequireTemplateId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new System.InvalidOperationException("Total War spawn receipt requires a non-empty templateId.");
        }
    }

    private SoldierSpawnReceiptPayload RequireSoldierPayload()
    {
        return _soldierPayload
            ?? throw new System.InvalidOperationException("Total War soldier spawn receipt requires a soldier payload.");
    }

    private FormationAgentSpawnReceiptPayload RequireFormationAgentPayload()
    {
        return _formationAgentPayload
            ?? throw new System.InvalidOperationException("Total War formation agent spawn receipt requires a formation agent payload.");
    }

    private ObstacleOverlaySpawnReceiptPayload RequireObstacleOverlayPayload()
    {
        return _obstacleOverlayPayload
            ?? throw new System.InvalidOperationException("Total War obstacle overlay spawn receipt requires an obstacle overlay payload.");
    }

    private readonly record struct SoldierSpawnReceiptPayload(
        int MassNavAgentIndex,
        int FormationIndex,
        int SlotIndex);

    private readonly record struct FormationAgentSpawnReceiptPayload(
        int MassNavAgentIndex,
        int FormationIndex);

    private readonly record struct ObstacleOverlaySpawnReceiptPayload(float ObstacleRadiusCm);
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
