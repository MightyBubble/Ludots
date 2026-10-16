using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>一次模糊运行里的一种操作。表由调用方给，执行器能接上才跑。</summary>
public sealed class CrowdFuzzOp
{
    public required string Id { get; init; }
    public bool Structure { get; init; }
    public bool ProbeHandles { get; init; }

    /// <summary>没有外加执行器时不抽中。调用方用这份标记把还没接上的操作留在表里。</summary>
    public bool Reserved { get; init; }
}

/// <summary>种子、步数和操作表都由调用方给。这里不保存一份默认轮次。</summary>
public sealed class CrowdFuzzPlan
{
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required int StepsPerOp { get; init; }
    public required int ProbeHandleCount { get; init; }
    public required int SpawnCount { get; init; }
    public required string PlaceTemplate { get; init; }
    public required int PlaceSizeCm { get; init; }
    public required string OrderShape { get; init; }
    public required int OrderMemberCap { get; init; }
    public required int StallGuard { get; init; }
    public required int BoomRadiusCm { get; init; }
    public required Fix64 RingScale { get; init; }
    public required Fix64 Impulse { get; init; }
    public string PathTemplate { get; init; } = "";
    public int PathWidthCm { get; init; }
    public int PathLengthCm { get; init; }
    public required IReadOnlyList<CrowdFuzzOp> Ops { get; init; }
}

public sealed class CrowdFuzzResult
{
    public required bool Passed { get; init; }
    public required long Seed { get; init; }
    public required int Step { get; init; }
    public required string OpId { get; init; }
    public string? Error { get; init; }
    public required IReadOnlyList<string> Reserved { get; init; }
}

public readonly record struct CrowdFuzzHandleProbe(uint Handle, int EntityId, int Version);

/// <summary>
/// 外加执行器。内建是生成、下令、放置、拆除、放条带、拆条带和爆炸。
/// 表里标了保留、又没有同名执行器的操作不抽中。
/// </summary>
public delegate void CrowdFuzzOpExecutor(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng);

/// <summary>
/// 同一条模糊运行可以一次跑完，也可以按时间片接着跑。
/// 一片开始前看预算；已经开工的操作会做完。预算用 <see cref="TimeSpan.MaxValue"/> 时一次跑完。
/// </summary>
public sealed class CrowdFuzzCursor
{
    private readonly CrowdFuzzPlan _plan;
    private readonly IReadOnlyDictionary<string, CrowdFuzzOpExecutor>? _extra;
    private readonly List<CrowdFuzzOp> _drawable;
    private readonly IReadOnlyList<string> _reserved;
    private CrowdSimRng? _rng;
    private List<(int UnitType, int RIdx)>? _options;
    private bool _blockSaved;
    private bool _previousBlock;
    private bool _opened;

    public CrowdFuzzCursor(CrowdFuzzPlan plan, IReadOnlyDictionary<string, CrowdFuzzOpExecutor>? extraExecutors = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        _extra = extraExecutors;
        _reserved = CrowdFuzzRunner.ReservedWithoutExecutor(plan.Ops, extraExecutors);
        _drawable = new List<CrowdFuzzOp>(plan.Ops.Count);
        for (int i = 0; i < plan.Ops.Count; i++)
        {
            var op = plan.Ops[i];
            if (op.Reserved && (extraExecutors == null || !extraExecutors.ContainsKey(op.Id))) continue;
            _drawable.Add(op);
        }
    }

    public int Completed { get; private set; }
    public bool Finished { get; private set; }
    public CrowdFuzzResult? Result { get; private set; }

    /// <summary>这一片里调用过 <see cref="CrowdSimSession.Step"/>。</summary>
    public bool Stepped { get; private set; }

