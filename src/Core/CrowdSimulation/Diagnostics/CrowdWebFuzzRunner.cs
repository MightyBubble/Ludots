using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Components;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>Web <c>fuzzIter</c>（debug.js）的 C# 移植：阈值抽 op、mulberry32、每 op 两步与全量校验。</summary>
public static class CrowdWebFuzzRunner
{
    /// <summary>
    /// 对拍的位置口径：Web 存 f32 米、C# 存 Fix64 厘米，同一单位两端坐标每轴差不超过 1 厘米。
    /// op 参数里从单位位姿读出的米制坐标按此比；位置 L1 统计（逐轴取整到厘米再求和）每单位最多差 4 厘米。
    /// </summary>
    public const double PositionAgreementMeters = 0.01;

    public const long PositionStatSlackCmPerUnit = 4;

    public static CrowdWebFuzzRunResult Run(
        CrowdSimSession session, int opCount, CrowdWebFuzzBlast blast, CrowdWebFuzzPalette palette, long seed)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(palette);
        if (opCount < 0) throw new ArgumentOutOfRangeException(nameof(opCount));
        var templates = ResolveTemplates(session, palette.TemplateIds);
        var formationIds = ResolveFormationIds(session, palette.FormationIds);
        var structureOrder = ResolveInitialStructureOrder(session, palette.InitialStructureAnchors);
        var rng = CrowdSimRng.Create(seed);
        var steps = new List<CrowdWebFuzzStepRecord>(opCount);
        var selection = new bool[session.Units.Capacity];
        double worldSizeM = WorldSizeMeters(session);
        int probeCount = 32;
        bool prevBlock = session.BlockOnDueReplies;
        session.BlockOnDueReplies = true;
        try
        {
            for (int k = 0; k < opCount; k++)
            {
                var rngDraws = new List<uint>();
                double opRoll = Draw(rng, rngDraws);
                CrowdWebFuzzOpRecord op;
                try
                {
                    if (opRoll >= 0.88)
                    {
                        op = ExecStructure(session, rng, rngDraws, worldSizeM, templates, structureOrder);
                    }
                    else if (opRoll < 0.3 && session.Units.Count > 0)
                    {
                        op = ExecExplode(session, rng, rngDraws, worldSizeM, blast, probeCount);
                    }
                    else if (opRoll < 0.55)
                    {
                        op = ExecSpawn(session, rng, rngDraws, worldSizeM);
                    }
                    else
                    {
                        op = ExecOrder(session, rng, rngDraws, worldSizeM, selection, formationIds);
                    }

                    StepTwice(session);
                    string? validateError = TryValidate(session, op.Kind);
                    bool ok = validateError == null;
                    steps.Add(new CrowdWebFuzzStepRecord
                    {
                        RngDraws = rngDraws,
                        Op = op,
                        Units = session.Units.Count,
                        ValidateOk = ok,
                        ValidateErrors = ok ? null : new[] { validateError! },
                        UnitChecksumHex = CrowdWebFuzzChecksum.UnitHex(session),
                        PositionStatL1Cm = PositionStatL1Cm(session),
                    });
                    if (!ok)
                    {
                        return Fail(seed, steps, k, op.Kind, new[] { validateError! });
                    }
                }
                catch (Exception ex)
                {
                    steps.Add(new CrowdWebFuzzStepRecord
                    {
                        RngDraws = rngDraws,
                        Op = new CrowdWebFuzzOpRecord { Kind = "error", Params = new JsonObject { ["message"] = ex.Message } },
                        Units = session.Units.Count,
                        ValidateOk = false,
                        ValidateErrors = new[] { ex.Message },
                        UnitChecksumHex = CrowdWebFuzzChecksum.UnitHex(session),
                        PositionStatL1Cm = PositionStatL1Cm(session),
                    });
                    return Fail(seed, steps, k, "error", new[] { ex.Message });
                }
            }

            ClearSelection(session, selection);
            return new CrowdWebFuzzRunResult
            {
                Ok = true,
                Seed = seed,
                Steps = steps,
            };
        }
        finally
        {
            session.BlockOnDueReplies = prevBlock;
        }
    }

    private static CrowdWebFuzzRunResult Fail(long seed, List<CrowdWebFuzzStepRecord> steps, int failedAt, string op, IReadOnlyList<string> errors)
        => new()
        {
            Ok = false,
            Seed = seed,
            FailedAt = failedAt,
            FailedOp = op,
            Errors = errors,
            Steps = steps,
        };

    private static void KillAtMeters(CrowdSimSession session, double wx, double wy, double radius, CrowdWebFuzzBlast blast)
    {
        session.Units.MaterializePending();
        float r2 = (float)(radius * radius);
        float ring = (float)(radius * blast.RingScale.ToDouble());
        float ring2 = ring * ring;
        var scratch = new List<int>();
        var units = session.Units;
        var world = session.World;
        var kernel = session.Movement;
        for (int i = 0; i < units.Count; i++)
        {
            var (mx, my) = UnitPositionMeters(session, i);
            float dx = (float)(mx - wx);
            float dy = (float)(my - wy);
            float d2 = dx * dx + dy * dy;
            if (d2 <= r2)
            {
                scratch.Add(i);
                continue;
            }

            if (d2 < ring2 && blast.Impulse > Fix64.Zero)
            {
                var entity = units.EntityAt(i);
                float d = MathF.Sqrt(d2);
                var agent = world.Get<CrowdSimulationAgent>(entity);
                float speed = (float)agent.SpeedCmPerSec.ToDouble() / 100f;
                float f = (1f - d / ring) * speed * (float)blast.Impulse.ToDouble() / d;
                var kin = world.Get<CrowdSimulationKinematics>(entity);
                kin.Velocity = new Fix64Vec2(
                    kin.Velocity.X + Fix64.FromDouble(dx * f * 100),
                    kin.Velocity.Y + Fix64.FromDouble(dy * f * 100));
                world.Set(entity, kin);
            }
        }

        for (int j = scratch.Count - 1; j >= 0; j--)
        {
            int i = scratch[j];
            var entity = units.EntityAt(i);
            int last = units.Count - 1;
            if (i != last) kernel?.MoveAvoidanceRow(last, i);
            var unitState = world.Get<CrowdSimulationUnitState>(entity);
            kernel?.ClearMotionSlot(unitState.Slot);
            int groupId = unitState.GroupId;
            units.ReleaseDense(i);
            if (!session.Groups.TryGet(groupId, out var group))
            {
                throw new InvalidOperationException($"kill: 组 {groupId} 不存在。");
            }

            group.Count--;
        }

        session.Groups.ReleaseEmpty();
        session.Orders.Prune(session.Groups);
    }

    private static double Draw(CrowdSimRng rng, List<uint> draws)
    {
        uint raw = NextU32(rng);
        draws.Add(raw);
        return raw / 4294967296.0;
    }

    private static int DrawIndex(CrowdSimRng rng, List<uint> draws, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        double u = Draw(rng, draws);
        return Math.Min(count - 1, (int)Math.Floor(u * count));
    }

    private static uint NextU32(CrowdSimRng rng)
    {
        var f = rng.Next();
        return (uint)f.RawValue;
    }

    private static double WorldSizeMeters(CrowdSimSession session)
        => session.Config.NavCellsPerSide * session.Config.NavCellSizeCm / 100.0;

    private static (double X, double Y) PickPoint(CrowdSimSession session, CrowdSimRng rng, List<uint> draws, double sizeM)
    {
        int n = session.Units.Count;
        if (n > 0 && Draw(rng, draws) < 0.7)
        {
            int j = DrawIndex(rng, draws, n);
            var pos = UnitPositionMeters(session, j);
            return (pos.X, pos.Y);
        }

        double x = Draw(rng, draws) * sizeM;
        double y = Draw(rng, draws) * sizeM;
        return (x, y);
    }

    private static (double X, double Y) UnitPositionMeters(CrowdSimSession session, int dense)
    {
        Fix64Vec2 p;
        var movement = session.Movement;
        if (movement != null && dense < movement.Positions.Length)
        {
            p = movement.Positions[dense];
        }
        else
        {
            var entity = session.Units.EntityAt(dense);
            p = session.World.Get<WorldPositionCm>(entity).Value;
        }

        float xm = (float)(p.X.ToDouble() / 100.0);
        float ym = (float)(p.Y.ToDouble() / 100.0);
        return (xm, ym);
    }

    private static RuntimeStructureTemplate[] ResolveTemplates(CrowdSimSession session, IReadOnlyList<string> ids)
    {
        var byId = session.Config.Structures.Templates.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var resolved = new RuntimeStructureTemplate[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            if (!byId.TryGetValue(ids[i], out var template))
            {
                throw new InvalidOperationException($"模糊对拍调色板: 结构模板 {ids[i]} 不在当前配置中。");
            }

            resolved[i] = template;
        }

        return resolved;
    }

    private static string[] ResolveFormationIds(CrowdSimSession session, IReadOnlyList<string> ids)
    {
        var known = new HashSet<string>(session.Config.Formations.Select(f => f.Id), StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!known.Contains(id))
            {
                throw new InvalidOperationException($"模糊对拍调色板: 阵型 {id} 不在当前配置中。");
            }
        }

        return ids.ToArray();
    }

    private static List<int> ResolveInitialStructureOrder(CrowdSimSession session, IReadOnlyList<(double X, double Y)> anchors)
    {
        var store = session.Structures
            ?? throw new InvalidOperationException("模糊对拍需要结构仓。");
        if (anchors.Count != store.EntityIds.Count)
        {
            throw new InvalidOperationException($"模糊对拍调色板: Web 开局结构 {anchors.Count} 个，C# 结构仓 {store.EntityIds.Count} 个。");
        }

        var idByAnchor = new Dictionary<(long X, long Y), int>(store.EntityIds.Count);
        foreach (int id in store.EntityIds)
        {
            var anchor = AnchorCm(store, id);
            if (!idByAnchor.TryAdd(anchor, id))
            {
                throw new InvalidOperationException($"模糊对拍调色板: 结构 {idByAnchor[anchor]} 与 {id} 锚点重合，无法与 Web 序一一对应。");
            }
        }

        var order = new List<int>(anchors.Count);
        foreach (var (x, y) in anchors)
        {
            var key = ((long)Math.Round(x * 100.0), (long)Math.Round(y * 100.0));
            if (!idByAnchor.Remove(key, out int id))
            {
                throw new InvalidOperationException($"模糊对拍调色板: Web 开局结构锚点 ({x}, {y}) 在 C# 结构仓里找不到。");
            }

            order.Add(id);
        }

        return order;
    }

    private static (long X, long Y) AnchorCm(CrowdStructuresStore store, int id)
    {
        if (!store.TryGetFootprint(id, out var fp))
        {
            throw new InvalidOperationException($"结构 {id} 无足迹。");
        }

        return fp.Shape == CrowdStructureShape.Path
            ? ((long)Math.Round(fp.X0.ToDouble()), (long)Math.Round(fp.Y0.ToDouble()))
            : ((long)Math.Round(fp.CenterXCm.ToDouble()), (long)Math.Round(fp.CenterYCm.ToDouble()));
    }

    /// <summary>与 Web 结构表（插入序 Map）同步：拆除与到期的从原位删掉，新放置的按放置序追加。</summary>
    private static void SyncStructureOrder(CrowdStructuresStore store, List<int> order)
    {
        var live = new HashSet<int>(store.EntityIds);
        order.RemoveAll(id => !live.Contains(id));
        var known = new HashSet<int>(order);
        foreach (int id in store.EntityIds)
        {
            if (known.Add(id)) order.Add(id);
        }
    }

    private static CrowdWebFuzzOpRecord ExecStructure(
        CrowdSimSession session, CrowdSimRng rng, List<uint> draws, double sizeM,
        RuntimeStructureTemplate[] templates, List<int> structureOrder)
    {
        var store = session.Structures
            ?? throw new InvalidOperationException("模糊对拍需要结构仓。");
        SyncStructureOrder(store, structureOrder);
        if (structureOrder.Count > 0 && Draw(rng, draws) < 0.4)
        {
            int pick = structureOrder[DrawIndex(rng, draws, structureOrder.Count)];
            if (!store.TryGetFootprint(pick, out var fp))
            {
                throw new InvalidOperationException($"结构 {pick} 无足迹。");
            }

            double xm = fp.Shape == CrowdStructureShape.Path
                ? fp.X0.ToDouble() / 100.0
                : fp.CenterXCm.ToDouble() / 100.0;
            double ym = fp.Shape == CrowdStructureShape.Path
                ? fp.Y0.ToDouble() / 100.0
                : fp.CenterYCm.ToDouble() / 100.0;
            CrowdStructureOps.RemoveStructureAt(session, Fix64.FromDouble(xm * 100.0), Fix64.FromDouble(ym * 100.0));
            return new CrowdWebFuzzOpRecord
            {
                Kind = "structure",
                Params = new JsonObject { ["action"] = "remove", ["x"] = xm, ["y"] = ym },
            };
        }

        int tIdx = DrawIndex(rng, draws, templates.Length);
        var template = templates[tIdx];
        var (x, y) = PickPoint(session, rng, draws, sizeM);
        double ang = Draw(rng, draws) * Math.PI * 2;
        double len = 200 + Draw(rng, draws) * 1500;
        double sizeVal = template.Footprint switch
        {
            CrowdStructureShape.Path => 40 + Draw(rng, draws) * 200,
            CrowdStructureShape.Disc => 80 + Draw(rng, draws) * 500,
            _ => 60 + Draw(rng, draws) * 800,
        };
        double clampW(double v) => Math.Min(sizeM - 1, Math.Max(0, v));
        double tx = clampW(x + Math.Cos(ang) * len);
        double ty = clampW(y + Math.Sin(ang) * len);
        int xCmPlace = (int)Math.Round(x * 100);
        int yCmPlace = (int)Math.Round(y * 100);
        int sizeCm = (int)Math.Round(sizeVal * 100);
        int toX = (int)Math.Round(tx * 100);
        int toY = (int)Math.Round(ty * 100);
        var cmd = new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = template.Id,
            XCm = xCmPlace,
            YCm = yCmPlace,
            SizeCm = sizeCm,
            ToXCm = toX,
            ToYCm = toY,
        };
        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);
        return new CrowdWebFuzzOpRecord
        {
            Kind = "structure",
            Params = new JsonObject
            {
                ["action"] = "place",
                ["template"] = template.Id,
                ["x"] = x,
                ["y"] = y,
                ["size"] = sizeVal,
                ["to"] = new JsonArray { tx, ty },
            },
        };
    }

    private static CrowdWebFuzzOpRecord ExecExplode(
        CrowdSimSession session, CrowdSimRng rng, List<uint> draws, double sizeM, CrowdWebFuzzBlast blast, int probeCount)
    {
        var (x, y) = PickPoint(session, rng, draws, sizeM);
        double r = 40 + Draw(rng, draws) * 400;
        double r2 = r * r;
        var probes = new List<(uint Handle, bool Dead)>(probeCount);
        int n = session.Units.Count;
        for (int t = 0; t < probeCount; t++)
        {
            int j = DrawIndex(rng, draws, n);
            var (ux, uy) = UnitPositionMeters(session, j);
            double dx = ux - x;
            double dy = uy - y;
            bool dead = dx * dx + dy * dy <= r2;
            probes.Add((session.Units.HandleAt(j), dead));
        }

        KillAtMeters(session, x, y, r, blast);
        foreach (var (handle, dead) in probes)
        {
            if (dead)
            {
                if (session.Units.TryResolveHandle(handle, out _))
                {
                    throw new InvalidOperationException($"句柄 {handle} 应在爆炸中死亡但仍可解析。");
                }
            }
            else if (!session.Units.TryResolveHandle(handle, out int dense) || dense < 0)
            {
                throw new InvalidOperationException($"句柄 {handle} 解析错误（幸存者）。");
            }
        }

        var probeArr = new JsonArray();
        foreach (var (handle, dead) in probes)
        {
            probeArr.Add(new JsonArray { handle, dead });
        }

        return new CrowdWebFuzzOpRecord
        {
            Kind = "explode",
            Params = new JsonObject
            {
                ["x"] = x,
                ["y"] = y,
                ["radius"] = r,
                ["probe"] = probeArr,
            },
        };
    }

    private static CrowdWebFuzzOpRecord ExecSpawn(CrowdSimSession session, CrowdSimRng rng, List<uint> draws, double sizeM)
    {
        var (x, y) = PickPoint(session, rng, draws, sizeM);
        int count = 1 + DrawIndex(rng, draws, 300);
        int pIndex = DrawIndex(rng, draws, session.Config.PairRules.PlayerCount);
        int unitType = DrawIndex(rng, draws, session.Config.UnitTypes.Count);
        int rIdx = DrawIndex(rng, draws, session.Config.RadiusClasses.Count);
        int player = pIndex + 1;
        int made = CrowdDeployment.SpawnAt(
            session,
            Fix64.FromDouble(x * 100.0),
            Fix64.FromDouble(y * 100.0),
            count,
            player,
            unitType,
            rIdx);
        return new CrowdWebFuzzOpRecord
        {
            Kind = "spawn",
            Params = new JsonObject
            {
                ["x"] = x,
                ["y"] = y,
                ["count"] = count,
                ["player"] = pIndex,
                ["unitType"] = unitType,
                ["rIdx"] = rIdx,
            },
        };
    }

    private static CrowdWebFuzzOpRecord ExecOrder(
        CrowdSimSession session, CrowdSimRng rng, List<uint> draws, double sizeM, bool[] selection, string[] formationIds)
    {
        int pIndex = DrawIndex(rng, draws, session.Config.PairRules.PlayerCount);
        int player = pIndex + 1;
        var (x, y) = PickPoint(session, rng, draws, sizeM);
        var (ax, ay) = PickPoint(session, rng, draws, sizeM);
        ClearSelection(session, selection);
        bool didSelect = Draw(rng, draws) < 0.6;
        if (didSelect)
        {
            SelectRect(session, player, selection, ax - 600, ay - 600, ax + 600, ay + 600, false);
        }

        double ang = Draw(rng, draws) * Math.PI * 2;
        bool drag = Draw(rng, draws) < 0.3;
        string shapeId = formationIds[DrawIndex(rng, draws, formationIds.Length)];
        bool auto = Draw(rng, draws) < 0.8;
        int widthCm = drag ? (int)Math.Round((100 + Draw(rng, draws) * 800) * 100) : 0;
        bool fogTerrain = Draw(rng, draws) < 0.5;
        Fix64? faceX = null;
        Fix64? faceY = null;
        if (drag)
        {
            faceX = Fix64.FromDouble(Math.Cos(ang));
            faceY = Fix64.FromDouble(Math.Sin(ang));
        }

        var members = CaptureSelected(session, player, selection);
        if (members.Length == 0)
        {
            members = CaptureAllOwn(session, player);
        }

        if (members.Length > 0)
        {
            var cmd = new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.Order,
                Player = player,
                XCm = (int)Math.Round(x * 100),
                YCm = (int)Math.Round(y * 100),
                ShapeId = shapeId,
                HasAuto = true,
                Auto = auto,
                HasFace = drag,
                FaceX = faceX ?? Fix64.Zero,
                FaceY = faceY ?? Fix64.Zero,
                WidthCm = widthCm,
                HasFogTerrain = true,
                FogTerrain = fogTerrain,
                Members = members,
            };
            session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
        }
        return new CrowdWebFuzzOpRecord
        {
            Kind = "order",
            Params = new JsonObject
            {
                ["player"] = pIndex,
                ["x"] = x,
                ["y"] = y,
                ["select"] = didSelect,
                ["ax"] = ax,
                ["ay"] = ay,
                ["drag"] = drag,
            },
        };
    }

    private static void SelectRect(
        CrowdSimSession session, int player, bool[] selection, double x0, double y0, double x1, double y1, bool additive)
    {
        double ax = Math.Min(x0, x1);
        double bx = Math.Max(x0, x1);
        double ay = Math.Min(y0, y1);
        double by = Math.Max(y0, y1);
        double axCm = ax * 100;
        double bxCm = bx * 100;
        double ayCm = ay * 100;
        double byCm = by * 100;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var entity = session.Units.EntityAt(i);
            if (session.World.Get<PlayerOwner>(entity).PlayerId != player)
            {
                selection[i] = false;
                continue;
            }

            var (xm, ym) = UnitPositionMeters(session, i);
            double xcm = xm * 100;
            double ycm = ym * 100;
            bool inside = xcm >= axCm && xcm <= bxCm && ycm >= ayCm && ycm <= byCm;
            if (inside) selection[i] = true;
            else if (!additive) selection[i] = false;
        }
    }

    private static void ClearSelection(CrowdSimSession session, bool[] selection)
    {
        for (int i = 0; i < selection.Length; i++) selection[i] = false;
    }

    private static uint[] CaptureAllOwn(CrowdSimSession session, int player)
    {
        int n = session.Units.Count;
        var list = new List<uint>();
        for (int i = 0; i < n; i++)
        {
            var entity = session.Units.EntityAt(i);
            if (session.World.Get<PlayerOwner>(entity).PlayerId != player) continue;
            list.Add(session.Units.HandleAt(i));
        }

        return list.ToArray();
    }

    private static uint[] CaptureSelected(CrowdSimSession session, int player, bool[] selection)
    {
        int n = session.Units.Count;
        var list = new List<uint>();
        for (int i = 0; i < n; i++)
        {
            if (!selection[i]) continue;
            var entity = session.Units.EntityAt(i);
            if (session.World.Get<PlayerOwner>(entity).PlayerId != player) continue;
            list.Add(session.Units.HandleAt(i));
        }

        return list.ToArray();
    }

    private static void StepTwice(CrowdSimSession session)
    {
        int guard = 64;
        for (int i = 0; i < 2; i++)
        {
            while (!session.Step(out _))
            {
                if (--guard <= 0)
                {
                    throw new InvalidOperationException("会话推进停摆过长。");
                }
            }
        }
    }

    private static string? TryValidate(CrowdSimSession session, string opKind)
    {
        try
        {
            CrowdSimulationValidator.ValidateSim(session);
            if (opKind == "structure")
            {
                CrowdSimulationValidator.ValidateStructures(session);
                CrowdSimulationValidator.ValidateNav(session);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    internal static long PositionStatL1Cm(CrowdSimSession session)
    {
        long sum = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var (xm, ym) = UnitPositionMeters(session, i);
            sum += (long)Math.Round(xm * 100.0) + (long)Math.Round(ym * 100.0);
        }

        return sum;
    }
}
