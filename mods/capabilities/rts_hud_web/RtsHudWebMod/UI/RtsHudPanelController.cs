using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surfaces;
using RtsProductionCapabilityMod.Runtime;

namespace RtsHudWebMod.UI;

internal sealed class RtsHudPanelController
{
	private ReactivePage<RtsProductionSnapshot>? _page;
	private GameEngine? _engine;
	private RtsProductionRuntime? _runtime;
	private UiSurfaceLeaseHandle _lease = UiSurfaceLeaseHandle.Invalid;

	public void MountOrRefresh(GameEngine engine)
	{
		_engine = engine;
		_runtime = ResolveRuntime(engine);
		if (_runtime == null)
		{
			ClearIfOwned(engine);
			return;
		}

		RtsProductionSnapshot snapshot = _runtime.BuildSnapshot(engine);
		if (!snapshot.ScenarioReady)
		{
			ClearIfOwned(engine);
			return;
		}

		if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
		{
			return;
		}

		IUiSurfaceLeaseService leases = engine.GetService(RtsHudWebServiceKeys.UiSurfaceLeaseService)
			?? throw new InvalidOperationException("RtsHudWebMod requires UiSurfaceLeaseService.");
		if (!_lease.IsValid || !leases.TryRevalidate(_lease))
		{
			_lease = leases.Acquire(new UiSurfaceLeaseRequest(UiSurfaceKind.RetainedUi, RtsHudWebIds.MainSegment, RtsHudWebIds.OwnerId, Exclusive: true));
		}

		if (_page == null)
		{
			var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
			var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
			_page = new ReactivePage<RtsProductionSnapshot>(textMeasurer, imageSizeProvider, snapshot, BuildRoot);
		}
		else
		{
			_page.SetState(_ => snapshot);
		}

		if (!ReferenceEquals(root.Scene, _page.Scene))
		{
			root.MountScene(_page.Scene);
		}

		root.IsDirty = true;
	}

	private UiElementBuilder BuildRoot(ReactiveContext<RtsProductionSnapshot> context)
	{
		RtsProductionSnapshot state = context.State;
		RtsFactionSnapshot current = ResolveCurrentFaction(state);
		return Ui.Column(
				BuildTopBar(state, current),
				Ui.Row(
						BuildLeftRoster(state, current),
						Ui.Column().FlexGrow(1f),
						BuildRightConsole(state))
					.Padding(16f, 10f)
					.Align(UiAlignItems.Start)
					.Gap(12f)
					.FlexGrow(1f),
				BuildCommandBand(state, current))
			.WidthPercent(100f)
			.HeightPercent(100f)
			.Background("#05070A28")
			.ZIndex(60);
	}

	private UiElementBuilder BuildTopBar(RtsProductionSnapshot state, RtsFactionSnapshot current)
	{
		var resourceNodes = new List<UiElementBuilder>();
		for (int i = 0; i < current.Resources.Count; i++)
		{
			ResourceAmountConfig resource = current.Resources[i];
			resourceNodes.Add(
				Ui.Column(
						Ui.Text(resource.Resource).FontSize(9f).Bold().Color("#96A3B5"),
						Ui.Text(resource.Amount.ToString()).FontSize(15f).Bold().Color("#F7F2D8"))
					.Width(90f)
					.Padding(7f, 5f)
					.Background("#D80B1017")
					.Border(1f, Color("#335F728A"))
					.Radius(4f));
		}

		var factionButtons = new List<UiElementBuilder>();
		for (int i = 0; i < state.Factions.Count; i++)
		{
			RtsFactionSnapshot faction = state.Factions[i];
			bool active = string.Equals(faction.Id, state.CurrentFactionId, StringComparison.Ordinal);
			factionButtons.Add(
				Ui.Button(faction.Label, _ => Run(runtime => runtime.SelectFaction(_engine!, faction.Id)))
					.Padding(9f, 6f)
					.Radius(4f)
					.Background(active ? "#273418" : "#121923")
					.Border(1f, Color(active ? faction.Accent : "#33445566"))
					.Color(active ? "#F7F2D8" : "#C8D2DE")
					.FontSize(11f));
		}

		return Ui.Row(
				Ui.Column(
						Ui.Text(state.Title).FontSize(18f).Bold().Color("#F7F2D8"),
						Ui.Text($"{state.Flavor} / {state.HudStyle}").FontSize(11f).Color("#9FB0C2"))
					.Width(280f),
				Ui.Row(resourceNodes.ToArray()).Gap(8f).FlexGrow(1f),
				Ui.Row(factionButtons.ToArray()).Gap(6f).Wrap())
			.Padding(14f, 10f)
			.Background("#F00A0F16")
			.Border(1f, Color("#22384A5F"))
			.Gap(12f)
			.Align(UiAlignItems.Center);
	}

