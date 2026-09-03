// sango 内政命令路由(M1.c)。命令到内核的映射逆向自原版 UI 窗口处理器
// (sango-src Project/Assets/Sango/Scripts/UI/City/*.cs → GameSystem City*.DoJob → City.Job*):
//   招揽(recruit) UICityRecruit.OnSure → CityRecruit.DoJob → City.JobRecruitPerson(executor, target)
//   探索(search)  UICitySearching.OnSure → CitySeraching.DoJob → City.JobSearching(persons)
//   训练(train)   UICityTrainTroops.OnSure → CityTrainTroops.DoJob → City.JobTrainTroops(persons)
//   奖励(reward)  UICityReward.OnSure → CityReward.DoJob → City.JobRewardPersons(persons)
// M2.d:门槛/执行体已内核化为 SangoCityOps(命令层与 SangoReplayJournal 共用同一实现,
// 含 journal 埋点),本层只做 payload 解析与城市解析,不再持有第二份门槛。
// M2.b 增两命令(编成/移动,见各 handler 注释与 SangoTroopOps 文件头的原版调用链)。

using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Core.Gameplay.GAS;
using Ludots.WebUI.DataPlane;
using Sango.Core;
using Sango.Runtime;

namespace Sango.WebUi;

public sealed class SangoCityCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.cityCommand";

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Handle(request));
    }

    internal static WebUiCommandResult Handle(WebUiCommandRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.cityCommand requires a JSON object payload.");
        }

        if (!request.Payload.TryGetProperty("type", out JsonElement typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.cityCommand requires payload.type.");
        }

        string type = typeElement.GetString() ?? string.Empty;
        Scenario? scenario = Scenario.Cur;
        if (scenario == null)
        {
            return WebUiCommandResult.Fail("kernel_not_booted", "Sango kernel is not booted; commands are unavailable.");
        }

        if (!TryReadCityId(request.Payload, scenario, out City? city) || city == null)
        {
            return WebUiCommandResult.Fail("city_not_found", "sango.cityCommand requires a known payload.cityId.");
        }

        if (!TryReadPersonIds(request, out int[] personIds))
        {
            personIds = Array.Empty<int>();
        }

        int targetPersonId = 0;
        if (request.Payload.TryGetProperty("targetPersonId", out JsonElement targetElement) &&
            targetElement.ValueKind == JsonValueKind.Number &&
            !targetElement.TryGetInt32(out targetPersonId))
        {
            targetPersonId = 0;
        }

        SangoTroopOpResult result = SangoCityOps.Execute(scenario, city, type, personIds, targetPersonId);
        return result.Succeeded
            ? WebUiCommandResult.Ok()
            : WebUiCommandResult.Fail(result.ErrorCode, result.Message);
    }

    internal static bool TryReadCityId(JsonElement payload, Scenario scenario, out City? city)
    {
        city = null;
        if (!payload.TryGetProperty("cityId", out JsonElement cityElement) ||
            cityElement.ValueKind != JsonValueKind.Number ||
            !cityElement.TryGetInt32(out int cityId))
        {
            return false;
        }

        city = scenario.citySet.Get(cityId);
        return city != null;
    }

    internal static bool TryReadPersonIds(WebUiCommandRequest request, out int[] personIds)
    {
        personIds = Array.Empty<int>();
        if (!request.Payload.TryGetProperty("personIds", out JsonElement element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var ids = new List<int>(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int id))
            {
                return false;
            }

            ids.Add(id);
        }

        personIds = ids.ToArray();
        return true;
    }

}

/// <summary>
/// 结束回合(Ludots 正式链):对 Manual 时钟 stepPolicy 调 RequestStep(1),由 GasClockSystem
/// 在引擎 tick 消费并发 TurnAdvanced → SangoSimMod 推进 sango 回合(PLAN D5)。
/// M3.a 玩家分支:玩家局里世界停在本玩家回合的阻塞位(Corps.Run 的 OnPlayerControl),
/// 先走原版「进行」语义(SangoPlayerTurnOps.EndPlayerTurn = PlayerEndTurn.Update 体)放行
/// 当前军团,再请求时钟步进。本 handler 不直接推内核,也不做任何缺服务兜底;策略实例由
/// Entry 从引擎服务解析注入。
/// </summary>
public sealed class SangoEndTurnCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.endTurn";

    private readonly GasClockStepPolicy _stepPolicy;

    public SangoEndTurnCommandHandler(GasClockStepPolicy stepPolicy)
    {
        _stepPolicy = stepPolicy ?? throw new ArgumentNullException(nameof(stepPolicy));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        // 玩家门与全托管并存:玩家局的结束回合 = 进行 + 时钟步;玩家势力灭亡后
        // AwaitingPlayer 恒假,自然退化为纯时钟步。
        Scenario? scenario = Scenario.Cur;
        if (scenario != null && SangoPlayerTurnOps.AwaitingPlayer(scenario))
        {
            try
            {
                SangoPlayerTurnOps.EndPlayerTurn();
            }
            catch (InvalidOperationException ex)
            {
                return ValueTask.FromResult(WebUiCommandResult.Fail("player_turn_invalid", ex.Message));
            }
        }

        _stepPolicy.RequestStep(1);
        return ValueTask.FromResult(WebUiCommandResult.Ok());
    }
}

