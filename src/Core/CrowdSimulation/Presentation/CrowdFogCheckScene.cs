using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 迷雾核对场景资产。字段与 regress/fog-acceptance.json 对齐，缺字段或未知键失败。
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
        if (string.IsNullOrWhiteSpace(scene.WallTemplate) || string.IsNullOrWhiteSpace(scene.UnseenTemplate))
            throw new InvalidOperationException($"{path}: 结构模板不能为空。");
        return scene;
    }
}
