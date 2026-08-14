using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class RtsMultiplayerFrontlinePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "rts_duel_v1";
    private const string RuntimeKey = "rts.multiplayer.frontline.runtime";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "RtsShowcaseMod",
        "RtsMultiplayerFrontlineMod",
    };

    [Test]
    [Description(
        "Feature: 公平开局\n" +
        "  Given 两位玩家进入前线对决\n" +
        "  When 战场完成载入\n" +
        "  Then 双方都看到一座指挥核心、两名采集者、两队步兵和40水晶")]
    public void GivenTwoPlayers_WhenBattleLoads_ThenBothReceiveTheSameStartingForce()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        World world = engine.World;
        AssertSide(world, playerId: 1, teamId: 1, "Northern", expectedCrystals: 40f);
        AssertSide(world, playerId: 2, teamId: 2, "Southern", expectedCrystals: 40f);
        Assert.That(CountNamed(world, "Crystal Field"), Is.EqualTo(2));
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
    }

    [Test]
    [Description(
        "Feature: 双方准备后开战\n" +
        "  Given 只有一位玩家准备完毕\n" +
        "  When 另一位玩家尚未准备，并且有人尝试提前移动单位\n" +
        "  Then 战斗保持等待、提前命令不执行；直到双方都准备并完成3秒倒计时后才允许移动")]
    public void GivenOnlyOneReady_WhenOrdersArrive_ThenBattleWaitsAndCommandsStartAfterCountdown()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        World world = engine.World;
        Entity harvester = FindNamed(world, "Northern Harvester A");
        WorldPositionCm start = world.Get<WorldPositionCm>(harvester);
        SetParticipantReady(engine, sideIndex: 0, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));

        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        Tick(engine, 30);
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), start), Is.EqualTo(0f));
        Assert.That(world.Get<OrderBuffer>(harvester).IsEmpty, Is.True,
            "A pre-match command must be explicitly cancelled, not held for a surprise start.");

        SetParticipantReady(engine, sideIndex: 1, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("Countdown"));
        Assert.That(ReadSnapshot(engine, "CountdownRemainingTicks"), Is.EqualTo(90));
        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        TickUntilCountdown(engine, 1);
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), start), Is.EqualTo(0f));
        Assert.That(world.Get<OrderBuffer>(harvester).IsEmpty, Is.True);

        TickUntil(engine, () => Equals(ReadSnapshot(engine, "Phase"), "InProgress"), 8,
            "The battle should start after the configured three-second countdown.");
        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        TickUntil(engine, () => DistanceCm(world.Get<WorldPositionCm>(harvester), start) > 100f, 30,
            "Movement should become available after the countdown.");
    }

    [Test]
    [Description(
        "Feature: 准备状态变化\n" +
        "  Given 双方已进入3秒开战倒计时\n" +
        "  When 任一玩家取消准备或掉线\n" +
        "  Then 倒计时立即取消；重连不会自动准备，必须由双方再次确认")]
    public void GivenCountdown_WhenPlayerUnreadyOrDisconnects_ThenCountdownCancelsImmediately()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        SetParticipantReady(engine, sideIndex: 0, ready: true);
        SetParticipantReady(engine, sideIndex: 1, ready: true);
        TickUntilCountdown(engine, 60);
        SetParticipantReady(engine, sideIndex: 1, ready: false);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(ReadSnapshot(engine, "CountdownRemainingTicks"), Is.EqualTo(0));

        SetParticipantReady(engine, sideIndex: 1, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("Countdown"));
        SetParticipantConnected(engine, sideIndex: 0, connected: false);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(ReadSnapshot(engine, "SideOneReady"), Is.EqualTo(false));

        SetParticipantConnected(engine, sideIndex: 0, connected: true);
        Tick(engine, 10);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"),
            "Reconnect must not silently ready a player.");
    }

    [Test]
    [Description(
        "Feature: 采集水晶\n" +
        "  Given 北方玩家有40水晶并选中一名采集者\n" +
        "  When 玩家右键北方水晶矿\n" +
        "  Then 采集者抵达矿区装载时资源不变，并且回到指挥核心后一次到账20水晶")]
    public void GivenHarvester_WhenPlayerOrdersGather_ThenCrystalsArriveOnlyAfterReturn()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        Entity harvester = FindNamed(world, "Northern Harvester A");
        Entity node = FindNamed(world, "Northern Crystal Field");
        int crystalAttributeId = RequireAttribute("Crystals");
        int gatherOrderTypeId = RequireOrderType(engine, "frontlineGather");

        EnqueueOrder(engine, gatherOrderTypeId, playerId: 1, harvester, node);
        WorldPositionCm start = world.Get<WorldPositionCm>(harvester);
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(harvester), start) > 500f,
            180,
            "The harvester should visibly leave its starting position.");

        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f));
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(harvester), world.Get<WorldPositionCm>(node)) <= 100f,
            420,
            "The harvester should reach the crystal field.");
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f),
            "Crystals must not be credited at the mine.");

        Tick(engine, 30);
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f),
            "Crystals must remain unchanged while loading.");

        TickUntil(
            engine,
            () => ReadAttribute(world, core, crystalAttributeId) == 60f,
            600,
            "The 20-crystal cargo should be credited only after the harvester returns.");
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), world.Get<WorldPositionCm>(core)),
            Is.LessThanOrEqualTo(100f));
    }

    [Test]
    [Description(
        "Feature: 训练步兵\n" +
        "  Given 指挥核心只有40水晶\n" +
        "  When 玩家按Q训练一队价值60水晶的步兵\n" +
        "  Then 指令被明确拒绝，水晶不扣除，也不会生成步兵")]
    public void GivenFortyCrystals_WhenPlayerTrainsInfantry_ThenOrderIsRejectedWithoutCharge()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        int startingInfantry = CountNamed(world, "Infantry");

        EnqueueCastAbility(engine, core, playerId: 1, slot: 0);
        TickUntil(
            engine,
            () => !world.Get<OrderBuffer>(core).HasActive,
            20,
            "The unaffordable training order should complete as rejected.");
        Tick(engine, 260);

        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f));
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry));
    }

    [Test]
    [Description(
        "Feature: 训练步兵\n" +
        "  Given 指挥核心已有60水晶\n" +
        "  When 玩家按Q训练步兵并等待8秒\n" +
        "  Then 60水晶只扣除一次，并且一队属于该玩家的新步兵抵达战场")]
    public void GivenSixtyCrystals_WhenTrainingFinishes_ThenExactlyOneOwnedInfantryArrives()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        AttributeMutationOps.SetCurrent(world, core, crystalAttributeId, 60f);
        int startingInfantry = CountNamed(world, "Infantry");

        EnqueueCastAbility(engine, core, playerId: 1, slot: 0);
        TickUntil(
            engine,
            () => ReadAttribute(world, core, crystalAttributeId) == 0f,
            20,
            "An admitted training order should charge exactly 60 crystals.");
        AdvanceCommittedTicks(engine, 239);
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry),
            "The squad must not arrive before the configured training time.");

        TickUntil(
            engine,
            () => CountNamed(world, "Infantry") == startingInfantry + 1,
            8,
            "One infantry squad should arrive when training completes.");

        Entity created = FindNamed(world, "Frontline Infantry");
        Assert.That(world.Get<PlayerOwner>(created).PlayerId, Is.EqualTo(1));
        Assert.That(world.Get<Team>(created).Id, Is.EqualTo(1));
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(0f));
    }

    [Test]
    [Description(
        "Feature: 步兵交战\n" +
        "  Given 北方步兵与南方步兵相距较远\n" +
        "  When 北方玩家右键敌军\n" +
        "  Then 北方步兵追近目标、持续造成伤害，并在击败目标后将其从战场移除")]
    public void GivenHostileTarget_WhenPlayerOrdersAttack_ThenInfantryPursuesDamagesAndDefeatsIt()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity attacker = FindNamed(world, "Northern Infantry A");
        Entity target = FindNamed(world, "Southern Infantry A");
        int healthAttributeId = RequireAttribute("Health");
        int attackOrderTypeId = RequireOrderType(engine, "attackTarget");
        WorldPositionCm startingPosition = world.Get<WorldPositionCm>(attacker);

        EnqueueOrder(engine, attackOrderTypeId, playerId: 1, attacker, target);
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(attacker), startingPosition) > 500f,
            180,
            "The infantry should pursue instead of damaging from across the map.");
        Assert.That(ReadAttribute(world, target, healthAttributeId), Is.EqualTo(100f));

        TickUntil(
            engine,
            () => world.IsAlive(target) && ReadAttribute(world, target, healthAttributeId) < 100f,
            1200,
            "The pursuing infantry should enter range and deal damage.");
        TickUntil(
            engine,
            () => !world.IsAlive(target),
            360,
            "The defeated infantry should be removed from the battlefield.");
    }

    [Test]
    [Description(
        "Feature: 摧毁指挥核心\n" +
        "  Given 双方对决仍在进行\n" +
        "  When 南方指挥核心被摧毁\n" +
        "  Then 北方玩家立即获胜")]
    public void GivenRunningMatch_WhenSouthernCoreFalls_ThenNorthernPlayerWins()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine.World, FindNamed(engine.World, "Southern Command Core"), 0f);
        AdvanceCommittedTicks(engine, 1);

        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"));
        Assert.That(ReadSnapshot(engine, "WinningSideIndex"), Is.EqualTo(0));
    }

    [Test]
    [Description(
        "Feature: 同帧决胜\n" +
        "  Given 双方指挥核心都只剩最后一点生命\n" +
        "  When 两座核心在同一个已提交帧被摧毁\n" +
        "  Then 比赛判为平局，而不是偏袒先被遍历的一方")]
    public void GivenBothCoresFallOnSameCommittedTick_WhenOutcomeResolves_ThenMatchIsDraw()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine.World, FindNamed(engine.World, "Northern Command Core"), 0f);
        SetHealth(engine.World, FindNamed(engine.World, "Southern Command Core"), 0f);
        AdvanceCommittedTicks(engine, 1);

        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("Draw"));
        Assert.That(ReadSnapshot(engine, "WinningSideIndex"), Is.EqualTo(-1));
    }

    [Test]
    [Description(
        "Feature: 限时决胜\n" +
        "  Given 五分钟内双方核心都未被摧毁\n" +
        "  When 比赛时间结束\n" +
        "  Then 核心剩余生命更高的一方获胜")]
    public void GivenFiveMinuteLimit_WhenBothCoresSurvive_ThenHigherHealthSideWins()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine.World, FindNamed(engine.World, "Southern Command Core"), 800f);
        TickUntilCommittedTick(engine, 8999);
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));

        TickUntilCommittedTick(engine, 9000);
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"));
    }

    [Test]
    [Description(
        "Feature: 断线裁定\n" +
        "  Given 南方玩家在五分钟终场前进入断线宽限期\n" +
        "  When 断线宽限期与比赛时间在同一帧结束\n" +
        "  Then 系统先按断线规则判北方获胜，不用核心生命覆盖断线结果")]
    public void GivenDisconnectGraceAndTimeLimitExpireTogether_WhenOutcomeResolves_ThenDisconnectTakesPriority()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine.World, FindNamed(engine.World, "Northern Command Core"), 500f);
        TickUntilCommittedTick(engine, 8700);
        SetParticipantConnected(engine, sideIndex: 1, connected: false);
        TickUntilCommittedTick(engine, 9000);

        Assert.That(ReadSnapshot(engine, "CommittedTick"), Is.EqualTo(9000));
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"),
            "Disconnect expiry must outrank the simultaneous higher-health time-limit result.");
    }

    [Test]
    [Description(
        "Feature: 新玩家战场指引\n" +
        "  Given 玩家第一次进入前线对决\n" +
        "  When 玩家查看战场界面\n" +
        "  Then 界面只说明胜利目标与采集、训练、进攻操作，不显示网络包、帧队列或延迟遥测")]
    public void GivenNewPlayer_WhenHudAppears_ThenItExplainsTheBattleWithoutNetworkTelemetry()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        Tick(engine, 1);

        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer service is missing.");
        string hud = string.Join("\n", ReadOverlayText(overlay));

        Assert.That(hud, Does.Contain("Break the opposing command core"));
        Assert.That(hud, Does.Contain("Waiting for both commanders"));
        Assert.That(hud, Does.Contain("Northern commander: NOT READY"));
        Assert.That(hud, Does.Contain("right-click a crystal field"));
        Assert.That(hud, Does.Contain("press Q to train infantry"));
        Assert.That(hud, Does.Contain("right-click an enemy"));
        Assert.That(hud, Does.Not.Match("(?i)packet|latency|ping|network tick|queue depth|snapshot id|ack"));
    }

    [Test]
    [Description(
        "Feature: 可复用的公平战场\n" +
        "  Given 制作者调整玩家编号、队伍或出生位置\n" +
        "  When 系统载入对局配置与地图\n" +
        "  Then 双方关系和镜像布局来自数据文件，玩法代码没有写死玩家、队伍或出生坐标")]
    public void GivenAuthoredBattleData_WhenFilesAreInspected_ThenSidesAndPositionsHaveOneDataSource()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(repoRoot, "mods", "showcases", "rts_multiplayer_frontline", "RtsMultiplayerFrontlineMod");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "RtsMultiplayerFrontlineConfig.json")));
        using JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Maps", "rts_duel_v1.json")));

        JsonElement sides = config.RootElement.GetProperty("sides");
        Assert.That(sides.GetArrayLength(), Is.EqualTo(2));
        Assert.That(sides[0].GetProperty("initialHarvesterCount").GetInt32(),
            Is.EqualTo(sides[1].GetProperty("initialHarvesterCount").GetInt32()));
        Assert.That(sides[0].GetProperty("initialInfantryCount").GetInt32(),
            Is.EqualTo(sides[1].GetProperty("initialInfantryCount").GetInt32()));

        JsonElement entities = map.RootElement.GetProperty("Entities");
        int northCoreX = FindMapEntityX(entities, "Northern Command Core");
        int southCoreX = FindMapEntityX(entities, "Southern Command Core");
        int centerX = map.RootElement.GetProperty("DefaultCamera").GetProperty("TargetXCm").GetInt32();
        Assert.That(northCoreX + southCoreX, Is.EqualTo(centerX * 2));

        string source = string.Join("\n", Directory.GetFiles(modRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        Assert.That(source, Does.Not.Match(@"PlayerId\s*=\s*[12]\b"));
        Assert.That(source, Does.Not.Match(@"TeamId\s*=\s*[12]\b"));
        Assert.That(source, Does.Not.Match(@"WorldPositionCm\s*\.\s*FromCm"));
        Assert.That(Regex.Matches(source, @"\b(7000|8200|9300|11200|18800|20700|21800|23000)\b"), Is.Empty);
    }

    private static GameEngine CreateStartedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);

        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine)
    {
        engine.LoadMap(MapId);
        Tick(engine, 5);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        Assert.That(engine.GlobalContext.ContainsKey(RuntimeKey), Is.True,
            "The Frontline runtime should publish its platform-neutral projection hook.");
    }

    private static void AssertSide(World world, int playerId, int teamId, string namePrefix, float expectedCrystals)
    {
        Entity core = FindNamed(world, $"{namePrefix} Command Core");
        Assert.That(world.Get<PlayerOwner>(core).PlayerId, Is.EqualTo(playerId));
        Assert.That(world.Get<Team>(core).Id, Is.EqualTo(teamId));
        Assert.That(ReadAttribute(world, core, RequireAttribute("Crystals")), Is.EqualTo(expectedCrystals));
        Assert.That(CountNamed(world, $"{namePrefix} Harvester"), Is.EqualTo(2));
        Assert.That(CountNamed(world, $"{namePrefix} Infantry"), Is.EqualTo(2));
    }

    private static void EnqueueCastAbility(GameEngine engine, Entity core, int playerId, int slot)
    {
        EnqueueOrder(
            engine,
            RequireOrderType(engine, "castAbility"),
            playerId,
            core,
            core,
            new OrderArgs { I0 = slot });
    }

    private static void EnqueueMove(GameEngine engine, Entity actor, int playerId, int x, int y)
    {
        EnqueueOrder(
            engine,
            RequireOrderType(engine, "moveTo"),
            playerId,
            actor,
            Entity.Null,
            OrderArgs.CreateSingleWorldCm(new Vector3(x, 0f, y)));
    }

    private static void EnqueueOrder(
        GameEngine engine,
        int orderTypeId,
        int playerId,
        Entity actor,
        Entity target,
        OrderArgs args = default)
    {
        var queue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        var order = new Order
        {
            OrderTypeId = orderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Target = target,
            Args = args,
            SubmitMode = OrderSubmitMode.Immediate,
        };

        Assert.That(queue.TryEnqueueAssigned(ref order), Is.True, "The player's order should enter the formal order queue.");
    }

    private static int RequireOrderType(GameEngine engine, string key)
    {
        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
        int id = registry.GetId(key);
        Assert.That(id, Is.GreaterThan(0), $"Order type '{key}' must be configured.");
        return id;
    }

    private static int RequireAttribute(string name)
    {
        int id = AttributeRegistry.GetId(name);
        Assert.That(id, Is.GreaterThanOrEqualTo(0), $"Attribute '{name}' must be configured.");
        return id;
    }

    private static Entity FindNamed(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name component) =>
        {
            if (found == Entity.Null && string.Equals(component.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        return found != Entity.Null
            ? found
            : throw new InvalidOperationException($"Entity '{name}' was not found.");
    }

    private static int CountNamed(World world, string fragment)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity _, ref Name name) =>
        {
            if (name.Value.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        });
        return count;
    }

    private static float ReadAttribute(World world, Entity entity, int attributeId) =>
        world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);

    private static void SetHealth(World world, Entity entity, float health) =>
        AttributeMutationOps.SetCurrent(world, entity, RequireAttribute("Health"), health);

    private static float DistanceCm(in WorldPositionCm a, in WorldPositionCm b)
    {
        var left = a.ToWorldCmInt2();
        var right = b.ToWorldCmInt2();
        long dx = left.X - (long)right.X;
        long dy = left.Y - (long)right.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static object ReadSnapshot(GameEngine engine, string propertyName)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        object snapshot = runtime.GetType().GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.Public)?.GetValue(runtime)
            ?? throw new MissingMemberException(runtime.GetType().FullName, "Snapshot");
        object value = snapshot.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(snapshot)
            ?? throw new MissingMemberException(snapshot.GetType().FullName, propertyName);
        return value is Enum ? value.ToString()! : value;
    }

    private static void SetParticipantConnected(GameEngine engine, int sideIndex, bool connected)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        MethodInfo method = runtime.GetType().GetMethod("SetParticipantConnected", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(runtime.GetType().FullName, "SetParticipantConnected");
        method.Invoke(runtime, new object[] { sideIndex, connected });
    }

    private static void SetParticipantReady(GameEngine engine, int sideIndex, bool ready)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        MethodInfo method = runtime.GetType().GetMethod("SetParticipantReady", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(runtime.GetType().FullName, "SetParticipantReady");
        method.Invoke(runtime, new object[] { sideIndex, ready });
    }

    private static void StartMatch(GameEngine engine)
    {
        SetParticipantReady(engine, sideIndex: 0, ready: true);
        SetParticipantReady(engine, sideIndex: 1, ready: true);
        TickUntil(engine, () => Equals(ReadSnapshot(engine, "Phase"), "InProgress"), 200,
            "Both ready players should enter battle after three seconds.");
        Assert.That(ReadSnapshot(engine, "CommittedTick"), Is.EqualTo(0));
    }

    private static IReadOnlyList<string> ReadOverlayText(ScreenOverlayBuffer overlay)
    {
        var result = new List<string>();
        foreach (ref readonly ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text))
            {
                result.Add(text);
            }
        }
        return result;
    }

    private static int FindMapEntityX(JsonElement entities, string name)
    {
        foreach (JsonElement entity in entities.EnumerateArray())
        {
            JsonElement overrides = entity.GetProperty("Overrides");
            if (string.Equals(overrides.GetProperty("Name").GetProperty("Value").GetString(), name, StringComparison.Ordinal))
            {
                return overrides.GetProperty("WorldPositionCm").GetProperty("Value").GetProperty("X").GetInt32();
            }
        }

        throw new InvalidOperationException($"Map entity '{name}' was not found.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, string because)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            Tick(engine, 1);
        }
        Assert.That(condition(), Is.True, because);
    }

    private static void AdvanceCommittedTicks(GameEngine engine, int count)
    {
        int current = (int)ReadSnapshot(engine, "CommittedTick");
        TickUntilCommittedTick(engine, checked(current + count));
    }

    private static void TickUntilCommittedTick(GameEngine engine, int targetTick)
    {
        int frameBudget = checked((targetTick - (int)ReadSnapshot(engine, "CommittedTick")) * 4 + 8);
        TickUntil(
            engine,
            () => (int)ReadSnapshot(engine, "CommittedTick") >= targetTick,
            frameBudget,
            $"The deterministic simulation should commit tick {targetTick}.");
    }

    private static void TickUntilCountdown(GameEngine engine, int remainingTicks)
    {
        TickUntil(
            engine,
            () => (int)ReadSnapshot(engine, "CountdownRemainingTicks") <= remainingTicks,
            200,
            $"The ready countdown should reach {remainingTicks} ticks remaining.");
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        engine.SetService(CoreServiceKeys.InputHandler, new PlayerInputHandler(new NullInputBackend(), inputConfig));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static string FindRepoRoot()
    {
        string? directory = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "src", "Core", "Ludots.Core.csproj")))
            {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