/// <summary>
/// 开局势力选择(M3.a,原版 window_scenario_force_select 的命令面):payload = {forceId};
/// 以同种子带玩家重装世界(SangoPlayerTurnOps.SelectPlayerForce → BootWithPlayer,走
/// CheckPlayer 正式数据面),随后重灌引擎城/部队标记。仅开局一次(世界已推进即拒绝)。
/// </summary>
public sealed class SangoSelectPlayerForceCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.selectPlayerForce";

    private readonly GameEngine _engine;
    private readonly IVirtualFileSystem _vfs;

    public SangoSelectPlayerForceCommandHandler(GameEngine engine, IVirtualFileSystem vfs)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        WebUiCommandResult result = Select(_vfs, request);
        if (result.Success)
        {
            SangoSimModEntry.SyncWorldPresentation(_engine);
        }

        return ValueTask.FromResult(result);
    }

    internal static WebUiCommandResult Select(IVirtualFileSystem vfs, WebUiCommandRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object ||
            !request.Payload.TryGetProperty("forceId", out JsonElement forceElement) ||
            forceElement.ValueKind != JsonValueKind.Number ||
            !forceElement.TryGetInt32(out int forceId))
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.selectPlayerForce requires an integer payload.forceId.");
        }

        try
        {
            SangoPlayerTurnOps.SelectPlayerForce(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed, forceId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return WebUiCommandResult.Fail("select_failed", ex.Message);
        }

        return WebUiCommandResult.Ok();
    }
}

/// <summary>
/// 存档/读档(M1.d 可选增量):走 Ludots 正式持久化管线(WorldSnapshotService 清洁边界捕获 →
/// SaveSlotStore 槽位 → WorldRestoreService 回灌),sango 世界态由引擎存档注册表里的
/// SangoSaveParticipant(sango.sim 域)承载。存储后端是引擎 ISaveStorage 服务,缺席即
/// 类型化失败——本 mod 不落任何文件,也不造替代存储(照 SavePanelRuntime 用法)。
/// </summary>
public sealed class SangoSaveCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.save";
    internal const string SlotPrefix = "sango-";

    private readonly GameEngine _engine;

    public SangoSaveCommandHandler(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Handle());
    }

    internal WebUiCommandResult Handle()
    {
        if (Scenario.Cur == null)
        {
            return WebUiCommandResult.Fail("kernel_not_booted", "Sango kernel is not booted; save is unavailable.");
        }

        ISaveStorage? storage = _engine.GetService(CoreServiceKeys.SaveStorage);
        if (storage == null)
        {
            return WebUiCommandResult.Fail("storage_unavailable",
                "This host did not register an ISaveStorage service; sango.save requires engine save storage.");
        }

        SaveSlotId id = SaveSlotId.Manual($"{SlotPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        WorldSaveSnapshot snapshot = new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        new SaveSlotStore(storage).WriteSlot(id, snapshot);
        return WebUiCommandResult.Ok();
    }
}

