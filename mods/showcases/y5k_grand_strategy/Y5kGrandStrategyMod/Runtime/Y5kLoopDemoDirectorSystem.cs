using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Tasks;
using StrategicDomainMod.Components;
using StrategicDomainMod.Runtime;
using UiRegionsMod;
using UiRegionsMod.Runtime;

namespace Y5kGrandStrategyMod.Runtime;

/// <summary>
/// Scripted five-loop demo for capture/recording. Advances world + HUD on presentation frames.
/// </summary>
public sealed class Y5kLoopDemoDirectorSystem : ISystem<float>
{
	private readonly World _world;
	private readonly StrategicDomainRuntime _domain;
	private readonly ProviderServices _providers;
	private readonly ActivityRuntimeService _activities;
	private readonly TaskRuntimeService _tasks;
	private readonly Y5kDemoState _state;
	private readonly Action _refreshHud;
	private int _frame;
	private bool _disposed;
	private Entity _activityScope = Entity.Null;

	public Y5kLoopDemoDirectorSystem(
		World world,
		StrategicDomainRuntime domain,
		ProviderServices providers,
		ActivityRuntimeService activities,
		TaskRuntimeService tasks,
		Y5kDemoState state,
		Action refreshHud)
	{
		_world = world ?? throw new ArgumentNullException(nameof(world));
		_domain = domain ?? throw new ArgumentNullException(nameof(domain));
		_providers = providers ?? throw new ArgumentNullException(nameof(providers));
		_activities = activities ?? throw new ArgumentNullException(nameof(activities));
		_tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
		_state = state ?? throw new ArgumentNullException(nameof(state));
		_refreshHud = refreshHud ?? throw new ArgumentNullException(nameof(refreshHud));
	}

	public int Frame => _frame;
	public Y5kDemoState State => _state;

	public void Initialize()
	{
	}

	public void BeforeUpdate(in float dt)
	{
	}

	public void Update(in float dt)
	{
		if (_disposed)
		{
			return;
		}

		_frame++;
		// ~1s per beat at 60fps presentation — compact enough for capture, still readable on HUD.
		switch (_frame)
		{
			case 60:
				SetPhase("supply", "补给环", "枢纽易主，补给网断裂；断补活动待抉择。");
				_domain.TransferSettlementOwner(2, newOwner: 2);
				EnsureSupplyActivity();
				break;
			case 120:
				ResolveForcedActivity("hold");
				_tasks.EmitSignal("supply.recovered");
				SetPhase("supply_resolved", "补给环·已抉择", "选择硬扛宽限期；补给任务推进。");
				break;
			case 180:
				ExecuteEffect(
					"combat.siege_invest",
					new Dictionary<string, object?>
					{
						["settlement_key"] = 3,
						["path"] = "garrison",
						["amount"] = 25f,
					});
				_tasks.EmitSignal("siege.breached");
				SetPhase("siege_garrison", "攻防环·完好路径", "打空兵力储备池，归属未变，进入可接管。");
				break;
			case 240:
				if (_domain.GetDefense(2).ControlState == SettlementControlState.Intact)
				{
					ExecuteEffect(
						"combat.siege_invest",
						new Dictionary<string, object?>
						{
							["settlement_key"] = 2,
							["path"] = "wall",
							["amount"] = 15f,
							["has_siege_capability"] = true,
						});
				}

				SetPhase("siege_wall", "攻防环·损毁路径", "对平行标的打空城防耐久，进入损毁。");
				break;
			case 300:
				ExecuteEffect(
					"city_control.commit_troops_takeover",
					new Dictionary<string, object?>
					{
						["settlement_key"] = 3,
						["faction_owner"] = 1,
						["troop_commitment"] = 8f,
					});
				_tasks.EmitSignal("siege.owner_transferred");
				AcceptOffered("task.dispose_captive");
				_activityScope = _world.Create();
				_activities.OfferOrActivate("activity.captive_disposal", _activityScope);
				SetPhase("takeover", "接管环", "投入兵力易主，英雄进入羁押；俘虏活动已弹出。");
				break;
			case 360:
				ResolveForcedActivity("release");
				_tasks.EmitSignal("captive.resolved");
				SetPhase("captive", "俘虏处置", "选择释放；羁押位清空。");
				break;
			case 420:
				ExecuteEffect(
					"population.appoint_governor",
					new Dictionary<string, object?>
					{
						["settlement_key"] = 3,
						["hero_key"] = 100,
					});
				_tasks.OfferOrStart("task.appoint_governor");
				_tasks.EmitSignal("governance.governor_appointed");
				OfferAndAccept("task.covert_probe");
				_activityScope = _world.Create();
				_activities.OfferOrActivate("activity.covert_exposure", _activityScope);
				SetPhase("governor", "治理环", "任命主官，产出可归因；隐秘暴露活动待确认。");
				break;
			case 480:
				ResolveForcedActivity("acknowledge");
				_tasks.EmitSignal("covert.exposed");
				_tasks.EmitSignal("skill.cast_committed");
				SetPhase("covert_skill", "谋略与技能", "隐秘失败必暴露；英雄主动技施放信号已发出。");
				break;
			case 540:
				SetPhase("complete", "五环演示完成", "补给→攻防→接管→治理→谋略技能 已串完。");
				break;
		}
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
	}

