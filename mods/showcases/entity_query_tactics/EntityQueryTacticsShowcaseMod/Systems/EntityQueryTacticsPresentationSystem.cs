using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;
using EntityQueryTacticsShowcaseMod.Runtime;

namespace EntityQueryTacticsShowcaseMod.Systems
{
    internal sealed class EntityQueryTacticsPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly EntityQueryTacticsScenarioState _state;
        private readonly NarrativeFrontendSurfaceModel[] _surfaces = new NarrativeFrontendSurfaceModel[5];
        private readonly NarrativeFrontendSurfaceItem[] _selectionItems = new NarrativeFrontendSurfaceItem[3];
        private readonly NarrativeFrontendSurfaceItem[] _queryItems = new NarrativeFrontendSurfaceItem[4];
        private readonly NarrativeFrontendSurfaceItem[] _relationItems = new NarrativeFrontendSurfaceItem[5];
        private readonly NarrativeFrontendSurfaceItem[] _cacheItems = new NarrativeFrontendSurfaceItem[4];
        private PresentationSignature _lastSignature;
        private bool _hasLastSignature;
        private bool _frontendVisible;

        private EntityQueryTacticsShowcaseConfig Config => _state.Config;
        private EntityQueryTacticsFrontendConfig Frontend => _state.FrontendConfig;
        private EntityQueryTacticsScenarioContext? ScenarioContext => _state.ScenarioContext;

        public EntityQueryTacticsPresentationSystem(GameEngine engine, EntityQueryTacticsScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsShowcaseMap() || ScenarioContext == null)
            {
                ClearFrontend();
                return;
            }

