// sango 内政命令路由(M1.c)。命令到内核的映射逆向自原版 UI 窗口处理器
// (sango-src Project/Assets/Sango/Scripts/UI/City/*.cs → GameSystem City*.DoJob → City.Job*):
//   招揽(recruit) UICityRecruit.OnSure → CityRecruit.DoJob → City.JobRecruitPerson(executor, target)
//   探索(search)  UICitySearching.OnSure → CitySeraching.DoJob → City.JobSearching(persons)
//   训练(train)   UICityTrainTroops.OnSure → CityTrainTroops.DoJob → City.JobTrainTroops(persons)
//   奖励(reward)  UICityReward.OnSure → CityReward.DoJob → City.JobRewardPersons(persons)
// 下单前置条件逐条照抄各 CityXxx 系统的 IsValid 原语义(含城/军团双层 jobCounter 与行动力门槛),
// 不新增平行命令、不放宽门槛;失败按类型化错误回传。

using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Core.Gameplay.GAS;
using Ludots.WebUI.DataPlane;
using Sango.Core;

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

        if (!TryReadCityId(request.Payload, scenario, out City? city))
        {
            return WebUiCommandResult.Fail("city_not_found", "sango.cityCommand requires a known payload.cityId.");
        }

        if (city!.mBelongForce == null || city.mBelongCorps == null)
        {
            return WebUiCommandResult.Fail("city_not_owned",
                "sango.cityCommand targets an unowned city; internal affairs require a force and corps.");
        }

        return type switch
        {
            "train" => Train(request, city),
            "search" => Search(request, city),
            "reward" => Reward(request, city),
            "recruit" => Recruit(request, scenario, city),
            _ => WebUiCommandResult.Fail("invalid_payload",
                $"Unknown sango.cityCommand type '{type}'; expected train/search/reward/recruit."),
        };
    }

    // CityTrainTroops.IsValid:freePersons.Count>0 && CheckJobCost && morale<MaxMorale &&
    // 城 jobCounter(TrainTroops)==0 && 军团 ActionPoint>=costAP;DoJob → JobTrainTroops(全选武将)。
    private static WebUiCommandResult Train(WebUiCommandRequest request, City city)
    {
        int jobId = (int)CityJobType.TrainTroops;
        if (city.freePersons.Count == 0 ||
            !city.CheckJobCost(CityJobType.TrainTroops) ||
            city.morale >= city.MaxMorale ||
            city.GetJobCounter(jobId) != 0 ||
            city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
        {
            return WebUiCommandResult.Fail("invalid_state",
                "CityTrainTroops.IsValid gate rejected the order (no free persons / gold / morale cap / already trained / action points).");
        }

        Person[]? persons = ResolveFreePersons(request, city);
        if (persons == null || persons.Length == 0)
        {
            return WebUiCommandResult.Fail("person_not_free",
                "train requires payload.personIds of persons currently in the city freePersons list.");
        }

        city.JobTrainTroops(persons);
        return WebUiCommandResult.Ok();
    }

    // CitySeraching.IsValid:freePersons.Count>0 && ActionPoint>=costAP;DoJob → JobSearching(入 RenderEvent 队列,
    // 下一回合 Run 时 DoJobSearching 结算:发现人才/资金)。
    private static WebUiCommandResult Search(WebUiCommandRequest request, City city)
    {
        int jobId = (int)CityJobType.Searching;
        if (city.freePersons.Count == 0 ||
            city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
        {
            return WebUiCommandResult.Fail("invalid_state",
                "CitySeraching.IsValid gate rejected the order (no free persons / action points).");
        }

        Person[]? persons = ResolveFreePersons(request, city);
        if (persons == null || persons.Length == 0)
        {
            return WebUiCommandResult.Fail("person_not_free",
                "search requires payload.personIds of persons currently in the city freePersons list.");
        }

        city.JobSearching(persons);
        return WebUiCommandResult.Ok();
    }

    // CityReward.IsValid:gold>100 && CheckJobCost && 军团 jobCounter(Reward)==0 && ActionPoint>=costAP;
    // OnEnter targetList = 势力武将(非君主、无部队、忠诚<100);DoJob → JobRewardPersons(选中者,忠诚+10)。
    private static WebUiCommandResult Reward(WebUiCommandRequest request, City city)
    {
        int jobId = (int)CityJobType.Reward;
        if (city.gold <= 100 ||
            !city.CheckJobCost(CityJobType.Reward) ||
            city.mBelongCorps.GetJobCounter(jobId) != 0 ||
            city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
        {
            return WebUiCommandResult.Fail("invalid_state",
                "CityReward.IsValid gate rejected the order (gold / already rewarded this turn / action points).");
        }

        if (!TryReadPersonIds(request, out int[] personIds))
        {
            return WebUiCommandResult.Fail("invalid_payload", "reward requires payload.personIds.");
        }

        Force force = city.mBelongForce
            ?? throw new InvalidOperationException("City force gate must run before reward dispatch.");

        var targets = new List<Person>(personIds.Length);
        foreach (int personId in personIds)
        {
            Person? person = Scenario.Cur.personSet.Get(personId);
            if (person == null || person.mBelongForce != force ||
                person == force.mGovernor || person.mTroop != null || person.loyalty >= 100)
            {
                return WebUiCommandResult.Fail("invalid_state",
                    $"person {personId} is outside the CityReward target list (own force, not governor, no troop, loyalty<100).");
            }

            targets.Add(person);
        }

        city.JobRewardPersons(targets.ToArray());
        return WebUiCommandResult.Ok();
    }

    // CityRecruit.IsValid:freePersons.Count>0 && ActionPoint>=costAP;DoJob → JobRecruitPerson(executor, target);
    // targetList 语义(CityRecruit.OnEnter):他势力非君主非俘虏武将 + 本势力在野/俘虏。
    private static WebUiCommandResult Recruit(WebUiCommandRequest request, Scenario scenario, City city)
    {
        int jobId = (int)CityJobType.RecruitPerson;
        if (city.freePersons.Count == 0 ||
            city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
        {
            return WebUiCommandResult.Fail("invalid_state",
                "CityRecruit.IsValid gate rejected the order (no free persons / action points).");
        }

        Person[]? persons = ResolveFreePersons(request, city);
        if (persons == null || persons.Length != 1)
        {
            return WebUiCommandResult.Fail("person_not_free",
                "recruit requires exactly one payload.personIds executor from the city freePersons list.");
        }

        if (!request.Payload.TryGetProperty("targetPersonId", out JsonElement targetElement) ||
            targetElement.ValueKind != JsonValueKind.Number ||
            !targetElement.TryGetInt32(out int targetId))
        {
            return WebUiCommandResult.Fail("invalid_payload", "recruit requires payload.targetPersonId.");
        }

        Person? target = scenario.personSet.Get(targetId);
        if (target == null || !IsRecruitTarget(city, target))
        {
            return WebUiCommandResult.Fail("invalid_state",
                $"person {targetId} is outside the CityRecruit target list.");
        }

        city.JobRecruitPerson(persons[0], target);
        return WebUiCommandResult.Ok();
    }

    private static bool IsRecruitTarget(City city, Person target)
    {
        Force? force = city.mBelongForce;
        if (force == null)
        {
            return false;
        }

        if (target.mBelongForce != force)
        {
            return target.state != (int)PersonStateType.Governor &&
                target.state != (int)PersonStateType.Prisoner;
        }

        return target.state == (int)PersonStateType.Unemployed ||
            target.state == (int)PersonStateType.Prisoner;
    }

    private static bool TryReadCityId(JsonElement payload, Scenario scenario, out City? city)
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

    private static bool TryReadPersonIds(WebUiCommandRequest request, out int[] personIds)
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

    // 原版各窗口的执行武将选择列表 = TargetCity.freePersons(UICityTrainTroops/UICitySearching/
    // UICityPersonCall 的 PersonSelectSystem.Start 入参);这里要求 personIds 全部落在该列表内。
    private static Person[]? ResolveFreePersons(WebUiCommandRequest request, City city)
    {
        if (!TryReadPersonIds(request, out int[] personIds) || personIds.Length == 0)
        {
            return null;
        }

        var persons = new List<Person>(personIds.Length);
        foreach (int personId in personIds)
        {
            Person? person = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personId);
            if (person == null)
            {
                return null;
            }

            persons.Add(person);
        }

        return persons.ToArray();
    }
}

/// <summary>
/// 结束回合(Ludots 正式链):对 Manual 时钟 stepPolicy 调 RequestStep(1),由 GasClockSystem
/// 在引擎 tick 消费并发 TurnAdvanced → SangoSimMod 推进 sango 回合(PLAN D5)。
/// 本 handler 不直接推内核,也不做任何缺服务兜底;策略实例由 Entry 从引擎服务解析注入。
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
        _stepPolicy.RequestStep(1);
        return ValueTask.FromResult(WebUiCommandResult.Ok());
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

public sealed class SangoWebUiPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        SangoCityCommandHandler.CommandName,
        SangoEndTurnCommandHandler.CommandName,
        SangoSaveCommandHandler.CommandName,
        SangoLoadCommandHandler.CommandName,
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
