using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphValueType : byte
    {
        Void = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
        Entity = 4,
        TargetList = 5
    }

    public enum GraphNodeOp : ushort
    {
        None = 0,
        ConstBool = 1,
        ConstInt = 2,
        ConstFloat = 3,
        LoadCaster = 4,
        LoadExplicitTarget = 5,
        Jump = 6,
        JumpIfFalse = 7,
        LoadAttribute = 10,
        AddFloat = 20,
        MulFloat = 21,
        SubFloat = 22,
        DivFloat = 23,   // div-by-zero → 0
        MinFloat = 24,
        MaxFloat = 25,
        ClampFloat = 26, // clamp(F[a], F[b], F[c]) → min=F[b], max=F[c]
        AbsFloat = 27,
        NegFloat = 28,
        // ── Int Math (29, 31-33) ──
        AddInt            = 29,   // I[Dst] = I[A] + I[B]
        CompareGtFloat = 30,
        CompareLtInt      = 31,   // B[Dst] = I[A] < I[B] ? 1 : 0
        CompareEqInt      = 32,   // B[Dst] = I[A] == I[B] ? 1 : 0
        HasTag            = 33,   // B[Dst] = E[A].HasTag(Imm) ? 1 : 0

        SelectEntity = 40,
        QueryRadius = 100,
        QueryFilterTagAll = 101,
        QuerySortStable = 102,
        QueryLimit = 103,
        QueryCone = 104,
        QueryRectangle = 105,
        QueryLine = 106,
        // 110 removed (was QueryFilterTeam — use QueryFilterRelationship instead)
        QueryFilterNotEntity = 111,
        QueryFilterLayer = 112,
        QueryFilterRelationship = 113,

        // ── TargetList iteration / aggregation (120-123) ──
        AggCount = 120,
        AggMinByDistance = 121,
        TargetListGet     = 123,  // E[Dst] = TargetList[I[A]]; B[Flags] = valid (0/1)

        // ── Hex spatial queries (130-132) ──
        QueryHexRange     = 130,  // TargetList = HexRange(TargetPos, Imm=radius)
        QueryHexRing      = 131,  // TargetList = HexRing(TargetPos, Imm=radius)
        QueryHexNeighbors = 132,  // TargetList = Hex6Neighbors(TargetPos)

        // ── Effect / Event Actions ──
        ApplyEffectTemplate = 200,
        FanOutApplyEffect = 201,            // Apply Effect(Imm=templateId) to ALL entities in TargetList
        ApplyEffectDynamic = 202,           // source=Caster, target=E[A], templateId=I[B]
        FanOutApplyEffectDynamic = 203,     // source=Caster, TargetList, templateId=I[A]
        ModifyAttributeAdd = 210,
        SendEvent = 220,

        // ── Blackboard immediate read/write (300-305) ──
        ReadBlackboardFloat   = 300,  // F[dst] = entity.BB[keyId]
        ReadBlackboardInt     = 301,  // I[dst] = entity.BB[keyId]
        ReadBlackboardEntity  = 302,  // E[dst] = entity.BB[keyId]
        WriteBlackboardFloat  = 303,  // entity.BB[keyId] = F[src] (immediate)
        WriteBlackboardInt    = 304,  // entity.BB[keyId] = I[src]
        WriteBlackboardEntity = 305,  // entity.BB[keyId] = E[src]

        // ── Config parameter reading (310-312) ──
        LoadConfigFloat       = 310,  // F[dst] = EffectTemplate.ConfigParams[keyId]
        LoadConfigInt         = 311,  // I[dst] = EffectTemplate.ConfigParams[keyId]
        LoadConfigEffectId    = 312,  // I[dst] = EffectTemplate.ConfigParams[keyId] (effectTemplateId)

        // ── Context entity loading (320-322) ──
        LoadContextSource        = 320,  // E[dst] = EffectContext.Source
        LoadContextTarget        = 321,  // E[dst] = EffectContext.Target
        LoadContextTargetContext = 322,  // E[dst] = EffectContext.TargetContext

        // ── Self attribute access for derived graphs (330-331) ──
        LoadSelfAttribute        = 330,  // F[dst] = Caster.Attribute[Imm] (no EffectContext needed)
        WriteSelfAttribute       = 331,  // Caster.Attribute[Imm] = F[A] (direct SetCurrent, bypasses modifiers)
    }

    public static class GraphNodeOpParser
    {
        public static bool TryParse(string op, out GraphNodeOp parsed)
        {
            parsed = GraphNodeOp.None;
            if (string.IsNullOrWhiteSpace(op)) return false;

            if (Enum.TryParse(op, ignoreCase: true, out GraphNodeOp v))
            {
                parsed = v;
                return true;
            }

            return false;
        }
    }
}

