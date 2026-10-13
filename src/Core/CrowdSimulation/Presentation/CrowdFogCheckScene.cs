using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 玩家视图迷雾核对场景。坐标和模板与回归夹具同一套含义；
/// 机位、间距只服务审阅，回归夹具不走这个类型。缺字段或未知键失败。
/// </summary>
internal sealed class CrowdFogCheckScene
{
    [JsonRequired] public int ViewerPlayer { get; init; }
    [JsonRequired] public int AllyPlayer { get; init; }
    [JsonRequired] public int EnemyPlayer { get; init; }
    [JsonRequired] public int UnitType { get; init; }
    [JsonRequired] public int RIdx { get; init; }
    [JsonRequired] public int SpawnCount { get; init; }
    [JsonRequired] public int ViewerXCm { get; init; }
    [JsonRequired] public int ViewerYCm { get; init; }
    [JsonRequired] public int EnemyXCm { get; init; }
    [JsonRequired] public int EnemyYCm { get; init; }
    [JsonRequired] public string WallTemplate { get; init; } = "";
    [JsonRequired] public int WallSizeCm { get; init; }
    [JsonRequired] public int WallCellsEast { get; init; }
    [JsonRequired] public int BehindCellsEast { get; init; }
    [JsonRequired] public string UnseenTemplate { get; init; } = "";
    [JsonRequired] public int UnseenXCm { get; init; }
    [JsonRequired] public int UnseenYCm { get; init; }
    [JsonRequired] public int UnseenSizeCm { get; init; }
    [JsonRequired] public int LeaveXCm { get; init; }
    [JsonRequired] public int LeaveYCm { get; init; }
    [JsonRequired] public int WaterXCm { get; init; }
    [JsonRequired] public int WaterYCm { get; init; }
    [JsonRequired] public int GoalXCm { get; init; }
    [JsonRequired] public int GoalYCm { get; init; }
    [JsonRequired] public string OrderShape { get; init; } = "";
    [JsonRequired] public int VisionTicks { get; init; }
    [JsonRequired] public int TravelTicks { get; init; }
    [JsonRequired] public int ClusterSpacingCm { get; init; }
    [JsonRequired] public int OutsideLookXCm { get; init; }
    [JsonRequired] public int OutsideLookYCm { get; init; }
    [JsonRequired] public float OutsideDistanceCm { get; init; }
    [JsonRequired] public float OutsideYaw { get; init; }
    [JsonRequired] public float OutsidePitch { get; init; }
    [JsonRequired] public int BehindLookXCm { get; init; }
    [JsonRequired] public int BehindLookYCm { get; init; }
    [JsonRequired] public float BehindDistanceCm { get; init; }
    [JsonRequired] public float BehindYaw { get; init; }
    [JsonRequired] public float BehindPitch { get; init; }

    public static CrowdFogCheckScene Load(JsonElement element, string path)
    {
        CrowdFogCheckScene scene;
        try
        {
            scene = element.Deserialize<CrowdFogCheckScene>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{path}: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path}: {ex.Message}", ex);
        }

        if (scene.ViewerPlayer <= 0 || scene.EnemyPlayer <= 0 || scene.AllyPlayer <= 0)
            throw new InvalidOperationException($"{path}: 玩家号必须 > 0。");
        if (scene.SpawnCount < 1 || scene.WallSizeCm < 1 || scene.UnseenSizeCm < 1)
            throw new InvalidOperationException($"{path}: spawnCount 与结构尺寸必须 > 0。");
        if (scene.WallCellsEast < 1 || scene.BehindCellsEast < 1)
            throw new InvalidOperationException($"{path}: wallCellsEast 与 behindCellsEast 必须 > 0。");
        if (scene.ClusterSpacingCm < 1)
            throw new InvalidOperationException($"{path}: clusterSpacingCm 必须 > 0。");
        RequireCamera(path, "outside", scene.OutsideDistanceCm, scene.OutsideYaw, scene.OutsidePitch);
        RequireCamera(path, "behind", scene.BehindDistanceCm, scene.BehindYaw, scene.BehindPitch);
        if (string.IsNullOrWhiteSpace(scene.WallTemplate) || string.IsNullOrWhiteSpace(scene.UnseenTemplate))
            throw new InvalidOperationException($"{path}: 结构模板不能为空。");
        return scene;
    }

    private static void RequireCamera(string path, string which, float distanceCm, float yaw, float pitch)
    {
        if (!float.IsFinite(distanceCm) || distanceCm <= 0f)
            throw new InvalidOperationException($"{path}: {which}DistanceCm 必须是 > 0 的有限数。");
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch))
            throw new InvalidOperationException($"{path}: {which}Yaw 与 {which}Pitch 必须是有限数。");
    }
}
