// sango 世界态 → WebUI DataPlane 投影(M1.c)。
// 数据源全部是内核单例(Scenario.Cur / PlayerMessage / SangoTurnDriver),本层只读投影,
// 不复制世界态、不造平行模型;消息流的两个真源见 SangoWorldFeed 注释。

using System.Text.Json;
using Sango.Core;
using Sango.Core.Player;
using Sango.Runtime;
using Ludots.WebUI.DataPlane;

namespace Sango.WebUi;

/// <summary>
/// 订阅方共享的世界消息流。三个真源:
///   1. 内核 PlayerMessage(原版玩家消息系统,经 onTextMessageAdd 回调投真源;当前
///      M1.b 全托管启动下无玩家势力,IsPlayer 门控路径暂不产消息,M3 玩家接入后自然激活);
///   2. SangoTurnDriver.DescribeTurn 的回合摘要行(内核侧确定性摘要,作为无玩家时的回合事件源);
///   3. SangoCombatAnnals 战报行(M2.c:战斗 GameEvent 群的逐事件 MUD 行,命令/回合内
///      即时入流,消息话题由 tick 泵自然刷新,不新增 topic)。
/// </summary>
public sealed class SangoWorldFeed
{
    public const int MaxMessages = 50;

    private readonly object _sync = new();
    private readonly Queue<SangoMessageRow> _messages = new();

    private long _seq;
    private int _tick;
    private PlayerMessage.PlayerTextMessageCallback? _playerMessageCallback;
    private SangoCombatAnnals? _combatAnnals;

    /// <summary>投影层发布序号(每次非订阅发布 +1,UI 据此感知数据新鲜度)。</summary>
    public int NextTick()
    {
        lock (_sync)
        {
            return ++_tick;
        }
    }

    /// <summary>订阅内核 PlayerMessage 真源(重复调用幂等;内核二次启动后仍指向当前系统实例)。</summary>
    public void AttachPlayerMessageSystem()
    {
        PlayerMessage system = GameSystem.GetSystem<PlayerMessage>()
            ?? throw new InvalidOperationException("PlayerMessage system is not registered in the sango kernel.");
        if (_playerMessageCallback != null)
        {
            system.onTextMessageAdd -= _playerMessageCallback;
        }

        _playerMessageCallback = (message, _) => AppendKernelMessage(message);
        system.onTextMessageAdd += _playerMessageCallback;
    }

    /// <summary>回合推进后由 Entry 调用:落一条 SangoTurnDriver 回合摘要行。</summary>
    public void NoteTurnAdvanced()
    {
        Append(SangoTurnDriver.DescribeTurn(), dateText: string.Empty);
    }

    /// <summary>挂 M2.c 战报采集器:战斗 GameEvent 群 → MUD 战报行,经既有消息流面板呈现
    /// (消息话题已由 tick 泵逐帧刷新,无需新 topic)。重复调用幂等。</summary>
    public void AttachCombatAnnals()
    {
        if (_combatAnnals != null)
        {
            return;
        }

        var annals = new SangoCombatAnnals();
        annals.LinePublished += line => Append(line, dateText: string.Empty);
        annals.Attach();
        _combatAnnals = annals;
    }

    /// <summary>已挂的战报采集器(战报面板话题的结构化真源;未挂即装配缺陷,fail-fast)。</summary>
    public SangoCombatAnnals CombatAnnals =>
        _combatAnnals ?? throw new InvalidOperationException(
            "SangoWorldFeed has no combat annals attached; call AttachCombatAnnals before serving battle topics.");

    public SangoMessageRow[] SnapshotMessages()
    {
        lock (_sync)
        {
            return _messages.ToArray();
        }
    }

    private void AppendKernelMessage(PlayerMessage.TextMessage message)
    {
        string dateText = message.year > 0 ? $"{message.year}年{message.month}月{message.day}日" : string.Empty;
        string text = message.text ?? string.Empty;
        Append(text, dateText);
    }

    private void Append(string text, string dateText)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; call SangoKernelBoot.Boot first.");
        lock (_sync)
        {
            _messages.Enqueue(new SangoMessageRow(++_seq, scenario.Info.turnCount, dateText, text));
            while (_messages.Count > MaxMessages)
            {
                _messages.Dequeue();
            }
        }
    }
}