public sealed class SangoLoadCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.load";

    private readonly GameEngine _engine;

    public SangoLoadCommandHandler(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Handle());
    }

    internal WebUiCommandResult Handle()
    {
        ISaveStorage? storage = _engine.GetService(CoreServiceKeys.SaveStorage);
        if (storage == null)
        {
            return WebUiCommandResult.Fail("storage_unavailable",
                "This host did not register an ISaveStorage service; sango.load requires engine save storage.");
        }

        var store = new SaveSlotStore(storage);
        SaveSlotId? latest = null;
        foreach (SaveSlotHeader header in store.ListSlots())
        {
            if (!string.Equals(header.Id.Kind, "manual", StringComparison.Ordinal) ||
                !header.Id.Name.StartsWith(SangoSaveCommandHandler.SlotPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (latest == null || string.Compare(header.Id.Name, latest.Value.Name, StringComparison.Ordinal) > 0)
            {
                latest = header.Id;
            }
        }

        if (latest == null)
        {
            return WebUiCommandResult.Fail("no_save_slot", "No sango save slot exists yet; save first.");
        }

        new WorldRestoreService().Restore(_engine, store.ReadSlot(latest.Value));
        return WebUiCommandResult.Ok();
    }
}

/// <summary>
/// 部队编成(M2.b):UICityExpedition 表单终态 → SangoTroopOps.CreateTroop(原版
/// CityExpedition.DoJob 链的 headless 等价展开,门槛/结算见 SangoTroopOps 文件头)。
/// payload = {cityId, personIds[1..3], landTroopTypeId?, waterTroopTypeId?, troops, gold, food};
/// 兵种 id 取自城市详情话题的 troopTypes 表单选项(OnEnter 的可组兵种表)。
/// </summary>
public sealed class SangoCreateTroopCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.createTroop";

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Handle(request));
    }

    internal static WebUiCommandResult Handle(WebUiCommandRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.createTroop requires a JSON object payload.");
        }

        Scenario? scenario = Scenario.Cur;
        if (scenario == null)
        {
            return WebUiCommandResult.Fail("kernel_not_booted", "Sango kernel is not booted; commands are unavailable.");
        }

        if (!SangoCityCommandHandler.TryReadCityId(request.Payload, scenario, out City? city) || city == null)
        {
            return WebUiCommandResult.Fail("city_not_found", "sango.createTroop requires a known payload.cityId.");
        }

        if (!SangoCityCommandHandler.TryReadPersonIds(request, out int[] personIds))
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.createTroop requires payload.personIds.");
        }

        (SangoTroopOpResult result, Troop? troop) = SangoTroopOps.CreateTroop(
            scenario,
            city,
            personIds,
            ReadOptionalId(request.Payload, "landTroopTypeId"),
            ReadOptionalId(request.Payload, "waterTroopTypeId"),
            ReadInt(request.Payload, "troops"),
            ReadInt(request.Payload, "gold"),
            ReadInt(request.Payload, "food"));

        if (!result.Succeeded)
        {
            return WebUiCommandResult.Fail(result.ErrorCode, result.Message);
        }

        return WebUiCommandResult.Ok();
    }

    private static int ReadInt(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int value)
                ? value
                : 0;
    }

    private static int? ReadOptionalId(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int value)
                ? value
                : null;
    }
}

/// <summary>
/// 部队移动(M2.b):目标格在移动范围内时单步落地(TroopSystem/TroopActionStay 链,见
/// SangoTroopOps 文件头);范围外按原版门槛拒绝(原版该路径走多回合委任链,不在本命令面)。
/// payload = {troopId, x, y}(内核格坐标,x=北、y=东)。
/// </summary>
public sealed class SangoMoveTroopCommandHandler : IWebUiCommandHandler
{
    public const string CommandName = "sango.moveTroop";

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Handle(request));
    }

    internal static WebUiCommandResult Handle(WebUiCommandRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.moveTroop requires a JSON object payload.");
        }

        Scenario? scenario = Scenario.Cur;
        if (scenario == null)
        {
            return WebUiCommandResult.Fail("kernel_not_booted", "Sango kernel is not booted; commands are unavailable.");
        }

        if (!request.Payload.TryGetProperty("troopId", out JsonElement troopElement) ||
            troopElement.ValueKind != JsonValueKind.Number ||
            !troopElement.TryGetInt32(out int troopId) ||
            scenario.troopsSet.Get(troopId) is not { } troop)
        {
            return WebUiCommandResult.Fail("troop_not_found", "sango.moveTroop requires a known payload.troopId.");
        }

        if (!request.Payload.TryGetProperty("x", out JsonElement xElement) ||
            xElement.ValueKind != JsonValueKind.Number || !xElement.TryGetInt32(out int x) ||
            !request.Payload.TryGetProperty("y", out JsonElement yElement) ||
            yElement.ValueKind != JsonValueKind.Number || !yElement.TryGetInt32(out int y))
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.moveTroop requires integer payload.x / payload.y cell coordinates.");
        }

        (SangoTroopOpResult result, _) = SangoTroopOps.MoveTroop(scenario, troop, scenario.Map.GetCell(x, y));
        if (!result.Succeeded)
        {
            return WebUiCommandResult.Fail(result.ErrorCode, result.Message);
        }

        // M3.c:成功 ack 的 message 段被引擎 CreateAck 平铺丢弃(引擎面不在本任务边界内),
        // 行动面语义经 sango.world.troops 的 missionLabel("前往指定格 (x,y)")向 UI 表达。
        return WebUiCommandResult.Ok();
    }
}

public sealed class SangoWebUiPermissionValidator : IWebUiCommandPermissionValidator
{
        private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
        {
            SangoCityCommandHandler.CommandName,
            SangoEndTurnCommandHandler.CommandName,
            SangoSaveCommandHandler.CommandName,
            SangoLoadCommandHandler.CommandName,
            SangoCreateTroopCommandHandler.CommandName,
            SangoMoveTroopCommandHandler.CommandName,
            SangoSelectPlayerForceCommandHandler.CommandName,
        };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in SangoWebUiMod.";
        return false;
    }
}

public sealed class SangoWebUiGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}
