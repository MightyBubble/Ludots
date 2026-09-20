using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Input.AimSource;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;


namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class LinearNodeDriver : IGraphOpsNodeDriver
{
    private const string GraphSettled = "graphSettled";
    private const float PinnedPointerPx = 42f;

    // Settle bench scale: the 4.8m track is the same 100-point ruler as the health bar.
    private const float TrackMeters = 4.8f;
    private const float TrackPoints = 100f;
    private const float TrackY = 1.6f;
    private const float MetersPerPoint = TrackMeters / TrackPoints;

    // RandomFloat01 shows a 0-30 damage dial and a stacked roll history (last 6 beats).
    private const float RandomScale = 30f;
    private const int RollHistoryCapacity = 6;

    private readonly float[] _rollHistory = new float[RollHistoryCapacity];
    private int _rollCount;
    private float _lastRoll;
    private bool _lastBool;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (IsLivePointerOp(ctx.Vignette.Op))
        {
            SeedPinnedLivePointer(ctx);
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    private static bool IsLivePointerOp(string op) =>
        string.Equals(op, nameof(GraphNodeOp.LoadPointerScreenX), StringComparison.Ordinal)
        || string.Equals(op, nameof(GraphNodeOp.LoadPointerScreenY), StringComparison.Ordinal);

    private static void SeedPinnedLivePointer(GraphOpsNodeDriverContext ctx)
    {
        var globals = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = new FixedPointerActionReader(
                new Vector2(PinnedPointerPx, PinnedPointerPx)),
            [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
        };
        ctx.Api.BindAimSource(new GraphAimSourceRuntime(ctx.SimWorld, globals));
    }

    private sealed class FixedPointerActionReader : IInputActionReader
    {
        private readonly Vector2 _pointer;

        public FixedPointerActionReader(Vector2 pointer) => _pointer = pointer;

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (string.Equals(actionId, InteractionActionBindings.DefaultPointerPositionActionId, StringComparison.Ordinal)
                && typeof(T) == typeof(Vector2))
            {
                return (T)(object)_pointer;
            }

            return default;
        }

        public bool IsDown(string actionId) => false;
        public bool PressedThisFrame(string actionId) => false;
        public bool ReleasedThisFrame(string actionId) => false;
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeLinearOptions linear = ctx.Vignette.Linear
            ?? throw new InvalidOperationException($"Linear vignette {ctx.Vignette.Op} requires a linear block.");

        bool settledByGraph = IsGraphSettled(linear);
        if (settledByGraph)
        {
            GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
        }

        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        string resultText = FormatResult(linear.ResultKind, result);
        ApplyResult(ctx, linear, result, targetIndex);
        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        if (settledByGraph && targetIndex >= 0 && healthAfter >= healthBefore)
        {
            throw new InvalidOperationException(
                $"Linear apply '{GraphSettled}' expected {ctx.Vignette.Op} to settle real damage on the target via its graph tail.");
        }

        CaptureBeatState(ctx, result);
        ctx.CaptionValues["result"] = resultText;
        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (caster < 0 || target < 0)
        {
            return;
        }

        if (ctx.Vignette.Linear == null || !IsGraphSettled(ctx.Vignette.Linear))
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                ctx.Vignette.Actors[caster].X,
                ctx.Vignette.Actors[caster].Y,
                ctx.Vignette.Actors[target].X,
                ctx.Vignette.Actors[target].Y);
            return;
        }

        switch (ctx.Vignette.Op)
        {
            case "MulFloat":
                DrawSettleBench(ctx, debugDraw, caster, target);
                break;
            case "MaxFloat":
            case "MinFloat":
                DrawPickBench(ctx, debugDraw, caster, target);
                break;
            case "AddFloat":
                DrawJoinBench(ctx, debugDraw, caster, target);
                break;
            case "ClampFloat":
                DrawWallBench(ctx, debugDraw, caster, target);
                break;
            case "ConstFloat":
                DrawPlinthBench(ctx, debugDraw, caster, target);
                break;
            case "NegFloat":
                DrawZeroAxisBench(ctx, debugDraw, caster, target, fold: false);
                break;
            case "AbsFloat":
                DrawZeroAxisBench(ctx, debugDraw, caster, target, fold: true);
                break;
            case "SubFloat":
                DrawBlockBench(ctx, debugDraw, caster, target);
                break;
            case "DivFloat":
                DrawSplitBench(ctx, debugDraw, caster, target);
                break;
            case "RandomFloat01":
                DrawDiceBench(ctx, debugDraw, caster, target);
                break;
            case "ConstBool":
                DrawGateBench(ctx, debugDraw, caster, target);
                break;
            case "CompareGtFloat":
                DrawCompareBench(ctx, debugDraw, caster, target);
                break;
        }
    }

    private void CaptureBeatState(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        if (string.Equals(ctx.Vignette.Op, "RandomFloat01", StringComparison.Ordinal))
        {
            _lastRoll = result.FloatValue;
            _rollHistory[_rollCount % RollHistoryCapacity] = result.FloatValue;
            _rollCount++;
            return;
        }

        if (string.Equals(ctx.Vignette.Op, "ConstBool", StringComparison.Ordinal) ||
            string.Equals(ctx.Vignette.Op, "CompareGtFloat", StringComparison.Ordinal))
        {
            _lastBool = result.BoolValue;
        }
    }

    private static bool IsGraphSettled(GraphOpsNodeLinearOptions linear)
    {
        return string.Equals(linear.ApplyTo, GraphSettled, StringComparison.Ordinal);
    }

    private static float ReadConstFloats(GraphOpsNodeDriverContext ctx, Span<float> dst)
    {
        int count = 0;
        foreach (GraphInstruction ins in ctx.Compiled.Program)
        {
            if (ins.Op != (ushort)GraphNodeOp.ConstFloat)
            {
                continue;
            }

            // Authored bench operands come first in program order; settle-tail plumbing
            // (e.g. CompareGtFloat's strike feeds) appends its own constants after them.
            if (count >= dst.Length)
            {
                break;
            }

            dst[count++] = ins.ImmF;
        }

        if (count < dst.Length)
        {
            throw new InvalidOperationException(
                $"Linear '{GraphSettled}' bench for {ctx.Vignette.Op} expected {dst.Length} ConstFloat feeds, found {count}.");
        }

        return count;
    }

    /// <summary>Damage bench between caster and target: a 100-point track where the yellow input segment is
    /// stretched by the multiplier badge (15 over 10) into the red result segment aimed at the target.</summary>
    private static void DrawSettleBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[2];
        ReadConstFloats(ctx, consts);
        float input = consts[0];
        float multiplier = consts[1];
        float scaled = input * multiplier;

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float midX = (casterActor.X + targetActor.X) * 0.5f;
        float trackLeft = midX - TrackMeters * 0.5f;
        float inputEnd = trackLeft + input * MetersPerPoint;
        float resultEnd = inputEnd + scaled * MetersPerPoint;

        DrawTrackRail(debugDraw, trackLeft);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, inputEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, inputEnd, TrackY, resultEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, resultEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        DrawMultiplierBadge(debugDraw, midX);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, (trackLeft + inputEnd) * 0.5f + 0.25f, TrackY - 0.5f, (int)input, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, (inputEnd + resultEnd) * 0.5f + 0.3f, TrackY - 0.5f, (int)scaled, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    /// <summary>Ring over the track midpoint plus 15-over-10 fraction glyphs reading as ×1.5.</summary>
    private static void DrawMultiplierBadge(DebugDrawCommandBuffer debugDraw, float midX)
    {
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, midX, TrackY + 0.75f, GraphShowcaseStagePresenter.BadgeKind.Ring, GraphShowcaseStagePresenter.GateColor, scale: 1.2f);

        float numberRightEdge = midX + 1.5f;
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, numberRightEdge, TrackY + 1.25f, 15, 0.5f, GraphShowcaseStagePresenter.CasterColor);
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(midX + 0.75f, TrackY + 0.95f),
            B = new Vector2(numberRightEdge, TrackY + 0.95f),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, numberRightEdge, TrackY + 0.55f, 10, 0.5f, GraphShowcaseStagePresenter.GateColor);
    }

    private static void DrawTrackRail(DebugDrawCommandBuffer debugDraw, float trackLeft)
    {
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(trackLeft, TrackY),
            B = new Vector2(trackLeft + TrackMeters, TrackY),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        for (int p = 10; p < 100; p += 10)
        {
            float x = trackLeft + p * MetersPerPoint;
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(x, TrackY - 0.18f),
                B = new Vector2(x, TrackY + 0.18f),
                Thickness = 0.04f,
                Color = GraphShowcaseStagePresenter.GhostColor
            });
        }
    }

    private static (float MidX, float TrackLeft) BenchGeometry(GraphOpsNodeActor caster, GraphOpsNodeActor target)
    {
        float midX = (caster.X + target.X) * 0.5f;
        return (midX, midX - TrackMeters * 0.5f);
    }

    /// <summary>Candidate blocks stacked on two rows; the picked length turns red and flies, the other stays as a ghost.</summary>
    private static void DrawPickBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[2];
        ReadConstFloats(ctx, consts);
        float a = consts[0];
        float b = consts[1];
        float picked = string.Equals(ctx.Vignette.Op, "MaxFloat", StringComparison.Ordinal)
            ? MathF.Max(a, b)
            : MathF.Min(a, b);

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (float midX, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        float topY = TrackY + 0.6f;
        float bottomY = TrackY - 0.5f;
        DrawCandidateRow(debugDraw, trackLeft, a, topY, a == picked);
        DrawCandidateRow(debugDraw, trackLeft, b, bottomY, b == picked);

        float pickEnd = trackLeft + picked * MetersPerPoint;
        float pickY = a == picked ? topY : bottomY;
        DrawPickArrows(debugDraw, trackLeft, pickY);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, pickEnd, pickY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);
    }

    private static void DrawCandidateRow(DebugDrawCommandBuffer debugDraw, float trackLeft, float length, float y, bool picked)
    {
        float end = trackLeft + length * MetersPerPoint;
        if (picked)
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, trackLeft, y, end, y, 0.22f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, trackLeft, y, end, y, GraphShowcaseStagePresenter.GhostColor);
        }

        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, end + 0.35f, y, (int)length, 0.45f,
            picked ? GraphShowcaseStagePresenter.EnemyColor : GraphShowcaseStagePresenter.GhostColor);
    }

    /// <summary>Two parallel arrows from the track edge pointing at the picked block.</summary>
    private static void DrawPickArrows(DebugDrawCommandBuffer debugDraw, float trackLeft, float pickY)
    {
        float x = trackLeft - 0.55f;
        GraphShowcaseStagePresenter.DrawDirectedLine(debugDraw, x, pickY - 0.28f, x + 0.5f, pickY - 0.28f, 0.08f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(debugDraw, x, pickY + 0.28f, x + 0.5f, pickY + 0.28f, 0.08f, GraphShowcaseStagePresenter.CasterColor);
    }

    /// <summary>Base block then the bonus block docked end-to-end with a seam tick; the joined length flies out.</summary>
    private static void DrawJoinBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[2];
        ReadConstFloats(ctx, consts);
        float baseLen = consts[0];
        float bonus = consts[1];
        float total = baseLen + bonus;

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (_, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        float baseEnd = trackLeft + baseLen * MetersPerPoint;
        float totalEnd = trackLeft + total * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, baseEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, baseEnd, TrackY, totalEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(baseEnd, TrackY - 0.28f),
            B = new Vector2(baseEnd, TrackY + 0.28f),
            Thickness = 0.04f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, totalEnd, TrackY, 0.14f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, totalEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (trackLeft + baseEnd) * 0.5f + 0.25f, TrackY - 0.5f, (int)baseLen, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (baseEnd + totalEnd) * 0.5f + 0.2f, TrackY - 0.5f, (int)bonus, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, totalEnd + 0.4f, TrackY + 0.45f, (int)total, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
    }

    /// <summary>Walls at the clamp bounds; the raw block is shoved left until it stops at the upper wall.</summary>
    private static void DrawWallBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[3];
        ReadConstFloats(ctx, consts);
        float raw = consts[0];
        float lo = consts[1];
        float hi = consts[2];

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (_, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        DrawWall(debugDraw, trackLeft + lo * MetersPerPoint, lo);
        DrawWall(debugDraw, trackLeft + hi * MetersPerPoint, hi);

        float wallX = trackLeft + hi * MetersPerPoint;
        float resultEnd = wallX + hi * MetersPerPoint;
        float rawEnd = wallX + raw * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, wallX, TrackY, rawEnd, TrackY, 0.2f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawGhostSegment(
            debugDraw, resultEnd, TrackY, rawEnd, TrackY, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, wallX, TrackY, resultEnd, TrackY, 0.16f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, resultEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, rawEnd + 0.4f, TrackY - 0.5f, (int)raw, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, resultEnd + 0.4f, TrackY + 0.5f, (int)hi, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    private static void DrawWall(DebugDrawCommandBuffer debugDraw, float x, float value)
    {
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x, TrackY - 1.1f),
            B = new Vector2(x, TrackY + 1.1f),
            Thickness = 0.14f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, x, TrackY + 1.45f, (int)value, 0.4f, GraphShowcaseStagePresenter.GateColor);
    }

    /// <summary>No dial: a thick plinth engraves the 42 groove and every beat copies it out, with equal-length tally marks.</summary>
    private static void DrawPlinthBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[1];
        ReadConstFloats(ctx, consts);
        float fixedLen = consts[0];

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (float midX, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        float plinthX = trackLeft + 1.0f;
        float plinthY = TrackY + 1.15f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, plinthX, plinthY, 1.9f, 0.7f, 1, GraphShowcaseStagePresenter.GateColor);
        float grooveEnd = plinthX - 0.75f + fixedLen * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, plinthX - 0.75f, plinthY, grooveEnd, plinthY, 0.14f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, grooveEnd + 0.3f, plinthY, (int)fixedLen, 0.45f, GraphShowcaseStagePresenter.CasterColor);

        float resultEnd = trackLeft + fixedLen * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, resultEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, resultEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, resultEnd + 0.4f, TrackY - 0.5f, (int)fixedLen, 0.45f, GraphShowcaseStagePresenter.EnemyColor);

        DrawEqualTallies(debugDraw, midX + TrackMeters * 0.5f + 0.5f, ctx.Wave);
    }

    /// <summary>Side column of notches lit one per beat; every notch is the same length so the value never changes.</summary>
    private static void DrawEqualTallies(DebugDrawCommandBuffer debugDraw, float x, int litCount)
    {
        const float notchLength = 0.7f;
        for (int i = 0; i < 5; i++)
        {
            float y = 3.0f + i * 0.55f;
            DebugDrawColor color = i < litCount
                ? GraphShowcaseStagePresenter.CasterColor
                : GraphShowcaseStagePresenter.GhostColor;
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, x - notchLength, y, x, y, 0.08f, color, arrowEnd: false);
        }
    }

    /// <summary>Zero-axis rail: the negative stub flips (slide) or folds (paper) over the axis into the positive red segment.</summary>
    private static void DrawZeroAxisBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target, bool fold)
    {
        Span<float> consts = stackalloc float[1];
        ReadConstFloats(ctx, consts);
        float signed = consts[0];
        float magnitude = MathF.Abs(signed);

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (float midX, _) = BenchGeometry(casterActor, targetActor);
        float railHalf = 0.9f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(midX - railHalf, TrackY),
            B = new Vector2(midX + railHalf, TrackY),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(midX, TrackY - 1.3f),
            B = new Vector2(midX, TrackY + 1.3f),
            Thickness = 0.12f,
            Color = GraphShowcaseStagePresenter.GateColor
        });

        float stubEnd = midX - magnitude * MetersPerPoint;
        float posEnd = midX + magnitude * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, stubEnd, TrackY, midX, TrackY, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, midX, TrackY, posEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, posEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, stubEnd - 0.3f, TrackY - 0.4f, (int)signed, 0.45f, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, posEnd + 0.3f, TrackY - 0.4f, (int)magnitude, 0.45f, GraphShowcaseStagePresenter.EnemyColor);

        if (fold)
        {
            GraphShowcaseStagePresenter.DrawArcArrow(debugDraw, midX, TrackY + 0.25f, 0.62f, -95f, 95f, GraphShowcaseStagePresenter.CasterColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, midX - magnitude * MetersPerPoint, TrackY + 1.0f, midX + magnitude * MetersPerPoint, TrackY + 1.0f,
                0.1f, GraphShowcaseStagePresenter.CasterColor);
        }
    }

    /// <summary>Damage block sent toward the stake; a gray guard block bites off the front, the rest keeps flying.</summary>
    private static void DrawBlockBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[2];
        ReadConstFloats(ctx, consts);
        float baseLen = consts[0];
        float cut = consts[1];
        float remaining = baseLen - cut;

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (_, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        float cutStart = trackLeft + remaining * MetersPerPoint;
        float baseEnd = trackLeft + baseLen * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, cutStart, TrackY, 0.2f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, cutStart, TrackY, baseEnd, TrackY, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, cutStart, TrackY, 0.14f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, cutStart, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (trackLeft + cutStart) * 0.5f + 0.25f, TrackY - 0.5f, (int)remaining, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (cutStart + baseEnd) * 0.5f + 0.2f, TrackY - 0.5f, (int)cut, 0.45f, GraphShowcaseStagePresenter.GhostColor);
    }

    /// <summary>The 40 block is cut in half at the middle and each 20 flies to its own stake.</summary>
    private static void DrawSplitBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[2];
        ReadConstFloats(ctx, consts);
        float total = consts[0];
        float divisor = consts[1];
        float half = total / divisor;

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        int context = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "context");
        if (context < 0)
        {
            return;
        }

        GraphOpsNodeActor contextActor = ctx.Vignette.Actors[context];
        (float midX, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        float leftEnd = midX - half * MetersPerPoint;
        float rightEnd = midX + half * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, leftEnd, TrackY, rightEnd, TrackY, 0.2f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(midX, TrackY - 0.3f),
            B = new Vector2(midX, TrackY + 0.3f),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, leftEnd, TrackY, midX, TrackY, 0.14f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, midX, TrackY, rightEnd, TrackY, 0.14f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, (leftEnd + midX) * 0.5f, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, (midX + rightEnd) * 0.5f, TrackY, contextActor.X, contextActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, midX, TrackY - 0.55f, (int)total, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (leftEnd + midX) * 0.5f - 0.2f, TrackY + 0.5f, (int)half, 0.4f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, (midX + rightEnd) * 0.5f - 0.2f, TrackY + 0.5f, (int)half, 0.4f, GraphShowcaseStagePresenter.EnemyColor);
    }

    /// <summary>Dice badge rerolls every beat; the dial (0-30 ruler) shows the current length over the last one's ghost, with a stacked roll history.</summary>
    private void DrawDiceBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        (float midX, float trackLeft) = BenchGeometry(casterActor, targetActor);
        DrawTrackRail(debugDraw, trackLeft);

        DrawDiceRuler(debugDraw, trackLeft);
        DrawDiceBadge(debugDraw, midX);

        float currentEnd = trackLeft + _lastRoll * RandomScale * MetersPerPoint;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, currentEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, currentEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        if (_rollCount > 1)
        {
            float previous = _rollHistory[(_rollCount - 2) % RollHistoryCapacity];
            GraphShowcaseStagePresenter.DrawGhostSegment(
                debugDraw, trackLeft, TrackY, trackLeft + previous * RandomScale * MetersPerPoint, TrackY, GraphShowcaseStagePresenter.GhostColor);
        }

        DrawRollHistory(debugDraw, targetActor.X);
    }

    private static void DrawDiceBadge(DebugDrawCommandBuffer debugDraw, float midX)
    {
        float x = midX;
        float y = TrackY + 1.35f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, x, y, 0.6f, 0.6f, 1, GraphShowcaseStagePresenter.CasterColor);
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x - 0.12f, y + 0.12f),
            Radius = 0.05f,
            Thickness = 0.03f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x + 0.12f, y - 0.12f),
            Radius = 0.05f,
            Thickness = 0.03f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y),
            Radius = 0.05f,
            Thickness = 0.03f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
    }

    /// <summary>0-30 dial ruler over the track: white ticks at 10/20/30 with the damage scale.</summary>
    private static void DrawDiceRuler(DebugDrawCommandBuffer debugDraw, float trackLeft)
    {
        for (int p = 10; p <= 30; p += 10)
        {
            float x = trackLeft + p * MetersPerPoint;
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(x, TrackY - 0.3f),
                B = new Vector2(x, TrackY + 0.3f),
                Thickness = 0.05f,
                Color = GraphShowcaseStagePresenter.GateColor
            });
            GraphShowcaseStagePresenter.DrawNumber(debugDraw, x + 0.28f, TrackY - 0.65f, p, 0.32f, GraphShowcaseStagePresenter.GateColor);
        }
    }

    private void DrawRollHistory(DebugDrawCommandBuffer debugDraw, float stakeX)
    {
        float x = stakeX + 1.6f;
        int shown = Math.Min(_rollCount, RollHistoryCapacity);
        for (int i = 0; i < shown; i++)
        {
            float roll = _rollHistory[(i + Math.Max(0, _rollCount - RollHistoryCapacity)) % RollHistoryCapacity];
            float y = 1.4f - i * 0.55f;
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, x - roll * RandomScale * MetersPerPoint, y, x, y, 0.12f,
                i == shown - 1 ? GraphShowcaseStagePresenter.EnemyColor : GraphShowcaseStagePresenter.GhostColor,
                arrowEnd: false);
        }
    }

    /// <summary>Gate between caster and stake: the permit lets one slash through every beat, all marks stay green.</summary>
    private void DrawGateBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float gateX = (casterActor.X + targetActor.X) * 0.5f;
        float groundY = casterActor.Y;

        float postL = gateX - 0.55f;
        float postR = gateX + 0.55f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(postL, groundY - 0.5f),
            B = new Vector2(postL, groundY + 0.5f),
            Thickness = 0.12f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(postR, groundY - 0.5f),
            B = new Vector2(postR, groundY + 0.5f),
            Thickness = 0.12f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        if (_lastBool)
        {
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(postL, groundY + 0.5f),
                B = new Vector2(postR, groundY + 0.5f),
                Thickness = 0.1f,
                Color = GraphShowcaseStagePresenter.GuardColor
            });
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, gateX, groundY + 1.15f, GraphShowcaseStagePresenter.BadgeKind.Check, GraphShowcaseStagePresenter.GuardColor);
        }
        else
        {
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(postL, groundY - 0.1f),
                B = new Vector2(postR, groundY - 0.1f),
                Thickness = 0.16f,
                Color = GraphShowcaseStagePresenter.EnemyColor
            });
        }

        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y, 0.08f, GraphShowcaseStagePresenter.EnemyColor);

        DrawPermitMarks(debugDraw, gateX, ctx.Wave);
    }

    /// <summary>Row of permit notches, one lit per beat; every mark is green because the permit never denies.</summary>
    private static void DrawPermitMarks(DebugDrawCommandBuffer debugDraw, float gateX, int wave)
    {
        for (int i = 0; i < 5; i++)
        {
            float x = gateX + (i - 2) * 0.45f;
            DebugDrawColor color = i < wave
                ? GraphShowcaseStagePresenter.GuardColor
                : GraphShowcaseStagePresenter.GhostColor;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(x, -1.2f),
                HalfWidth = 0.12f,
                HalfHeight = 0.12f,
                Thickness = 0.04f,
                Color = color
            });
        }
    }

    /// <summary>Comparison beat: the same 50 damage blade is held against both bars — the thin bar is shorter than the
    /// blade so the slash lands and clears it, the thick bar swallows it so the blade stays suspended (no reach, no hit).</summary>
    private void DrawCompareBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        Span<float> consts = stackalloc float[1];
        ReadConstFloats(ctx, consts);
        float damage = consts[0];

        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        int context = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "context");
        if (context < 0)
        {
            return;
        }

        GraphOpsNodeActor contextActor = ctx.Vignette.Actors[context];
        float bladeHeight = damage * (2f / 100f);

        DrawStakeBar(debugDraw, targetActor, ctx.ActorHealth[target]);
        DrawStakeBar(debugDraw, contextActor, ctx.ActorHealth[context]);
        DrawStakeBlade(debugDraw, targetActor, bladeHeight, lands: true);
        DrawStakeBlade(debugDraw, contextActor, bladeHeight, lands: false);
    }

    /// <summary>Vertical bar over a stake: max outline, real current fill, and the white 50 threshold tick.</summary>
    private static void DrawStakeBar(DebugDrawCommandBuffer debugDraw, GraphOpsNodeActor stake, float currentHealth)
    {
        float barScale = 2f / 100f;
        float maxHeight = stake.HealthMax > 0f ? stake.HealthMax * barScale : 2f;
        float currentHeight = Math.Clamp(currentHealth * barScale, 0f, maxHeight);
        float baseY = stake.Y;

        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(stake.X, baseY),
            B = new Vector2(stake.X, baseY + maxHeight),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(stake.X - 0.22f, baseY + currentHeight),
            B = new Vector2(stake.X + 0.22f, baseY + currentHeight),
            Thickness = 0.08f,
            Color = currentHealth > 0f
                ? GraphShowcaseStagePresenter.EnemyColor
                : GraphShowcaseStagePresenter.GhostColor
        });
        float thresholdY = 50f * barScale;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(stake.X - 0.34f, baseY + thresholdY),
            B = new Vector2(stake.X + 0.34f, baseY + thresholdY),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, stake.X + 0.52f, baseY + thresholdY, 50, 0.32f, GraphShowcaseStagePresenter.GateColor);
    }

    /// <summary>The shared damage blade: red and fallen when it outlengths the bar, gray and hovering when it cannot reach the bottom.</summary>
    private static void DrawStakeBlade(DebugDrawCommandBuffer debugDraw, GraphOpsNodeActor stake, float bladeHeight, bool lands)
    {
        float baseY = stake.Y;
        float x = stake.X - 0.6f;
        if (lands)
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, x, baseY + bladeHeight, x, baseY, 0.12f, GraphShowcaseStagePresenter.EnemyColor, arrowEnd: false);
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, x, baseY, stake.X, baseY, 0.08f, GraphShowcaseStagePresenter.EnemyColor);
            GraphShowcaseStagePresenter.DrawNumber(debugDraw, x - 0.25f, baseY + bladeHeight, 50, 0.32f, GraphShowcaseStagePresenter.EnemyColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawGhostSegment(
                debugDraw, x, baseY + bladeHeight, x, baseY + 0.2f, GraphShowcaseStagePresenter.GhostColor);
            GraphShowcaseStagePresenter.DrawNumber(debugDraw, x - 0.25f, baseY + bladeHeight, 50, 0.32f, GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private static void ApplyResult(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeLinearOptions linear,
        GraphOpsNodeExecuteResult result,
        int targetIndex)
    {
        if (string.Equals(linear.ApplyTo, "none", StringComparison.Ordinal) || IsGraphSettled(linear))
        {
            return;
        }

        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Linear apply '{linear.ApplyTo}' requires a target actor.");
        }

        float next = ctx.ActorHealth[targetIndex];
        if (string.Equals(linear.ApplyTo, "targetHealthSet", StringComparison.Ordinal))
        {
            next = RequireFloat(linear, result);
        }
        else if (string.Equals(linear.ApplyTo, "targetHealthSubtract", StringComparison.Ordinal))
        {
            next = Math.Max(0f, next - RequireFloat(linear, result));
        }
        else
        {
            throw new InvalidOperationException($"Unknown linear.applyTo '{linear.ApplyTo}'.");
        }

        GraphOpsNodeActor actor = ctx.Vignette.Actors[targetIndex];
        GraphOpsNodeActorBinding.WriteHealth(
            ctx.SimWorld,
            ctx.SimActors[targetIndex],
            next,
            actor.HealthMax,
            GraphOpsNodeActorBinding.RequireTagOps(ctx));
        ctx.ActorHealth[targetIndex] = next;
    }

    private static float RequireFloat(GraphOpsNodeLinearOptions linear, GraphOpsNodeExecuteResult result)
    {
        return linear.ResultKind switch
        {
            "float" => result.FloatValue,
            "int" => result.IntValue,
            "bool" => result.BoolValue ? 1f : 0f,
            _ => throw new InvalidOperationException($"Unknown linear.resultKind '{linear.ResultKind}'.")
        };
    }

    private static string FormatResult(string resultKind, GraphOpsNodeExecuteResult result)
    {
        return resultKind switch
        {
            "float" => result.FloatValue.ToString("0.#"),
            "int" => result.IntValue.ToString(),
            "bool" => result.BoolValue ? "成立" : "不成立",
            _ => throw new InvalidOperationException($"Unknown linear.resultKind '{resultKind}'.")
        };
    }
}