public sealed class SangoWorldCitiesTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.cities";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldCitiesTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoCitiesSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            ProjectCities(scenario));
        packet = Packet(context, isSubscription, snapshot);
        return true;
    }

    internal static SangoCityRow[] ProjectCities(Scenario scenario)
    {
        var rows = new List<SangoCityRow>(scenario.citySet.Count);
        scenario.citySet.ForEach(city =>
        {
            if (city == null)
            {
                return;
            }

            Force? force = city.mBelongForce;
            rows.Add(new SangoCityRow(
                city.Id,
                city.Name ?? string.Empty,
                force?.Id ?? 0,
                force?.Name ?? string.Empty,
                city.population,
                city.gold,
                city.food,
                city.allPersons.Count));
        });
        return rows.ToArray();
    }

    private WebUiOutboundPacket Packet(in WebUiTopicContext context, bool isSubscription, SangoCitiesSnapshot snapshot)
    {
        return new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
    }
}

public sealed class SangoWorldForcesTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.forces";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldForcesTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoForcesSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            ProjectForces(scenario));
        packet = Packet(context, isSubscription, snapshot);
        return true;
    }

    internal static SangoForceRow[] ProjectForces(Scenario scenario)
    {
        var rows = new List<SangoForceRow>(scenario.forceSet.Count);
        scenario.forceSet.ForEach(force =>
        {
            if (force == null)
            {
                return;
            }

            int personCount = 0;
            force.ForEachPerson(_ => personCount++);
            rows.Add(new SangoForceRow(
                force.Id,
                force.Name ?? string.Empty,
                force.mGovernor?.Name ?? string.Empty,
                force.CityBaseCount,
                personCount,
                force.IsAlive));
        });
        return rows.ToArray();
    }

    private WebUiOutboundPacket Packet(in WebUiTopicContext context, bool isSubscription, SangoForcesSnapshot snapshot)
    {
        return new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
    }
}

public sealed class SangoWorldTurnTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.turn";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        ScenarioInfo info = scenario.Info;
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoTurnSnapshot(
            info.turnCount,
            info.year,
            info.month,
            info.day,
            scenario.GetDateStr(),
            SangoTurnDriver.DescribeTurn());
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }
}

public sealed class SangoWorldMessagesTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.messages";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldMessagesTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoMessagesSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            _feed.SnapshotMessages());
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }
}

/// <summary>
/// 单城详情(订阅参数 cityId 选城)。列表话题保持瘦行,详情数值与在城武将在此展开;
/// 订阅即得快照,命令与回合推进后由 UI 重新订阅刷新(见 WebApp useCityDetail)。
/// </summary>
public sealed class SangoWorldCityTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.city";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldCityTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        if (!TryReadCityId(context.Parameters, out int cityId) ||
            scenario.citySet.Get(cityId) is not { } city)
        {
            packet = null!;
            return false;
        }

        bool isSubscription = context.RequestId != 0;
        Force? force = city.mBelongForce;
        var snapshot = new SangoCityDetailSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            city.Id,
            city.Name ?? string.Empty,
            force?.Id ?? 0,
            force?.Name ?? string.Empty,
            city.population,
            city.gold,
            city.food,
            city.morale,
            city.MaxMorale,
            city.troops,
            city.troopsLimit,
            city.freePersons.Count,
            city.mBelongCorps?.ActionPoint ?? 0,
            ProjectPersons(city),
            ProjectWildPersons(city),
            ProjectCityTroops(scenario, city),
            ProjectTroopTypes(city));
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }

    internal static bool TryReadCityId(JsonElement parameters, out int cityId)
    {
        cityId = 0;
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("cityId", out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return value.TryGetInt32(out cityId) && cityId > 0;
    }

    internal static SangoPersonRow[] ProjectPersons(City city)
    {
        var rows = new List<SangoPersonRow>(city.allPersons.Count);
        foreach (Person person in city.allPersons)
        {
            if (person == null)
            {
                continue;
            }

            rows.Add(ToRow(person, free: city.freePersons.Contains(person)));
        }

        return rows.ToArray();
    }

    internal static SangoPersonRow[] ProjectWildPersons(City city)
    {
        var rows = new List<SangoPersonRow>(city.wildPersons.Count);
        foreach (Person person in city.wildPersons)
        {
            if (person == null)
            {
                continue;
            }

            rows.Add(ToRow(person, free: false));
        }

        return rows.ToArray();
    }

    private static SangoPersonRow ToRow(Person person, bool free)
    {
        return new SangoPersonRow(
            person.Id,
            person.Name ?? string.Empty,
            person.loyalty,
            person.state,
            free,
            person.Command,
            person.Strength,
            person.Intelligence,
            person.Politics,
            person.Glamour);
    }

    // 本城部队 = troopsSet 中 mBelongCity==城 的存活部队(编成城的所属关系)。
    internal static SangoCityTroopRow[] ProjectCityTroops(Scenario scenario, City city)
    {
        var rows = new List<SangoCityTroopRow>();
        scenario.troopsSet.ForEach(troop =>
        {
            if (troop == null || !troop.IsAlive || troop.mBelongCity != city)
            {
                return;
            }

            rows.Add(new SangoCityTroopRow(
                troop.Id,
                troop.Name ?? string.Empty,
                troop.x,
                troop.y,
                troop.troops,
                troop.food,
                troop.ActionOver));
        });
        return rows.ToArray();
    }

    // 出征表单的兵种选项(UICityExpedition.OnEnter 的可组兵种表,TroopType.
    // CheckActivTroopTypeList(城 freePersons));isLand 分组由 UI 呈现。
    internal static SangoTroopTypeRow[] ProjectTroopTypes(City city)
    {
        var active = new List<TroopType>();
        TroopType.CheckActivTroopTypeList(city.freePersons, active);
        return active
            .Select(type => new SangoTroopTypeRow(type.Id, type.Name ?? string.Empty, type.isLand))
            .ToArray();
    }
}