	private UiElementBuilder BuildLeftRoster(RtsProductionSnapshot state, RtsFactionSnapshot current)
	{
		return BuildPanel(
				"Participant View",
				Ui.Text(current.Label).FontSize(18f).Bold().Color(current.Accent),
				Ui.Text($"Player {current.PlayerId} / Team {current.TeamId}").FontSize(11f).Color("#C8D2DE"),
				Ui.Text(current.AiControlled ? "AI macro enabled" : "Human view").FontSize(11f).Color(current.AiControlled ? "#8DE3AE" : "#7DD3FC"),
				BuildEntityList("Buildings", current.Buildings),
				BuildEntityList("Units", current.Units),
				Ui.Text(state.AiStatus).FontSize(10f).Color("#96A3B5").WhiteSpace(UiWhiteSpace.Normal))
			.Width(330f);
	}

	private UiElementBuilder BuildEntityList(string title, IReadOnlyList<RtsProductionEntityRecord> records)
	{
		var rows = new List<UiElementBuilder>();
		int count = Math.Min(records.Count, 5);
		for (int i = 0; i < count; i++)
		{
			RtsProductionEntityRecord record = records[i];
			rows.Add(
				Ui.Row(
						Ui.Text(record.Label).FontSize(11f).Color("#EDF3F8").WhiteSpace(UiWhiteSpace.Normal),
						Ui.Text(record.Produced ? "made" : "seed").FontSize(9f).Color(record.Produced ? "#8DE3AE" : "#96A3B5"))
					.Justify(UiJustifyContent.SpaceBetween)
					.Gap(8f));
		}

		if (rows.Count == 0)
		{
			rows.Add(Ui.Text("none").FontSize(11f).Color("#96A3B5"));
		}

		return Ui.Column(
				Ui.Text(title).FontSize(10f).Bold().Color("#F6D77C"),
				Ui.Column(rows.ToArray()).Gap(5f))
			.Gap(5f);
	}

	private UiElementBuilder BuildRightConsole(RtsProductionSnapshot state)
	{
		return Ui.Column(
				BuildTechPanel(state),
				BuildDiplomacyPanel(state),
				BuildTradePanel(state),
				BuildSavePanel(state))
			.Width(368f)
			.Gap(10f);
	}

	private UiElementBuilder BuildTechPanel(RtsProductionSnapshot state)
	{
		var rows = new List<UiElementBuilder>();
		for (int i = 0; i < state.Techs.Count; i++)
		{
			RtsTechNodeSnapshot tech = state.Techs[i];
			if (!string.Equals(tech.FactionId, state.CurrentFactionId, StringComparison.Ordinal))
			{
				continue;
			}

			string color = tech.State switch
			{
				TechNodeState.Completed => "#8DE3AE",
				TechNodeState.InProgress => "#7DD3FC",
				TechNodeState.Available => "#F6D77C",
				_ => "#718096",
			};
			UiElementBuilder row = Ui.Row(
					Ui.Column(
							Ui.Text(tech.Label).FontSize(11f).Bold().Color("#EDF3F8"),
							Ui.Text($"{tech.State} / {DependencyLabel(tech.Requires)}").FontSize(9f).Color(color).WhiteSpace(UiWhiteSpace.Normal))
						.FlexGrow(1f),
					Ui.Button("Research", _ => Run(runtime => runtime.StartResearch(_engine!, tech.Id)))
						.Padding(7f, 5f)
						.Radius(4f)
						.Background(tech.State == TechNodeState.Available ? "#493A16" : "#161D25")
						.Border(1f, Color(tech.State == TechNodeState.Available ? "#66F6D77C" : "#33445566"))
						.Color("#EDF3F8")
						.FontSize(10f))
				.Align(UiAlignItems.Center)
				.Gap(8f);
			rows.Add(row);
		}

		return BuildPanel("Progression Tree", Ui.Column(rows.ToArray()).Gap(7f));
	}

