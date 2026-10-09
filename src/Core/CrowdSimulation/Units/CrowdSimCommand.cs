using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>指令种类(Exec 分发全集;名字与 JSON type 字段一一对应,报错文案用 KindName)。</summary>
public enum CrowdSimCommandKind : byte
{
    Spawn,
    SpawnAt,
    Select,
    SelectAll,
    ClearSelection,
    Order,
    FogSight,
    Reveal,
    Obscure,
    Forget,
    FogShare,
    PlaceStructure,
    RemoveStructureAt,
}

/// <summary>
/// 指令载荷(数据):类型/玩家/坐标/形状/选项在入队边界一次成形,JsonNode 只在边界出现
/// (脚本配置/测试载荷 → Parse;演示现场指令直接构造 struct)。日志/队列/回放/执行共用
/// 同一份 struct,不再持有 JSON 树;内嵌形状与模板/阵型串解析后只读,拷贝 struct 即安全。
/// 坐标存厘米整数,Fix64 换算沿用原执行点口径(FromInt);face 在边界 FromDouble 一次成形。
/// 会话域校验(玩家号表外/迷雾启用)在入队 Validate,不在这里。
/// </summary>
public readonly record struct CrowdSimCommand
{
    public CrowdSimCommandKind Kind { get; init; }
    /// <summary>spawnAt/select/selectAll/order/reveal/obscure/forget/fogShare。</summary>
    public int Player { get; init; }
    /// <summary>fogShare 的并组对象。</summary>
    public int With { get; init; }
    /// <summary>spawn/spawnAt 的数量。</summary>
    public int Count { get; init; }
    /// <summary>spawnAt 的兵种与半径级。</summary>
    public int UnitType { get; init; }
    public int RIdx { get; init; }
    /// <summary>spawnAt/order/placeStructure/removeStructureAt 的目标点。</summary>
    public int XCm { get; init; }
    public int YCm { get; init; }
    /// <summary>select 的框。</summary>
    public int X0Cm { get; init; }
    public int Y0Cm { get; init; }
    public int X1Cm { get; init; }
    public int Y1Cm { get; init; }
    /// <summary>order 的阵型宽(缺省 0)。</summary>
    public int WidthCm { get; init; }
    /// <summary>obscure 的持续 tick(Parse 保证 ≥1)。</summary>
    public int Ticks { get; init; }
    /// <summary>placeStructure 的尺寸。</summary>
    public int SizeCm { get; init; }
    /// <summary>placeStructure 的终点(0 = 无终点,道路条带用)。</summary>
    public int ToXCm { get; init; }
    public int ToYCm { get; init; }
    /// <summary>select 的加选。</summary>
    public bool Additive { get; init; }
    /// <summary>fogSight 的 LOS 开关/fogShare 的并组方向(Parse:未给 on 视为 true;
    /// fogShare 的 on=false 拆组在边界拒收,Exec 只见 true)。</summary>
    public bool On { get; init; }
    /// <summary>order 的 auto 三态(未给 = null)。</summary>
    public bool HasAuto { get; init; }
    public bool Auto { get; init; }
    /// <summary>order 的朝向(未给 = 无)。</summary>
    public bool HasFace { get; init; }
    public Fix64 FaceX { get; init; }
    public Fix64 FaceY { get; init; }
    /// <summary>order 的乐观迷雾开关(未给 = 不切)。</summary>
    public bool HasFogTerrain { get; init; }
    public bool FogTerrain { get; init; }
    /// <summary>order 的阵型 id(开放词表,聚合键原样透传;缺省 "box")。</summary>
    public string? ShapeId { get; init; }
    /// <summary>placeStructure 的模板 id。</summary>
    public string? TemplateId { get; init; }
    /// <summary>reveal/obscure/forget 的区域形状(Parse 经 CrowdFogShape 恰一校验)。</summary>
    public CrowdFogShape? Area { get; init; }

    /// <summary>报错文案用的 type 名(与 JSON type 字段一致)。</summary>
    public static string KindName(CrowdSimCommandKind kind) => kind switch
    {
        CrowdSimCommandKind.Spawn => "spawn",
        CrowdSimCommandKind.SpawnAt => "spawnAt",
        CrowdSimCommandKind.Select => "select",
        CrowdSimCommandKind.SelectAll => "selectAll",
        CrowdSimCommandKind.ClearSelection => "clearSelection",
        CrowdSimCommandKind.Order => "order",
        CrowdSimCommandKind.FogSight => "fogSight",
        CrowdSimCommandKind.Reveal => "reveal",
        CrowdSimCommandKind.Obscure => "obscure",
        CrowdSimCommandKind.Forget => "forget",
        CrowdSimCommandKind.FogShare => "fogShare",
        CrowdSimCommandKind.PlaceStructure => "placeStructure",
        CrowdSimCommandKind.RemoveStructureAt => "removeStructureAt",
        _ => kind.ToString(),
    };

    /// <summary>
    /// 边界解析:JSON 指令 → struct 一次成形(结构域坏指令在此以合同异常拒收——字段缺失/
    /// 类型错/area 恰一/obscure ticks 值/fogShare 拆组,坏指令进不了日志与队列)。
    /// 会话域校验(玩家号表外/迷雾启用)留在入队 Validate。
    /// </summary>
    public static CrowdSimCommand Parse(JsonNode cmd)
    {
        string type = cmd["type"]?.GetValue<string>() ?? throw new InvalidOperationException("指令缺少 type 字段。");
        switch (type)
        {
            case "spawn":
                return new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = RequireInt(cmd, type, "count") };
            case "spawnAt":
                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.SpawnAt,
                    Player = RequireInt(cmd, type, "player"),
                    XCm = RequireInt(cmd, type, "xCm"),
                    YCm = RequireInt(cmd, type, "yCm"),
                    Count = RequireInt(cmd, type, "count"),
                    UnitType = RequireInt(cmd, type, "unitType"),
                    RIdx = RequireInt(cmd, type, "rIdx"),
                };
            case "select":
                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.Select,
                    Player = RequireInt(cmd, type, "player"),
                    X0Cm = RequireInt(cmd, type, "x0Cm"),
                    Y0Cm = RequireInt(cmd, type, "y0Cm"),
                    X1Cm = RequireInt(cmd, type, "x1Cm"),
                    Y1Cm = RequireInt(cmd, type, "y1Cm"),
                    Additive = OptionalBool(cmd, type, "additive", false),
                };
            case "selectAll":
                return new CrowdSimCommand { Kind = CrowdSimCommandKind.SelectAll, Player = RequireInt(cmd, type, "player") };
            case "clearSelection":
                return new CrowdSimCommand { Kind = CrowdSimCommandKind.ClearSelection };
            case "order":
            {
                var face = OptionalFace(cmd, type);
                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.Order,
                    Player = RequireInt(cmd, type, "player"),
                    XCm = RequireInt(cmd, type, "xCm"),
                    YCm = RequireInt(cmd, type, "yCm"),
                    ShapeId = cmd["shape"]?.GetValue<string>() ?? "box",
                    HasAuto = cmd["auto"] is { },
                    Auto = OptionalBool(cmd, type, "auto", false),
                    HasFace = face.HasValue,
                    FaceX = face?.X ?? Fix64.Zero,
                    FaceY = face?.Y ?? Fix64.Zero,
                    WidthCm = OptionalInt(cmd, type, "widthCm", 0),
                    HasFogTerrain = cmd["fogTerrain"] is { },
                    FogTerrain = OptionalBool(cmd, type, "fogTerrain", false),
                };
            }

            case "fogSight":
                return new CrowdSimCommand { Kind = CrowdSimCommandKind.FogSight, On = OptionalBool(cmd, type, "on", true) };
            case "fogShare":
                if (OptionalBool(cmd, type, "on", true) == false)
                {
                    throw new InvalidOperationException("fogShare: 拆分视野组尚未支持(需显式重组命令)。");
                }

                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.FogShare,
                    Player = RequireInt(cmd, type, "player"),
                    With = RequireInt(cmd, type, "with"),
                    On = true,
                };
            case "reveal":
            case "obscure":
            case "forget":
            {
                int ticks = 0;
                if (type == "obscure")
                {
                    ticks = cmd["ticks"] is { }
                        ? RequireInt(cmd, type, "ticks")
                        : throw new InvalidOperationException("obscure: 缺少 ticks(需为 ≥1 的整数)。");
                    if (ticks < 1) throw new InvalidOperationException("obscure: ticks 需为 ≥1 的整数。");
                }

                return new CrowdSimCommand
                {
                    Kind = type == "reveal" ? CrowdSimCommandKind.Reveal
                        : type == "obscure" ? CrowdSimCommandKind.Obscure
                        : CrowdSimCommandKind.Forget,
                    Player = RequireInt(cmd, type, "player"),
                    Ticks = ticks,
                    Area = Fog.CrowdFogShape.Parse(type, cmd),
                };
            }

            case "placeStructure":
                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.PlaceStructure,
                    TemplateId = cmd["template"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("placeStructure: 缺少 template 字段。"),
                    XCm = RequireInt(cmd, type, "xCm"),
                    YCm = RequireInt(cmd, type, "yCm"),
                    SizeCm = RequireInt(cmd, type, "sizeCm"),
                    ToXCm = OptionalInt(cmd, type, "toXCm", 0),
                    ToYCm = OptionalInt(cmd, type, "toYCm", 0),
                };
            case "removeStructureAt":
                return new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.RemoveStructureAt,
                    XCm = RequireInt(cmd, type, "xCm"),
                    YCm = RequireInt(cmd, type, "yCm"),
                };
            default:
                throw new InvalidOperationException($"未知指令 {type}。");
        }
    }

    /// <summary>必给整数字段:缺失或 JSON 值不是整数 token 即拒(GetValue 的转换异常不是合同)。</summary>
    private static int RequireInt(JsonNode cmd, string type, string field)
    {
        return cmd[field] is JsonValue v && v.TryGetValue<int>(out int value) && v.GetValueKind() == JsonValueKind.Number
            ? value
            : throw new InvalidOperationException($"指令 {type} 的 {field} 字段需为整数。");
    }

    private static int OptionalInt(JsonNode cmd, string type, string field, int fallback) =>
        cmd[field] is { } ? RequireInt(cmd, type, field) : fallback;

    private static bool OptionalBool(JsonNode cmd, string type, string field, bool fallback)
    {
        if (cmd[field] is not { } node) return fallback;
        return node is JsonValue v && v.TryGetValue<bool>(out bool value)
            ? value
            : throw new InvalidOperationException($"指令 {type} 的 {field} 字段需为布尔。");
    }

    /// <summary>order 的 face [x, y](双精度,边界 FromDouble 一次成形;未给 = 无)。</summary>
    private static (Fix64 X, Fix64 Y)? OptionalFace(JsonNode cmd, string type)
    {
        if (cmd["face"] is not { } f) return null;
        if (f is not JsonArray fa || fa.Count != 2 ||
            fa[0] is not JsonValue x || fa[1] is not JsonValue y ||
            !x.TryGetValue<double>(out double fx) || !y.TryGetValue<double>(out double fy))
        {
            throw new InvalidOperationException($"指令 {type} 的 face 字段需为 [x, y]。");
        }

        return (Fix64.FromDouble(fx), Fix64.FromDouble(fy));
    }
}