            PublishFrontend();
            if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is GroundOverlayBuffer ground)
            {
                DrawWorldHighlights(ground);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsShowcaseMap()
        {
            return string.Equals(_engine.CurrentMapSession?.MapId.Value, Config.MapId, StringComparison.OrdinalIgnoreCase);
        }

        private void PublishFrontend()
        {
            if (_engine.GetService(NarrativeFrontendServiceKeys.Service) is not NarrativeFrontendService frontend)
            {
                return;
            }

            PresentationSignature signature = BuildPresentationSignature();
            if (_frontendVisible && _hasLastSignature && signature.Equals(_lastSignature))
            {
                return;
            }

            _selectionItems[0] = new NarrativeFrontendSurfaceItem("Drag box", $"{_state.UiBoxCount} preview", string.IsNullOrWhiteSpace(_state.UiBoxNames) ? "empty" : _state.UiBoxNames, AccentHex: "#60A5FA", Active: _state.UiBoxCount > 0);
            _selectionItems[1] = new NarrativeFrontendSurfaceItem("Committed squad", $"{_state.FormalSelectionCount} units", string.IsNullOrWhiteSpace(_state.SelectedNames) ? "press Enter to commit" : _state.SelectedNames, AccentHex: "#93C5FD", Active: _state.FormalSelectionCount > 0);
            _selectionItems[2] = new NarrativeFrontendSurfaceItem("Friendly query", _state.SelectedCount.ToString(), ReadName(_state.SelectedBest), AccentHex: "#6EE7B7", Active: _state.SelectedCount > 0);

            _queryItems[0] = new NarrativeFrontendSurfaceItem("Squad count", _state.SelectedCount.ToString(), BuildSelectedFilterSummary(), AccentHex: "#FDE68A", Active: _state.SelectedCount > 0);
            _queryItems[1] = new NarrativeFrontendSurfaceItem("Command power", _state.SelectedCommandPowerSum.ToString("0"), "sum", AccentHex: "#FACC15");
            _queryItems[2] = new NarrativeFrontendSurfaceItem("Supply held", _state.SelectedSupplySum.ToString("0"), "sum", AccentHex: "#FDE68A");
            _queryItems[3] = new NarrativeFrontendSurfaceItem("Best unit", ReadName(_state.SelectedBest), "highest power", AccentHex: "#A7F3D0", Active: _state.SelectedBest != Entity.Null);

            _relationItems[0] = new NarrativeFrontendSurfaceItem("Priority enemies", _state.ThreatCount.ToString(), "threat + priority", AccentHex: "#FB7185", Active: _state.ThreatCount > 0);
            _relationItems[1] = new NarrativeFrontendSurfaceItem("Threat sum", _state.ThreatSum.ToString(), "total danger", AccentHex: "#FDA4AF");
            _relationItems[2] = new NarrativeFrontendSurfaceItem("Threat avg", _state.ThreatAverage.ToString(), "average danger", AccentHex: "#FBCFE8");
            _relationItems[3] = new NarrativeFrontendSurfaceItem("Top threat", _state.ThreatMax.ToString(), ReadName(_state.ThreatBest), AccentHex: "#F43F5E", Active: _state.ThreatBest != Entity.Null);
            _relationItems[4] = new NarrativeFrontendSurfaceItem("Pressure path", $"{_state.PressurePulseCount} pulse", $"graph x{_state.GraphExecutionCount} | frame {_state.LastFrameMs:0.0}ms", AccentHex: "#FDBA74", Active: _state.PressurePulseCount > 0);

            _cacheItems[0] = new NarrativeFrontendSurfaceItem("Formation", $"{_state.FormalSelectionCount} -> {_state.FormationCount}", "Routed Scout excluded", AccentHex: "#A78BFA", Active: _state.FormationCount > 0);
            _cacheItems[1] = new NarrativeFrontendSurfaceItem("Max command", _state.FormationMaxCommandPower.ToString("0"), ReadName(_state.FormationBest), AccentHex: "#C4B5FD");
            _cacheItems[2] = new NarrativeFrontendSurfaceItem("Lowest supply", _state.FormationMinSupply.ToString("0"), "after exclusion", AccentHex: "#DDD6FE");
            _cacheItems[3] = new NarrativeFrontendSurfaceItem("Cache probe", _state.LastCacheProbeUnchanged ? "reused" : "pending", $"input rev {_state.FormationRevision} | graph rev {_state.FormationResultRevision}", AccentHex: _state.LastCacheProbeUnchanged ? "#86EFAC" : "#FDBA74", Active: _state.LastCacheProbeUnchanged);

            _surfaces[0] = CreateSurface(
                Frontend.PromptRibbon,
                NarrativeFrontendSurfaceKind.PromptRibbon,
                Config.Presentation.Title,
                Config.Presentation.ControlsLine,
                Config.Presentation.ArchitectureLine);
            _surfaces[1] = CreateSurface(
                Frontend.SelectionPanel,
                NarrativeFrontendSurfaceKind.StatusPanel,
                Frontend.SelectionPanel.Title,
                string.Empty,
                Frontend.SelectionPanel.Footer,
                _selectionItems);
            _surfaces[2] = CreateSurface(
                Frontend.QueryBoard,
                NarrativeFrontendSurfaceKind.RelationshipNotebook,
                Frontend.QueryBoard.Title,
                string.Empty,
                Frontend.QueryBoard.Footer,
                _queryItems);
            _surfaces[3] = CreateSurface(
                Frontend.RelationBoard,
                NarrativeFrontendSurfaceKind.RelationshipNotebook,
                Frontend.RelationBoard.Title,
                _state.ThreatNames,
                Frontend.RelationBoard.Footer,
                _relationItems);
            _surfaces[4] = CreateSurface(
                Frontend.CachePanel,
                NarrativeFrontendSurfaceKind.FlowReview,
                Frontend.CachePanel.Title,
                string.Empty,
                Frontend.CachePanel.Footer,
                _cacheItems);

            frontend.Publish(new NarrativeFrontendPageState(
                Frontend.OwnerId,
                BuildPageSignature(in signature),
                true,
                Frontend.BackdropHex,
                _surfaces));
            _lastSignature = signature;
            _hasLastSignature = true;
            _frontendVisible = true;
        }

        private NarrativeFrontendSurfaceModel CreateSurface(
            EntityQueryTacticsSurfaceConfig config,
            NarrativeFrontendSurfaceKind kind,
            string title,
            string body,
            string footer = "",
            IReadOnlyList<NarrativeFrontendSurfaceItem>? items = null)
        {
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{Frontend.OwnerId}.{kind}.{config.ResolveAnchor()}",
                Kind: kind,
                Anchor: config.ResolveAnchor(),
                Title: string.IsNullOrWhiteSpace(title) ? config.Title : title,
                Subtitle: config.Eyebrow,
                Body: body,
                Footer: string.IsNullOrWhiteSpace(footer) ? config.Footer : footer,
                Items: items,
                Width: config.Width,
                OffsetX: config.OffsetX,
                OffsetY: config.OffsetY,
                ZIndex: config.ZIndex,
                AccentHex: config.AccentHex,
                BackgroundHex: config.BackgroundHex,
                BorderHex: config.BorderHex,
                ForegroundHex: config.ForegroundHex,
                MutedHex: config.MutedHex);
        }

        private string BuildSelectedFilterSummary()
        {
            return $"{Config.Scenario.PlayerTeamName} | squad template | {Config.Tags.Commandable}";
        }

        private PresentationSignature BuildPresentationSignature()
        {
            return new PresentationSignature(
                _state.UiBoxRevision,
                _state.FormalSelectionRevision,
                _state.FormationRevision,
                _state.FormationResultRevision,
                _state.HostileResultRevision,
                _state.SelectedCount,
                _state.SelectedCommandPowerSum,
                _state.SelectedSupplySum,
                _state.ThreatCount,
                _state.ThreatSum,
                _state.ThreatAverage,
                _state.ThreatMax,
                _state.PressurePulseCount,
                _state.GraphExecutionCount,
                _state.LastFrameMs,
                _state.FormationCount,
                _state.FormationMaxCommandPower,
                _state.FormationMinSupply,
                _state.SelectedBest,
                _state.ThreatBest,
                _state.FormationBest,
                _state.LastCacheProbeUnchanged,
                _state.Log.Count == 0 ? string.Empty : _state.Log[^1]);
        }

        private static string BuildPageSignature(in PresentationSignature signature)
        {
            return string.Join("|",
                signature.UiBoxRevision,
                signature.FormalSelectionRevision,
                signature.FormationRevision,
                signature.FormationResultRevision,
                signature.HostileResultRevision,
                signature.SelectedCount,
                signature.SelectedCommandPowerSum,
                signature.SelectedSupplySum,
                signature.ThreatCount,
                signature.ThreatSum,
                signature.ThreatAverage,
                signature.ThreatMax,
                signature.PressurePulseCount,
                signature.GraphExecutionCount,
                signature.LastFrameMs,
                signature.FormationCount,
                signature.FormationMaxCommandPower,
                signature.FormationMinSupply,
                signature.SelectedBest.Id,
                signature.SelectedBest.Version,
                signature.ThreatBest.Id,
                signature.ThreatBest.Version,
                signature.FormationBest.Id,
                signature.FormationBest.Version,
                signature.LastCacheProbeUnchanged,
                signature.LastLog);
        }

        private void DrawWorldHighlights(GroundOverlayBuffer ground)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            for (int i = 0; i < ScenarioContext.Allies.Length; i++)
            {
                AddRing(ground, ScenarioContext.Allies[i], new Vector4(0.1f, 0.55f, 1f, 0.12f), new Vector4(0.38f, 0.68f, 1f, 0.92f), 2.1f, 1.68f);
            }

            for (int i = 0; i < ScenarioContext.Enemies.Length; i++)
            {
                AddRing(ground, ScenarioContext.Enemies[i], new Vector4(1f, 0.2f, 0.26f, 0.08f), new Vector4(1f, 0.43f, 0.5f, 0.78f), 2.0f, 1.62f);
            }

            AddRing(ground, _state.SelectedBest, new Vector4(0.17f, 0.95f, 0.65f, 0.16f), new Vector4(0.43f, 0.91f, 0.72f, 0.96f), 2.55f, 2.06f);
            AddRing(ground, _state.ThreatBest, new Vector4(1f, 0.18f, 0.2f, 0.18f), new Vector4(1f, 0.3f, 0.42f, 1f), 2.7f, 2.2f);
            AddRing(ground, _state.FormationBest, new Vector4(0.52f, 0.38f, 1f, 0.13f), new Vector4(0.66f, 0.55f, 0.98f, 0.9f), 2.4f, 1.96f);
        }

        private void AddRing(GroundOverlayBuffer ground, Entity entity, Vector4 fill, Vector4 border, float radius, float innerRadius)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<VisualTransform>(entity))
            {
                return;
            }

            Vector3 center = _world.Get<VisualTransform>(entity).Position;
            center.Y = 0.08f;
            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Ring,
                Center = center,
                Radius = radius,
                InnerRadius = innerRadius,
                FillColor = fill,
                BorderColor = border,
                BorderWidth = 0.055f,
            });
        }

        private string ReadName(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return entity == Entity.Null ? "(none)" : $"Entity#{entity.Id}";
            }

            return _world.Get<Name>(entity).Value;
        }

        private void ClearFrontend()
        {
            if (_engine.GetService(NarrativeFrontendServiceKeys.Service) is NarrativeFrontendService frontend)
            {
                frontend.Clear(Frontend.OwnerId);
            }

            _frontendVisible = false;
        }

        private readonly record struct PresentationSignature(
            uint UiBoxRevision,
            uint FormalSelectionRevision,
            uint FormationRevision,
            uint FormationResultRevision,
            uint HostileResultRevision,
            int SelectedCount,
            float SelectedCommandPowerSum,
            float SelectedSupplySum,
            int ThreatCount,
            int ThreatSum,
            int ThreatAverage,
            int ThreatMax,
            uint PressurePulseCount,
            uint GraphExecutionCount,
            float LastFrameMs,
            int FormationCount,
            float FormationMaxCommandPower,
            float FormationMinSupply,
            Entity SelectedBest,
            Entity ThreatBest,
            Entity FormationBest,
            bool LastCacheProbeUnchanged,
            string LastLog);
    }
}