	private UiElementBuilder BuildDiplomacyPanel(RtsProductionSnapshot state)
	{
		var rows = new List<UiElementBuilder>();
		for (int i = 0; i < state.Treaties.Count; i++)
		{
			RtsTreatySnapshot treaty = state.Treaties[i];
			rows.Add(
				Ui.Column(
						Ui.Row(
								Ui.Text(treaty.Label).FontSize(11f).Bold().Color("#EDF3F8").FlexGrow(1f),
								Ui.Text($"Trust {treaty.Trust}").FontSize(10f).Color(treaty.TradePact ? "#8DE3AE" : "#FFB38A"))
							.Gap(8f),
						Ui.Row(
								Ui.Button("Sign", _ => Run(runtime => runtime.SignTreaty(_engine!, treaty.Id))).Padding(7f, 5f).Radius(4f).Background("#173522").Border(1f, Color("#558DE3AE")).Color("#EDF3F8").FontSize(10f),
								Ui.Button("Tear", _ => Run(runtime => runtime.TearTreaty(_engine!, treaty.Id))).Padding(7f, 5f).Radius(4f).Background("#3A1C18").Border(1f, Color("#66FFB38A")).Color("#EDF3F8").FontSize(10f),
								Ui.Text(treaty.Embargo ? "Embargo" : treaty.TradePact ? "TradePact" : treaty.AtWar ? "AtWar" : "Neutral").FontSize(10f).Color("#C8D2DE"))
							.Gap(6f))
					.Gap(5f));
		}

		return BuildPanel("Diplomacy", Ui.Column(rows.ToArray()).Gap(8f));
	}

	private UiElementBuilder BuildTradePanel(RtsProductionSnapshot state)
	{
		var rows = new List<UiElementBuilder>();
		for (int i = 0; i < state.TradeOffers.Count; i++)
		{
			RtsTradeOfferSnapshot offer = state.TradeOffers[i];
			rows.Add(
				Ui.Column(
						Ui.Text(offer.Label).FontSize(11f).Bold().Color("#EDF3F8"),
						Ui.Text($"{offer.GiveAmount} {offer.GiveResource} -> {offer.ReceiveAmount} {offer.ReceiveResource} / {offer.State}")
							.FontSize(10f)
							.Color(offer.State == TradeOfferState.Accepted ? "#8DE3AE" : offer.State == TradeOfferState.RelationshipDenied ? "#FFB38A" : "#C8D2DE")
							.WhiteSpace(UiWhiteSpace.Normal),
						Ui.Row(
								Ui.Button("Offer", _ => Run(runtime => runtime.ProposeTrade(offer.Id))).Padding(7f, 5f).Radius(4f).Background("#1A2635").Border(1f, Color("#557DD3FC")).Color("#EDF3F8").FontSize(10f),
								Ui.Button("Accept", _ => Run(runtime => runtime.AcceptTrade(_engine!, offer.Id))).Padding(7f, 5f).Radius(4f).Background("#173522").Border(1f, Color("#558DE3AE")).Color("#EDF3F8").FontSize(10f),
								Ui.Button("Reject", _ => Run(runtime => runtime.RejectTrade(offer.Id))).Padding(7f, 5f).Radius(4f).Background("#2B1D24").Border(1f, Color("#55C94F46")).Color("#EDF3F8").FontSize(10f))
							.Gap(6f))
					.Gap(5f));
		}

		return BuildPanel("Trade Offers", Ui.Column(rows.ToArray()).Gap(8f));
	}

	private UiElementBuilder BuildSavePanel(RtsProductionSnapshot state)
	{
		return BuildPanel(
			"Save",
			Ui.Row(
					Ui.Text($"{state.SaveSlotCount} slot(s)").FontSize(11f).Color("#C8D2DE").FlexGrow(1f),
					Ui.Button("Check Storage", _ => Run(runtime => runtime.SaveSmoke(_engine!)))
						.Padding(7f, 5f)
						.Radius(4f)
						.Background("#1A2635")
						.Border(1f, Color("#557DD3FC"))
						.Color("#EDF3F8")
						.FontSize(10f))
				.Gap(8f),
			Ui.Text(state.SaveStatus).FontSize(10f).Color("#96A3B5").WhiteSpace(UiWhiteSpace.Normal));
	}