    public void Advance(CrowdSimSession session, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(session);
        Stepped = false;
        if (Finished) return;
        if (_plan.OpCount < 0 || _plan.StepsPerOp < 1 || _plan.ProbeHandleCount < 0 || _plan.StallGuard < 1
            || _plan.BoomRadiusCm <= 0 || _plan.RingScale <= Fix64.OneValue || _plan.Impulse < Fix64.Zero)
        {
            Finish(CrowdFuzzRunner.Fail(_plan, _reserved, 0, "", "操作数、每步推进、句柄条数、停摆上限或爆炸参数无效。"));
            return;
        }

        if (_plan.OpCount == 0)
        {
            Finish(new CrowdFuzzResult
            {
                Passed = true,
                Seed = _plan.Seed,
                Step = 0,
                OpId = "",
                Reserved = _reserved,
            });
            return;
        }

        if (_drawable.Count == 0)
        {
            Finish(CrowdFuzzRunner.Fail(_plan, _reserved, 0, "", "没有可执行的操作。"));
            return;
        }

        if (!_opened)
        {
            _opened = true;
            _rng = CrowdSimRng.Create(_plan.Seed);
            _options = CrowdFuzzRunner.SpawnOptions(session);
            _previousBlock = session.BlockOnDueReplies;
            _blockSaved = true;
            session.BlockOnDueReplies = true;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        int began = Completed;
        try
        {
            while (Completed < _plan.OpCount)
            {
                if (Completed > began && watch.Elapsed >= budget) return;
                var op = _drawable[CrowdFuzzRunner.NextInt(_rng!, _drawable.Count)];
                int step = Completed + 1;
                try
                {
                    CrowdFuzzHandleProbe[]? probes = op.ProbeHandles
                        ? CrowdFuzzRunner.Snapshot(session, _plan.ProbeHandleCount, _rng!)
                        : null;
                    CrowdFuzzRunner.Execute(session, _plan, _rng!, op, _extra, _options!);
                    if (probes != null)
                    {
                        string? probeError = CrowdFuzzRunner.CheckHandles(session, probes);
                        if (probeError != null)
                        {
                            Finish(CrowdFuzzRunner.Fail(_plan, _reserved, step, op.Id, probeError));
                            return;
                        }
                    }

                    int guard = _plan.StallGuard;
                    int ran = 0;
                    while (ran < _plan.StepsPerOp)
                    {
                        if (guard-- <= 0)
                        {
                            throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
                        }

                        bool ok = session.Step(out _);
                        Stepped = true;
                        if (!ok) continue;
                        ran++;
                    }

                    CrowdSimulationValidator.ValidateSim(session);
                    if (op.Structure)
                    {
                        CrowdSimulationValidator.ValidateStructures(session);
                        CrowdSimulationValidator.ValidateNav(session);
                    }
                }
                catch (Exception ex)
                {
                    Finish(CrowdFuzzRunner.Fail(_plan, _reserved, step, op.Id, ex.Message));
                    return;
                }

                Completed = step;
            }

            Finish(new CrowdFuzzResult
            {
                Passed = true,
                Seed = _plan.Seed,
                Step = _plan.OpCount,
                OpId = "",
                Reserved = _reserved,
            });
        }
        finally
        {
            if (Finished && _blockSaved)
            {
                session.BlockOnDueReplies = _previousBlock;
                _blockSaved = false;
            }
        }
    }

    private void Finish(CrowdFuzzResult result)
    {
        Finished = true;
        Result = result;
        Completed = result.Passed ? _plan.OpCount : result.Step;
    }
}

/// <summary>
/// 按种子抽操作。每个操作后成功推进 <see cref="CrowdFuzzPlan.StepsPerOp"/> 次并查仿真不变量。
/// 放置和拆除再对实体栅格和真实导航做全量比较。
/// 带句柄探测的操作在执行前抽样，执行后幸存者必须仍解析到原实体，死亡句柄解析为 -1。
/// 无头推进会暂时打开 <see cref="CrowdSimSession.BlockOnDueReplies"/>，否则路径答复没到时两次 step 停在同一 tick。
/// </summary>
public static class CrowdFuzzRunner
{
    public static CrowdFuzzResult Run(
        CrowdSimSession session,
        CrowdFuzzPlan plan,
        IReadOnlyDictionary<string, CrowdFuzzOpExecutor>? extraExecutors = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var cursor = new CrowdFuzzCursor(plan, extraExecutors);
        cursor.Advance(session, TimeSpan.MaxValue);
        return cursor.Result ?? Fail(plan, Array.Empty<string>(), 0, "", "模糊运行没有结果。");
    }

