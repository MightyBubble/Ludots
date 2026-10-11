using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.Camera;

/// <summary>
/// 配置驱动的镜头姿态脚本:按帧在关键相位之间插值,再交给虚拟相机。
/// 相位边界与插值通道来自 Camera/pose_script.json;没有这份目录登记时不装配。
/// </summary>
internal sealed class CameraPoseScriptSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CameraPoseScript _script;
    private int _frame;

    public CameraPoseScriptSystem(GameEngine engine, CameraPoseScript script)
    {
        _engine = engine;
        _script = script;
    }

    public static void TryInstall(GameEngine engine)
    {
        var catalog = engine.ConfigCatalog;
        if (catalog == null || !catalog.TryGet(CameraPoseScript.CatalogPath, out var entry)) return;
        var node = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in entry, engine.ConfigConflictReport)
            ?? throw new InvalidOperationException($"{CameraPoseScript.CatalogPath} 存在目录登记但合并为空。");
        engine.RegisterPresentationSystem(new CameraPoseScriptSystem(engine, CameraPoseScript.Parse(node)));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, _script.MapId, StringComparison.OrdinalIgnoreCase))
        {
            _frame = 0;
            return;
        }

        CameraManager? camera;
        try
        {
            camera = ClientLocalSeatAccess.ResolveAuthorityCamera(_engine);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        int frame = _frame++;
        _script.Resolve(frame, out float targetX, out float targetY, out float yawDeg, out float pitchDeg, out float distanceCm);
        camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = _script.VirtualCameraId,
            TargetCm = new Vector2(targetX, targetY),
            Yaw = yawDeg,
            Pitch = pitchDeg,
            DistanceCm = distanceCm,
        });
        if (_script.LogEveryFrames > 0 && frame % _script.LogEveryFrames == 0)
        {
            Console.WriteLine(
                $"[TerrainLodCam] frame={frame} pose=({targetX:F0},{targetY:F0}) yaw={yawDeg:F0} pitch={pitchDeg:F0} dist={distanceCm:F0}");
        }
    }
}

internal sealed class CameraPoseScript
{
    public const string CatalogPath = "Camera/pose_script.json";

    public required string MapId { get; init; }
    public required string VirtualCameraId { get; init; }
    public int LogEveryFrames { get; init; }
    public required Phase LinearA { get; init; }
    public required Phase YawSweep { get; init; }
    public required Phase Pullback { get; init; }
    public required Phase Reentry { get; init; }
    public required Pose Hold { get; init; }

    public readonly record struct Pose(float X, float Y, float Yaw, float Pitch, float DistanceCm);

    public readonly record struct Phase(int EndFrame, Pose From, Pose To, int EaseFrames, bool SmoothDistance);

    public static CameraPoseScript Parse(JsonObject node)
    {
        return new CameraPoseScript
        {
            MapId = Req(node, "mapId"),
            VirtualCameraId = Req(node, "virtualCameraId"),
            LogEveryFrames = node["logEveryFrames"]?.GetValue<int>() ?? 0,
            LinearA = PhaseOf(node, "flyover", smoothDistance: false),
            YawSweep = PhaseOf(node, "yawSweep", smoothDistance: false),
            Pullback = PhaseOf(node, "pullback", smoothDistance: true),
            Reentry = PhaseOf(node, "reentry", smoothDistance: true),
            Hold = PoseOf(Obj(node, "hold")),
        };
    }

    public void Resolve(int frame, out float x, out float y, out float yaw, out float pitch, out float distance)
    {
        if (frame <= LinearA.EndFrame) { Sample(LinearA, frame, 0, out x, out y, out yaw, out pitch, out distance); return; }
        if (frame <= YawSweep.EndFrame) { Sample(YawSweep, frame, LinearA.EndFrame, out x, out y, out yaw, out pitch, out distance); return; }
        if (frame <= Pullback.EndFrame) { Sample(Pullback, frame, YawSweep.EndFrame, out x, out y, out yaw, out pitch, out distance); return; }
        if (frame <= Reentry.EndFrame) { Sample(Reentry, frame, Pullback.EndFrame, out x, out y, out yaw, out pitch, out distance); return; }
        x = Hold.X; y = Hold.Y; yaw = Hold.Yaw; pitch = Hold.Pitch; distance = Hold.DistanceCm;
    }

    private static void Sample(Phase phase, int frame, int start, out float x, out float y, out float yaw, out float pitch, out float distance)
    {
        float span = phase.EaseFrames > 0 ? phase.EaseFrames : Math.Max(1, phase.EndFrame - start);
        float t = Math.Min(1f, (frame - start) / span);
        float move = phase.SmoothDistance ? Smooth(t) : t;
        x = Lerp(phase.From.X, phase.To.X, t);
        y = Lerp(phase.From.Y, phase.To.Y, t);
        yaw = Lerp(phase.From.Yaw, phase.To.Yaw, t);
        pitch = Lerp(phase.From.Pitch, phase.To.Pitch, t);
        distance = Lerp(phase.From.DistanceCm, phase.To.DistanceCm, move);
    }

    private static Phase PhaseOf(JsonObject root, string name, bool smoothDistance)
    {
        var node = Obj(root, name);
        int end = node["endFrame"]?.GetValue<int>() ?? throw new InvalidOperationException($"Camera/pose_script.json {name}.endFrame 必填。");
        int ease = node["easeFrames"]?.GetValue<int>() ?? 0;
        return new Phase(end, PoseOf(Obj(node, "from")), PoseOf(Obj(node, "to")), ease, smoothDistance);
    }

    private static Pose PoseOf(JsonObject node) => new(
        Num(node, "x"), Num(node, "y"), Num(node, "yaw"), Num(node, "pitch"), Num(node, "distanceCm"));

    private static JsonObject Obj(JsonObject node, string name) =>
        node[name] as JsonObject ?? throw new InvalidOperationException($"Camera/pose_script.json {name} 必填。");

    private static string Req(JsonObject node, string name) =>
        node[name]?.GetValue<string>() ?? throw new InvalidOperationException($"Camera/pose_script.json {name} 必填。");

    private static float Num(JsonObject node, string name) =>
        node[name]?.GetValue<float>() ?? throw new InvalidOperationException($"Camera/pose_script.json 姿态缺少 {name}。");

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
