using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Gameplay.Camera;

/// <summary>
/// 配置驱动的镜头姿态脚本:按固定步采样有序关键帧,产出 <see cref="CameraPoseRequest"/>。
/// 采样只看关键帧,不解释相位名。没有 Camera/pose_script.json 的目录登记时不装配。
/// 排在 <see cref="CameraRuntimeSystem"/> 之前,由它在同一固定步消费请求。
/// </summary>
internal sealed class CameraPoseScriptSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CameraPoseScript _script;
    private int _tick;

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
        engine.InsertSystemBeforeRequired<CameraRuntimeSystem>(
            new CameraPoseScriptSystem(engine, CameraPoseScript.Parse(node)),
            SystemGroup.AttributeCalculation);
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
            _tick = 0;
            return;
        }

        CameraPoseSample sample = _script.Sample(_tick++);
        _engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = _script.VirtualCameraId,
            TargetCm = new Vector2(sample.X, sample.Y),
            Yaw = sample.Yaw,
            Pitch = sample.Pitch,
            DistanceCm = sample.DistanceCm,
        });
    }
}

internal enum CameraChannelEase
{
    Linear,
    Smoothstep,
}

internal readonly struct CameraPoseSample
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float DistanceCm { get; init; }
}

/// <summary>
/// 关键帧左闭右闭:frame 落在 (上一帧终点, 本帧 endFrame] 内按通道插值。
/// 第一段从 0 起。超过最后一帧后保持最后一帧的 to。各通道 ease 独立。
/// </summary>
internal sealed class CameraPoseScript
{
    public const string CatalogPath = "Camera/pose_script.json";

    public required string MapId { get; init; }
    public required string VirtualCameraId { get; init; }
    public required CameraKeyframe[] Keyframes { get; init; }

    public static CameraPoseScript Parse(JsonNode node)
    {
        CameraPoseScriptDto dto;
        try
        {
            dto = node.Deserialize<CameraPoseScriptDto>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{CatalogPath} 反序列化为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{CatalogPath} 严格反序列化失败: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(dto.MapId))
            throw new InvalidOperationException($"{CatalogPath} mapId 必填。");
        if (string.IsNullOrWhiteSpace(dto.VirtualCameraId))
            throw new InvalidOperationException($"{CatalogPath} virtualCameraId 必填。");
        if (dto.Keyframes == null || dto.Keyframes.Length == 0)
            throw new InvalidOperationException($"{CatalogPath} keyframes 至少一帧。");

        var frames = new CameraKeyframe[dto.Keyframes.Length];
        int previousEnd = 0;
        for (int i = 0; i < dto.Keyframes.Length; i++)
        {
            CameraKeyframeDto frame = dto.Keyframes[i]
                ?? throw new InvalidOperationException($"{CatalogPath} keyframes[{i}] 为空。");
            if (frame.EndFrame <= previousEnd)
            {
                throw new InvalidOperationException(
                    $"{CatalogPath} keyframes[{i}].endFrame 必须大于上一帧终点 {previousEnd}。");
            }

            frames[i] = new CameraKeyframe(
                frame.EndFrame,
                TargetAxis(frame.Target, 0, $"{CatalogPath} keyframes[{i}].target"),
                TargetAxis(frame.Target, 1, $"{CatalogPath} keyframes[{i}].target"),
                Scalar(frame.Yaw, $"{CatalogPath} keyframes[{i}].yaw"),
                Scalar(frame.Pitch, $"{CatalogPath} keyframes[{i}].pitch"),
                Scalar(frame.Distance, $"{CatalogPath} keyframes[{i}].distance"));
            previousEnd = frame.EndFrame;
        }

        return new CameraPoseScript
        {
            MapId = dto.MapId,
            VirtualCameraId = dto.VirtualCameraId,
            Keyframes = frames,
        };
    }