    public static CrowdFuzzHandleProbe[] Snapshot(CrowdSimSession session, int count, CrowdSimRng rng)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        int n = session.Units.Count;
        int take = n <= count ? n : count;
        var probes = new CrowdFuzzHandleProbe[take];
        if (take == 0) return probes;
        if (take == n)
        {
            for (int i = 0; i < n; i++) probes[i] = ProbeAt(session, i);
            return probes;
        }

        var index = new int[n];
        for (int i = 0; i < n; i++) index[i] = i;
        for (int i = 0; i < take; i++)
        {
            int j = i + NextInt(rng, n - i);
            (index[i], index[j]) = (index[j], index[i]);
            probes[i] = ProbeAt(session, index[i]);
        }

        return probes;
    }

    /// <summary>解析成功必须是抽样时的实体。解析失败（-1）只允许该实体已经不在场。</summary>
    public static string? CheckHandles(CrowdSimSession session, ReadOnlySpan<CrowdFuzzHandleProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(session);
        for (int i = 0; i < probes.Length; i++)
        {
            var probe = probes[i];
            if (session.Units.TryResolveHandle(probe.Handle, out int dense))
            {
                var entity = session.Units.EntityAt(dense);
                if (entity.Id != probe.EntityId || entity.Version != probe.Version)
                {
                    return $"句柄 {probe.Handle} 解析到实体 {entity.Id}.{entity.Version}，探测时是 {probe.EntityId}.{probe.Version}";
                }
            }
            else if (StillLive(session, probe.EntityId, probe.Version))
            {
                return $"句柄 {probe.Handle} 解析为 -1，实体 {probe.EntityId}.{probe.Version} 仍在";
            }
        }

        return null;
    }

    internal static void Execute(
        CrowdSimSession session,
        CrowdFuzzPlan plan,
        CrowdSimRng rng,
        CrowdFuzzOp op,
        IReadOnlyDictionary<string, CrowdFuzzOpExecutor>? extra,
        List<(int UnitType, int RIdx)> options)
    {
        if (extra != null && extra.TryGetValue(op.Id, out var plugged))
        {
            plugged(session, plan, rng);
            return;
        }

        switch (op.Id)
        {
            case "spawn":
                ExecSpawn(session, plan, rng, options);
                return;
            case "order":
                ExecOrder(session, plan, rng);
                return;
            case "place":
                ExecPlace(session, plan, rng);
                return;
            case "remove":
                ExecRemove(session, plan, rng);
                return;
            case "placePath":
                ExecPlacePath(session, plan, rng);
                return;
            case "removePath":
                ExecRemovePath(session, plan, rng);
                return;
            case "kill":
                ExecKill(session, plan, rng);
                return;
            default:
                throw new InvalidOperationException($"操作 {op.Id} 没有执行器。");
        }
    }