/// <summary>
/// 部队列表(M2.b):troopsSet 存活部队的瘦行投影(势力过滤在 UI 侧)。
/// 行字段即 digest 的部队语义(id/corps/force/cell/兵力)加表单展示项(粮/士气/移动力/待命)。
/// M2.d 增任务态:missionType/missionLabel(委任目标的提示按机会主义语义表述,
/// 见 SangoMissionLabels)。
/// </summary>
public sealed class SangoWorldTroopsTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.troops";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldTroopsTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoTroopsSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            ProjectTroops(scenario));
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }

    internal static SangoTroopRow[] ProjectTroops(Scenario scenario)
    {
        var rows = new List<SangoTroopRow>(scenario.troopsSet.Count);
        scenario.troopsSet.ForEach(troop =>
        {
            if (troop == null || !troop.IsAlive)
            {
                return;
            }

            rows.Add(new SangoTroopRow(
                troop.Id,
                troop.Name ?? string.Empty,
                troop.mBelongForce?.Id ?? 0,
                troop.mBelongForce?.Name ?? string.Empty,
                troop.mBelongCorps?.Id ?? 0,
                troop.mBelongCity?.Id ?? 0,
                troop.x,
                troop.y,
                troop.troops,
                troop.food,
                troop.morale,
                troop.MoveAbility,
                troop.ActionOver,
                troop.LandTroopType?.Name ?? string.Empty,
                troop.missionType,
                SangoMissionLabels.Describe(scenario, troop)));
        });
        return rows.ToArray();
    }
}

/// <summary>
/// 战报面板话题(M2.d):SangoCombatAnnals 的结构化 per-battle 卡快照(真源 =
/// feed.CombatAnnals.SnapshotBattles;卡聚合语义见 SangoCombatAnnals 文件头)。
/// 回合过滤在 UI 侧(卡自带 TurnStart/TurnLast)。
/// </summary>
public sealed class SangoWorldBattlesTopic : IWebUiTopicProducer
{
    public const string TopicName = "sango.world.battles";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SangoWorldFeed _feed;

    public SangoWorldBattlesTopic(SangoWorldFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        Scenario scenario = Scenario.Cur
            ?? throw new InvalidOperationException("Sango kernel is not booted; topic snapshot is unavailable.");
        bool isSubscription = context.RequestId != 0;
        var snapshot = new SangoBattlesSnapshot(
            isSubscription ? 0 : _feed.NextTick(),
            scenario.Info.turnCount,
            _feed.CombatAnnals.SnapshotBattles());
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscription ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions),
            "application/json",
            context.RequestId);
        return true;
    }
}