    public CameraPoseSample Sample(int frame)
    {
        int start = 0;
        for (int i = 0; i < Keyframes.Length; i++)
        {
            CameraKeyframe key = Keyframes[i];
            if (frame <= key.EndFrame)
            {
                float span = key.EndFrame - start;
                float t = span <= 0 ? 1f : (frame - start) / (float)span;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                return new CameraPoseSample
                {
                    X = key.TargetX.Sample(t),
                    Y = key.TargetY.Sample(t),
                    Yaw = key.Yaw.Sample(t),
                    Pitch = key.Pitch.Sample(t),
                    DistanceCm = key.Distance.Sample(t),
                };
            }

            start = key.EndFrame;
        }

        CameraKeyframe last = Keyframes[Keyframes.Length - 1];
        return new CameraPoseSample
        {
            X = last.TargetX.To,
            Y = last.TargetY.To,
            Yaw = last.Yaw.To,
            Pitch = last.Pitch.To,
            DistanceCm = last.Distance.To,
        };
    }

    private static CameraScalarChannel TargetAxis(CameraTargetChannelDto? channel, int axis, string path)
    {
        if (channel == null) throw new InvalidOperationException($"{path} 必填。");
        if (channel.From == null || channel.From.Length != 2 || channel.To == null || channel.To.Length != 2)
            throw new InvalidOperationException($"{path}.from/to 需要 2 个分量。");
        return new CameraScalarChannel(channel.From[axis], channel.To[axis], ParseEase(channel.Ease, path));
    }

    private static CameraScalarChannel Scalar(CameraScalarChannelDto? channel, string path)
    {
        if (channel == null) throw new InvalidOperationException($"{path} 必填。");
        return new CameraScalarChannel(channel.From, channel.To, ParseEase(channel.Ease, path));
    }

    private static CameraChannelEase ParseEase(string? ease, string path)
    {
        if (ease == "linear") return CameraChannelEase.Linear;
        if (ease == "smoothstep") return CameraChannelEase.Smoothstep;
        throw new InvalidOperationException($"{path}.ease \"{ease}\" 需为 linear / smoothstep。");
    }
}

internal readonly struct CameraKeyframe
{
    public CameraKeyframe(
        int endFrame,
        CameraScalarChannel targetX,
        CameraScalarChannel targetY,
        CameraScalarChannel yaw,
        CameraScalarChannel pitch,
        CameraScalarChannel distance)
    {
        EndFrame = endFrame;
        TargetX = targetX;
        TargetY = targetY;
        Yaw = yaw;
        Pitch = pitch;
        Distance = distance;
    }

    public int EndFrame { get; }
    public CameraScalarChannel TargetX { get; }
    public CameraScalarChannel TargetY { get; }
    public CameraScalarChannel Yaw { get; }
    public CameraScalarChannel Pitch { get; }
    public CameraScalarChannel Distance { get; }
}

internal readonly struct CameraScalarChannel
{
    public CameraScalarChannel(float from, float to, CameraChannelEase ease)
    {
        From = from;
        To = to;
        Ease = ease;
    }

    public float From { get; }
    public float To { get; }
    public CameraChannelEase Ease { get; }

    public float Sample(float t)
    {
        float eased = Ease == CameraChannelEase.Smoothstep ? t * t * (3f - (2f * t)) : t;
        return From + ((To - From) * eased);
    }
}

internal sealed class CameraPoseScriptDto
{
    [JsonRequired]
    public string MapId { get; set; } = string.Empty;

    [JsonRequired]
    public string VirtualCameraId { get; set; } = string.Empty;

    [JsonRequired]
    public CameraKeyframeDto[] Keyframes { get; set; } = Array.Empty<CameraKeyframeDto>();
}

internal sealed class CameraKeyframeDto
{
    [JsonRequired]
    public int EndFrame { get; set; }

    [JsonRequired]
    public CameraTargetChannelDto Target { get; set; } = new();

    [JsonRequired]
    public CameraScalarChannelDto Yaw { get; set; } = new();

    [JsonRequired]
    public CameraScalarChannelDto Pitch { get; set; } = new();

    [JsonRequired]
    public CameraScalarChannelDto Distance { get; set; } = new();
}

internal sealed class CameraTargetChannelDto
{
    [JsonRequired]
    public float[] From { get; set; } = Array.Empty<float>();

    [JsonRequired]
    public float[] To { get; set; } = Array.Empty<float>();

    [JsonRequired]
    public string Ease { get; set; } = string.Empty;
}

internal sealed class CameraScalarChannelDto
{
    [JsonRequired]
    public float From { get; set; }

    [JsonRequired]
    public float To { get; set; }

    [JsonRequired]
    public string Ease { get; set; } = string.Empty;
}