    private static void ExecSpawn(
        CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng, List<(int UnitType, int RIdx)> options)
    {
        if (options.Count == 0) throw new InvalidOperationException("没有可生成的兵种。");
        var bases = session.Config.Deploy.Bases;
        if (bases.Count == 0) throw new InvalidOperationException("没有部署基地，无法生成。");
        int side = WorldSideCm(session);
        var pick = options[NextInt(rng, options.Count)];
        var player = bases[NextInt(rng, bases.Count)].PlayerId;
        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = player,
            XCm = NextInt(rng, side),
            YCm = NextInt(rng, side),
            Count = plan.SpawnCount,
            UnitType = pick.UnitType,
            RIdx = pick.RIdx,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    private static void ExecOrder(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        var bases = session.Config.Deploy.Bases;
        if (bases.Count == 0 || plan.OrderMemberCap <= 0) return;
        int player = PlayerForOrder(session, rng, bases);
        int n = session.Units.CopyOwnHandles(player, session.MemberCapture);
        if (n <= 0) return;
        int take = Math.Min(n, plan.OrderMemberCap);
        var members = new uint[take];
        if (take == n)
        {
            Array.Copy(session.MemberCapture, members, n);
        }
        else
        {
            var pool = new uint[n];
            Array.Copy(session.MemberCapture, pool, n);
            for (int i = 0; i < take; i++)
            {
                int j = i + NextInt(rng, n - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
                members[i] = pool[i];
            }
        }

        int side = WorldSideCm(session);
        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = NextInt(rng, side),
            YCm = NextInt(rng, side),
            ShapeId = plan.OrderShape,
            Members = members,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    private static void ExecPlace(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        int side = WorldSideCm(session);
        int margin = plan.PlaceSizeCm / 2;
        int x;
        int y;
        if (margin < 0 || side - margin <= margin)
        {
            x = side / 2;
            y = side / 2;
        }
        else
        {
            int span = side - margin - margin;
            x = margin + NextInt(rng, span);
            y = margin + NextInt(rng, span);
        }

        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = plan.PlaceTemplate,
            XCm = x,
            YCm = y,
            SizeCm = plan.PlaceSizeCm,
            ToXCm = x,
            ToYCm = y,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    private static void ExecKill(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        int x;
        int y;
        int n = session.Units.Count;
        if (n > 0)
        {
            var entity = session.Units.EntityAt(NextInt(rng, n));
            var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
            x = pos.X.ToInt();
            y = pos.Y.ToInt();
        }
        else
        {
            int side = WorldSideCm(session);
            x = NextInt(rng, side);
            y = NextInt(rng, side);
        }

        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = x,
            YCm = y,
            RadiusCm = plan.BoomRadiusCm,
            RingScale = plan.RingScale,
            Impulse = plan.Impulse,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    private static void ExecPlacePath(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        if (plan.PathWidthCm <= 0 || plan.PathLengthCm <= 0 || string.IsNullOrWhiteSpace(plan.PathTemplate))
        {
            throw new InvalidOperationException("放条带需要 pathTemplate、pathWidthCm 和 pathLengthCm。");
        }

        int side = WorldSideCm(session);
        int half = plan.PathWidthCm / 2;
        int lo = half;
        int hi = side - half;
        int x0;
        int y;
        int x1;
        if (hi <= lo)
        {
            x0 = side / 2;
            y = side / 2;
            x1 = x0;
        }
        else
        {
            int room = hi - lo;
            x0 = lo + NextInt(rng, room);
            y = lo + NextInt(rng, room);
            int span = Math.Min(plan.PathLengthCm, hi - x0);
            x1 = x0 + span;
        }

        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = plan.PathTemplate,
            XCm = x0,
            YCm = y,
            SizeCm = plan.PathWidthCm,
            ToXCm = x1,
            ToYCm = y,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    private static void ExecRemovePath(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        var store = session.Structures;
        if (store == null) return;
        int picked = -1;
        int seen = 0;
        for (int i = 0; i < store.EntityIds.Count; i++)
        {
            int id = store.EntityIds[i];
            if (!store.TryGetTemplate(id, out int tpl)) continue;
            if (!session.Config.Structures.Templates[tpl].OnUpperLayer) continue;
            seen++;
            if (NextInt(rng, seen) == 0) picked = id;
        }

        if (picked < 0) return;
        CrowdStructureOps.RemoveStructure(session, picked);
    }

    private static void ExecRemove(CrowdSimSession session, CrowdFuzzPlan plan, CrowdSimRng rng)
    {
        int side = WorldSideCm(session);
        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = NextInt(rng, side),
            YCm = NextInt(rng, side),
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }

    internal static List<(int UnitType, int RIdx)> SpawnOptions(CrowdSimSession session)
    {
        var list = new List<(int, int)>();
        var cfg = session.Config;
        var radii = CrowdDeployment.DistinctRadiusClasses(cfg);
        for (int t = 0; t < cfg.UnitTypes.Count; t++)
        {
            int layer = cfg.UnitTypes[t].AgentTypeIndex;
            for (int r = 0; r < radii.Count; r++)
            {
                if (!CrowdDeployment.ProfileOf(cfg, layer, r).AutoDeploy) continue;
                if (!session.NavByLayerRadius.ContainsKey((layer, r))) continue;
                list.Add((t, r));
            }
        }

        return list;
    }

    private static int PlayerForOrder(CrowdSimSession session, CrowdSimRng rng, IReadOnlyList<RuntimeDeployBase> bases)
    {
        int choices = 0;
        int picked = bases[0].PlayerId;
        for (int i = 0; i < bases.Count; i++)
        {
            int player = bases[i].PlayerId;
            if (!OwnsUnit(session, player)) continue;
            choices++;
            if (NextInt(rng, choices) == 0) picked = player;
        }

        return choices == 0 ? bases[NextInt(rng, bases.Count)].PlayerId : picked;
    }

    private static bool OwnsUnit(CrowdSimSession session, int player)
    {
        var world = session.World;
        var units = session.Units;
        for (int i = 0; i < units.Count; i++)
        {
            if (world.Get<Ludots.Core.Gameplay.Components.PlayerOwner>(units.EntityAt(i)).PlayerId == player)
            {
                return true;
            }
        }

        return false;
    }

    private static int WorldSideCm(CrowdSimSession session)
    {
        long side = (long)session.Config.NavCellsPerSide * session.Config.NavCellSizeCm;
        if (side <= 0 || side > int.MaxValue)
        {
            throw new InvalidOperationException("世界边长不在可放置范围内。");
        }

        return (int)side;
    }

    private static CrowdFuzzHandleProbe ProbeAt(CrowdSimSession session, int dense)
    {
        var entity = session.Units.EntityAt(dense);
        return new CrowdFuzzHandleProbe(session.Units.HandleAt(dense), entity.Id, entity.Version);
    }

    private static bool StillLive(CrowdSimSession session, int entityId, int version)
    {
        var units = session.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var entity = units.EntityAt(i);
            if (entity.Id == entityId && entity.Version == version) return true;
        }

        return false;
    }

    internal static int NextInt(CrowdSimRng rng, int count)
    {
        if (count <= 1) return 0;
        long value = (rng.Next() * Fix64.FromInt(count)).ToLong();
        if (value < 0) return 0;
        if (value >= count) return count - 1;
        return (int)value;
    }

    internal static IReadOnlyList<string> ReservedWithoutExecutor(
        IReadOnlyList<CrowdFuzzOp> ops,
        IReadOnlyDictionary<string, CrowdFuzzOpExecutor>? extra)
    {
        var list = new List<string>();
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (!op.Reserved) continue;
            if (extra != null && extra.ContainsKey(op.Id)) continue;
            list.Add(op.Id);
        }

        return list;
    }

    internal static CrowdFuzzResult Fail(
        CrowdFuzzPlan plan, IReadOnlyList<string> reserved, int step, string opId, string message)
        => new()
        {
            Passed = false,
            Seed = plan.Seed,
            Step = step,
            OpId = opId,
            Error = $"种子 {plan.Seed} 第 {step} 步 {opId}: {message}",
            Reserved = reserved,
        };
}