/// <summary>
/// 部队任务态的展示语(M2.d):Troop.missionType/missionTarget 的只读投影。占城任务按
/// 机会主义评分语义表述(沿途敌据点顺手攻陷、近战陷城即入城解散,见 SangoTroopOps
/// 文件头 TroopOccupyCity 链),不承诺直线行军。
/// </summary>
public static class SangoMissionLabels
{
    public static string Describe(Scenario scenario, Troop troop)
    {
        if (troop.missionType <= 0)
        {
            return string.Empty;
        }

        return (MissionType)troop.missionType switch
        {
            MissionType.TroopDestroyTroop => $"歼灭 {TroopName(scenario, troop.missionTarget)}",
            MissionType.TroopOccupyCity => $"攻占 {CityName(scenario, troop.missionTarget)}(沿途将顺势打击敌据点)",
            MissionType.TroopHarassCity => $"骚扰 {CityName(scenario, troop.missionTarget)}",
            MissionType.TroopBanishTroop => $"驱逐 {TroopName(scenario, troop.missionTarget)}",
            MissionType.TroopDestroyBuilding => $"摧毁 {BuildingName(scenario, troop.missionTarget)}",
            MissionType.TroopProtectTroop => $"护卫 {TroopName(scenario, troop.missionTarget)}",
            MissionType.TroopProtectBuilding => $"护卫 {BuildingName(scenario, troop.missionTarget)}",
            MissionType.TroopProtectCity => $"护卫 {CityName(scenario, troop.missionTarget)}",
            MissionType.TroopBuildBuilding => $"修建 {BuildingName(scenario, troop.missionTarget)}",
            MissionType.TroopFixBuilding => $"修缮 {BuildingName(scenario, troop.missionTarget)}",
            MissionType.TroopTransformGoodsToCity => $"输送至 {CityName(scenario, troop.missionTarget)}",
            MissionType.TroopReturnCity => $"返回 {CityName(scenario, troop.missionTarget)}",
            MissionType.TroopMovetoCity => $"前往 {CityName(scenario, troop.missionTarget)}",
            MissionType.TroopMovetoBuild => $"前往 {BuildingName(scenario, troop.missionTarget)}",
            MissionType.TroopMovetoCell => "前往指定格",
            MissionType.TroopStay => "原地驻守",
            _ => $"任务 #{troop.missionType}",
        };
    }

    static string TroopName(Scenario scenario, int id) => scenario.troopsSet.Get(id)?.Name ?? $"部队#{id}";
    static string CityName(Scenario scenario, int id) => scenario.citySet.Get(id)?.Name ?? $"城市#{id}";
    static string BuildingName(Scenario scenario, int id) => scenario.buildingSet.Get(id)?.Name ?? $"据点#{id}";
}

public sealed record SangoCityRow(
    int Id,
    string Name,
    int ForceId,
    string ForceName,
    int Population,
    int Gold,
    int Food,
    int PersonCount);

public sealed record SangoCitiesSnapshot(int Tick, int TurnCount, SangoCityRow[] Cities);

public sealed record SangoForceRow(
    int Id,
    string Name,
    string GovernorName,
    int CityCount,
    int PersonCount,
    bool Alive);

public sealed record SangoForcesSnapshot(int Tick, int TurnCount, SangoForceRow[] Forces);

public sealed record SangoTroopRow(
    int Id,
    string Name,
    int ForceId,
    string ForceName,
    int CorpsId,
    int BelongCityId,
    int X,
    int Y,
    int Troops,
    int Food,
    int Morale,
    int MoveAbility,
    bool ActionOver,
    string LandTroopTypeName,
    int MissionType,
    string MissionLabel);

public sealed record SangoTroopsSnapshot(int Tick, int TurnCount, SangoTroopRow[] Troops);

public sealed record SangoBattlesSnapshot(int Tick, int TurnCount, Sango.Runtime.SangoBattleRecord[] Battles);

public sealed record SangoTroopTypeRow(int Id, string Name, bool IsLand);

/// <summary>城市详情的部队区行:本城编成的部队(Leader 的 mBelongCity 即编成城)。</summary>
public sealed record SangoCityTroopRow(
    int Id,
    string Name,
    int X,
    int Y,
    int Troops,
    int Food,
    bool ActionOver);

public sealed record SangoTurnSnapshot(
    int TurnCount,
    int Year,
    int Month,
    int Day,
    string DateText,
    string Summary);

public sealed record SangoMessageRow(long Seq, int TurnCount, string Date, string Text);

public sealed record SangoMessagesSnapshot(int Tick, int TurnCount, SangoMessageRow[] Messages);

public sealed record SangoPersonRow(
    int Id,
    string Name,
    int Loyalty,
    int State,
    bool Free,
    int Command,
    int Strength,
    int Intelligence,
    int Politics,
    int Glamour);

public sealed record SangoCityDetailSnapshot(
    int Tick,
    int TurnCount,
    int Id,
    string Name,
    int ForceId,
    string ForceName,
    int Population,
    int Gold,
    int Food,
    int Morale,
    int MaxMorale,
    int Troops,
    int TroopsLimit,
    int FreePersonCount,
    int ActionPoint,
    SangoPersonRow[] Persons,
    SangoPersonRow[] WildPersons,
    SangoCityTroopRow[] FieldTroops,
    SangoTroopTypeRow[] TroopTypes);
