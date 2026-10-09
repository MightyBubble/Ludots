using System;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;

namespace TerrainLodCullingAcceptanceMod;

/// <summary>
/// 地形 LOD/裁剪验收镜头脚本：四段确定性运镜（帧计数驱动，presentation 阶段每帧必达），
/// B3 低空巡飞 → D2 斜视旋转 → D1 拉远驻留 overview → 快速回切。
/// </summary>
internal sealed class TerrainLodCameraScriptSystem : ISystem<float>
{
    private const string MapId = "terrain_lod_culling_acceptance";
    private const string VirtualCameraId = "TerrainLod.Script";

    private const int PhaseAFlyoverEndFrame = 300;
    private const int PhaseBRotateEndFrame = 480;
    private const int PhaseCOverviewEndFrame = 1020;
    private const int PhaseDReentryEndFrame = 1080;

    private readonly GameEngine _engine;
    private int _frame;

    public TerrainLodCameraScriptSystem(GameEngine engine)
    {
        _engine = engine;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase))
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
        ResolveScriptedPose(frame, out float targetX, out float targetY, out float yawDeg, out float pitchDeg, out float distanceCm);
        camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = VirtualCameraId,
            TargetCm = new System.Numerics.Vector2(targetX, targetY),
            Yaw = yawDeg,
            Pitch = pitchDeg,
            DistanceCm = distanceCm,
        });
        if (frame % 60 == 0)
        {
            Console.WriteLine(
                $"[TerrainLodCam] frame={frame} pose=({targetX:F0},{targetY:F0}) yaw={yawDeg:F0} pitch={pitchDeg:F0} dist={distanceCm:F0}");
        }
    }

    private static void ResolveScriptedPose(
        int frame,
        out float targetX,
        out float targetY,
        out float yawDeg,
        out float pitchDeg,
        out float distanceCm)
    {
        if (frame <= PhaseAFlyoverEndFrame)
        {
            float t = frame / (float)PhaseAFlyoverEndFrame;
            targetX = Lerp(90_000f, 320_000f, t);
            targetY = Lerp(90_000f, 320_000f, t);
            yawDeg = 40f;
            pitchDeg = 46f;
            distanceCm = 5_200f;
            return;
        }

        if (frame <= PhaseBRotateEndFrame)
        {
            float t = (frame - PhaseAFlyoverEndFrame) / (float)(PhaseBRotateEndFrame - PhaseAFlyoverEndFrame);
            targetX = 204_800f;
            targetY = 204_800f;
            yawDeg = Lerp(210f, 30f, t);
            pitchDeg = 36f;
            distanceCm = 26_000f;
            return;
        }

        if (frame <= PhaseCOverviewEndFrame)
        {
            float t = Math.Min(1f, (frame - PhaseBRotateEndFrame) / 60f);
            targetX = 204_800f;
            targetY = 204_800f;
            yawDeg = 40f;
            pitchDeg = Lerp(36f, 62f, t);
            distanceCm = Lerp(26_000f, 900_000f, Smooth(t));
            return;
        }

        if (frame <= PhaseDReentryEndFrame)
        {
            float t = Math.Min(1f, (frame - PhaseCOverviewEndFrame) / 12f);
            targetX = 204_800f;
            targetY = 204_800f;
            yawDeg = 40f;
            pitchDeg = Lerp(62f, 46f, t);
            distanceCm = Lerp(900_000f, 5_200f, Smooth(t));
            return;
        }

        targetX = 204_800f;
        targetY = 204_800f;
        yawDeg = 40f;
        pitchDeg = 46f;
        distanceCm = 5_200f;
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + ((to - from) * t);
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - (2f * t));
    }
}