	private UiElementBuilder BuildCommandBand(RtsProductionSnapshot state, RtsFactionSnapshot current)
	{
		var productionButtons = new List<UiElementBuilder>();
		for (int i = 0; i < state.AvailableProduction.Count; i++)
		{
			ProductionLineConfig line = state.AvailableProduction[i];
			productionButtons.Add(
				Ui.Button(Compact(line.Label), _ => Run(runtime => runtime.StartProduction(_engine!, line.Id)))
					.Width(78f)
					.Height(58f)
					.Padding(6f)
					.Radius(4f)
					.Background("#121923")
					.Border(1f, Color("#446B7C91"))
					.Color("#EDF3F8")
					.FontSize(10f)
					.WhiteSpace(UiWhiteSpace.Normal));
		}

		var queueRows = new List<UiElementBuilder>();
		for (int i = 0; i < current.Queue.Count; i++)
		{
			RtsProductionQueueItem item = current.Queue[i];
			queueRows.Add(
				Ui.Column(
						Ui.Text(item.Label).FontSize(10f).Bold().Color("#EDF3F8").WhiteSpace(UiWhiteSpace.Normal),
						Ui.Text($"{item.ProgressPercent}%").FontSize(9f).Color("#F6D77C"))
					.Width(82f)
					.Height(42f)
					.Padding(6f, 4f)
					.Background("#0E151D")
					.Border(1f, Color("#33445566"))
					.Radius(4f));
		}

		if (queueRows.Count == 0)
		{
			queueRows.Add(Ui.Text("Queue idle").FontSize(11f).Color("#96A3B5"));
		}

		return Ui.Row(
				BuildPanel(
						"Command Deck",
						Ui.Row(productionButtons.ToArray()).Gap(6f).Wrap(),
						Ui.Text(state.Summary).FontSize(10f).Color("#96A3B5").WhiteSpace(UiWhiteSpace.Normal))
					.Width(650f),
				BuildPanel("Production Queue", Ui.Row(queueRows.ToArray()).Gap(6f).Wrap()).FlexGrow(1f),
				BuildPanel("Acceptance", Ui.Column(BuildAcceptanceLines(state).ToArray()).Gap(4f)).Width(320f))
			.Padding(14f, 10f)
			.Gap(12f)
			.Background("#F00A0F16")
			.Border(1f, Color("#22384A5F"))
			.Align(UiAlignItems.Stretch);
	}

	private static List<UiElementBuilder> BuildAcceptanceLines(RtsProductionSnapshot state)
	{
		var lines = new List<UiElementBuilder>();
		int count = Math.Min(state.Acceptance.Count, 4);
		for (int i = 0; i < count; i++)
		{
			lines.Add(Ui.Text(state.Acceptance[i]).FontSize(10f).Color(i == 0 ? "#F6D77C" : "#C8D2DE").WhiteSpace(UiWhiteSpace.Normal));
		}

		return lines;
	}

	private UiElementBuilder BuildPanel(string title, params UiElementBuilder[] children)
	{
		var all = new List<UiElementBuilder>
		{
			Ui.Text(title).FontSize(11f).Bold().Color("#F6D77C")
		};
		all.AddRange(children);
		return Ui.Card(all.ToArray())
			.Padding(11f)
			.Gap(8f)
			.Radius(4f)
			.Background("#D80B1017")
			.Border(1f, Color("#33445566"))
			.BoxShadow(0f, 8f, 22f, Color("#55000000"));
	}

	private static RtsFactionSnapshot ResolveCurrentFaction(RtsProductionSnapshot state)
	{
		for (int i = 0; i < state.Factions.Count; i++)
		{
			if (string.Equals(state.Factions[i].Id, state.CurrentFactionId, StringComparison.Ordinal))
			{
				return state.Factions[i];
			}
		}

		return state.Factions.Count > 0
			? state.Factions[0]
			: new RtsFactionSnapshot(string.Empty, "No Faction", 0, 0, "#F6D77C", false, Array.Empty<ResourceAmountConfig>(), Array.Empty<RtsProductionEntityRecord>(), Array.Empty<RtsProductionEntityRecord>(), Array.Empty<RtsProductionQueueItem>());
	}

	private void Run(Action<RtsProductionRuntime> action)
	{
		if (_runtime == null || _engine == null)
		{
			return;
		}

		action(_runtime);
	}

	private void ClearIfOwned(GameEngine engine)
	{
		if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root &&
			_page != null &&
			ReferenceEquals(root.Scene, _page.Scene))
		{
			root.ClearScene();
		}

		if (_lease.IsValid &&
			engine.GetService(RtsHudWebServiceKeys.UiSurfaceLeaseService) is IUiSurfaceLeaseService leases)
		{
			leases.Release(_lease);
			_lease = UiSurfaceLeaseHandle.Invalid;
		}
	}

	private static RtsProductionRuntime? ResolveRuntime(GameEngine engine)
	{
		return engine.GlobalContext.TryGetValue(RtsProductionIds.RuntimeKey, out object? runtimeObj) &&
			   runtimeObj is RtsProductionRuntime runtime
			? runtime
			: null;
	}

	private static UiColor Color(string hex)
	{
		if (!UiColor.TryParse(hex, out UiColor color))
		{
			throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
		}

		return color;
	}

	private static string DependencyLabel(IReadOnlyList<string> dependencies)
		=> dependencies.Count == 0 ? "root" : string.Join(", ", dependencies);

	private static string Compact(string label)
		=> label.Length <= 18 ? label : label[..18];
}