	public void AdvanceToCompletion(float dt = 1f / 60f)
	{
		while (_state.PhaseId != "complete" && _frame < 2000)
		{
			Update(dt);
		}

		if (_state.PhaseId != "complete")
		{
			throw new InvalidOperationException(
				$"Demo director stalled at frame={_frame} phase={_state.PhaseId}.");
		}
	}

	private void SetPhase(string id, string title, string detail)
	{
		_state.PhaseId = id;
		_state.PhaseTitle = title;
		_state.PhaseDetail = detail;
		_state.StepIndex++;
		_state.BulletinLines = new[]
		{
			title,
			detail,
		};
		_refreshHud();
		Console.WriteLine($"[Y5kDemo] step={_state.StepIndex} phase={id} :: {title}");
	}

	private void EnsureSupplyActivity()
	{
		foreach (ActivityView view in _activities.CaptureViews())
		{
			if (view.State == ActivityInstanceState.Active &&
			    string.Equals(view.ActivityId, "activity.supply_strain", StringComparison.Ordinal))
			{
				return;
			}
		}

		_activityScope = _world.Create();
		_activities.OfferOrActivate("activity.supply_strain", _activityScope);
	}

	private void ResolveForcedActivity(string optionId)
	{
		foreach (ActivityView view in _activities.CaptureViews())
		{
			if (view.State == ActivityInstanceState.Active &&
			    view.DispatchPolicy == ActivityDispatchPolicy.Forced)
			{
				_activities.ResolveOption(view.Entity, optionId);
				return;
			}
		}

		throw new InvalidOperationException($"No forced activity to resolve with option '{optionId}'.");
	}

	private void ExecuteEffect(string key, Dictionary<string, object?> parameters)
	{
		IEffectHandler handler = _providers.Effects.MustGet(key, out _);
		var context = new ProviderExecutionContext(
			_world,
			_world.Create(),
			ProviderContextBinding.CreateBindings());
		handler.Execute(new ProviderEffectCall(key, "context.subject", parameters, 0), context);
	}

	private void AcceptOffered(string taskId)
	{
		foreach (TaskView view in _tasks.CaptureViews())
		{
			if (string.Equals(view.TaskId, taskId, StringComparison.Ordinal) &&
			    view.State == TaskInstanceState.Offered)
			{
				_tasks.Accept(view.Entity);
				return;
			}
		}
	}

	private void OfferAndAccept(string taskId)
	{
		Entity entity = _tasks.OfferOrStart(taskId);
		if (_tasks.TryGetView(entity, out TaskView view) && view.State == TaskInstanceState.Offered)
		{
			_tasks.Accept(entity);
		}
	}

	public static void WireBulletin(GameEngine engine, Y5kDemoState state)
	{
		ArgumentNullException.ThrowIfNull(engine);
		ArgumentNullException.ThrowIfNull(state);
		UiRegionsRuntime runtime = engine.GetService(UiRegionsServiceKeys.Runtime)
			?? throw new InvalidOperationException("UiRegionsRuntime missing.");
		runtime.BulletinProvider = () => state.BulletinLines;
	}
}
