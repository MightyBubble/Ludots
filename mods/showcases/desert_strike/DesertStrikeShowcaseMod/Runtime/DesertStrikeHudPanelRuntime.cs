using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace DesertStrikeShowcaseMod.Runtime
{
    public sealed class DesertStrikeHudPanelRuntime
    {
        public const string PanelType = "panel.desert_strike.hud";

        private readonly GameEngine _engine;
        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly UiPanelActivationStore _activationStore;
        private readonly PanelActivationApi _activationApi;
        private readonly PanelRegionHost _regionHost;
        private readonly PanelTemplate _template;
        private readonly PanelProjectionReader _reader;
        private readonly GraphOutputValueStore _graphOutputs;
        private readonly ReactivePage<HudSnapshot> _page;
        private readonly IUiSurfaceHost? _surfaceHost;
        private readonly int _mineralsAttributeId;
        private readonly int _healthAttributeId;

        private Entity _scope = Entity.Null;
        private PanelInstance? _instance;
        private UiSurfaceLeaseHandle _lease;

        public DesertStrikeHudPanelRuntime(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config, IModContext ctx)
        {
            _engine = engine;
            _state = state;
            _config = config;
            _activationStore = new UiPanelActivationStore();
            _activationApi = new PanelActivationApi(_activationStore);
            _regionHost = new PanelRegionHost();

            engine.GetService(CoreServiceKeys.GasGraphRuntimeApi).BindPanelActivation(_activationApi);

            string templateUri = $"{ctx.ModId}:assets/Panels/desert_strike_hud.panel.json";
            using Stream templateStream = ctx.VFS.GetStream(templateUri);
            using var templateReader = new StreamReader(templateStream);
            _template = PanelTemplateLoader.Load(templateReader.ReadToEnd());

            _graphOutputs = engine.GetService(CoreServiceKeys.GraphOutputValueStore);
            _reader = new PanelProjectionReader(
                engine.World,
                _graphOutputs,
                AttributeRegistry.GetId,
                engine.GetService(CoreServiceKeys.GraphLookupTableRegistry));

            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                _surfaceHost = surfaceHost;
                _page = new ReactivePage<HudSnapshot>(
                    engine.GetService(CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer
                        ?? throw new InvalidOperationException("UiTextMeasurer service not registered."),
                    engine.GetService(CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider
                        ?? throw new InvalidOperationException("UiImageSizeProvider service not registered."),
                    default,
                    BuildRoot);
            }

            _mineralsAttributeId = EnsureAttributeId("Minerals");
            _healthAttributeId = EnsureAttributeId("Health");
        }

        public UiPanelActivationStore ActivationStore => _activationStore;

        public void BindScope(Entity scope)
        {
            _scope = scope;
            _instance = new PanelInstance(_template, scope);
            _activationApi.ShowPanel(PanelType);
        }

        public void Update()
        {
            if (_surfaceHost == null)
            {
                return;
            }

            (IReadOnlyList<string> activated, IReadOnlyList<string> deactivated) = _regionHost.Reconcile(_activationStore);
            bool showRequested = _activationStore.IsVisible(PanelType);

            if (Contains(deactivated, PanelType))
            {
                _surfaceHost.ReleaseLease(ref _lease);
            }

            if (showRequested && World.IsAlive(_scope))
            {
                WriteGraphOutputs();
                if (Contains(activated, PanelType) || !_lease.IsValid || !_surfaceHost.Revalidate(_lease))
                {
                    _surfaceHost.PublishReactivePage(
                        ref _lease,
                        new UiSurfaceLeaseRequest("DesertStrike.Hud", UiSurfaceSegment.Overlay, priority: 60),
                        _page);
                }

                _page.SetState(_ => BuildSnapshot());
                _surfaceHost.InvalidateLease(_lease);
            }
            else
            {
                _surfaceHost.ReleaseLease(ref _lease);
            }
        }

        private World World => _engine.World;

        private void WriteGraphOutputs()
        {
            int step = _engine.GetService(CoreServiceKeys.Clock).Now(ClockDomainId.FixedFrame);
            SetOutput("desert_strike.hud.minerals", ReadMinerals(_state.PlayerBase));
            SetOutput("desert_strike.hud.waveSeconds", Math.Max(0, _state.NextWaveStep - step));
            SetOutput("desert_strike.hud.waveNumber", _state.WaveNumber);
            SetOutput("desert_strike.hud.playerQueue", _state.PlayerQueue.Count);
            SetOutput("desert_strike.hud.aiMinerals", ReadMinerals(_state.AiBase));
            SetOutput("desert_strike.hud.aiQueue", _state.AiQueue.Count);
            SetOutput("desert_strike.hud.playerBaseHp", ReadHealth(_state.PlayerBase));
            SetOutput("desert_strike.hud.aiBaseHp", ReadHealth(_state.AiBase));
            SetOutput("desert_strike.hud.winner", _state.GameOver ? _state.WinnerPlayerId : 0);
        }

        private void SetOutput(string key, float value)
        {
            _graphOutputs.SetFloat(_scope, key, value);
        }

        private HudSnapshot BuildSnapshot()
        {
            PanelVariableSet values = _instance!.Evaluate(_reader);
            return new HudSnapshot(
                values.Get("minerals"),
                values.Get("waveSeconds"),
                values.Get("waveNumber"),
                values.Get("playerQueue"),
                values.Get("aiMinerals"),
                values.Get("aiQueue"),
                values.Get("playerBaseHp"),
                values.Get("aiBaseHp"),
                values.Get("winner"));
        }

        private float ReadMinerals(Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.TryGet(baseEntity, out AttributeBuffer buffer))
            {
                return 0f;
            }

            return buffer.GetCurrent(_mineralsAttributeId);
        }

        private float ReadHealth(Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.TryGet(baseEntity, out AttributeBuffer buffer))
            {
                return 0f;
            }

            return buffer.GetCurrent(_healthAttributeId);
        }

        private static bool Contains(IReadOnlyList<string> items, string value)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }

        private static UiElementBuilder BuildRoot(ReactiveContext<HudSnapshot> context)
        {
            HudSnapshot hud = context.State;
            int waveSeconds = (int)hud.WaveSeconds;
            string banner = hud.Winner switch
            {
                1 => "胜利！敌方基地已被摧毁",
                2 => "失败！我方基地已被摧毁",
                _ => string.Empty,
            };

            return Ui.Panel(
                    Ui.Card(
                        Ui.Column(
                                Ui.Text("沙漠风暴 · Desert Strike (Tug of War)")
                                    .FontSize(18f)
                                    .Bold()
                                    .Color("#F2C36B"),
                                Ui.Text($"水晶: {hud.Minerals:0}    下一波: {waveSeconds / 60:0}m{waveSeconds % 60:00}s    波次: {hud.WaveNumber:0}")
                                    .FontSize(14f)
                                    .Color("#9FE6A8"),
                                Ui.Text($"本波待发: {hud.PlayerQueue:0} 单位    AI 水晶: {hud.AiMinerals:0}    AI 待发: {hud.AiQueue:0}")
                                    .FontSize(14f)
                                    .Color("#C4E4FF"),
                                Ui.Text($"我方基地 HP: {hud.PlayerBaseHp:0}    敌方基地 HP: {hud.AiBaseHp:0}")
                                    .FontSize(14f)
                                    .Color("#FFB3B3"),
                                Ui.Text(banner.Length > 0
                                        ? banner
                                        : "玩法：选中我方基地（绿圈）→ 点击下方按钮购买单位 → 每 30 秒自动出兵，摧毁敌方基地获胜")
                                    .FontSize(13f)
                                    .Color(banner.Length > 0 ? "#FFFFFF" : "#C9C9C9"))
                            .Padding(10f)
                            .Gap(3f))
                        .Background("#0B1520D8"))
                .Width(0f)
                .Height(0f);
        }

        public readonly record struct HudSnapshot(
            float Minerals,
            float WaveSeconds,
            float WaveNumber,
            float PlayerQueue,
            float AiMinerals,
            float AiQueue,
            float PlayerBaseHp,
            float AiBaseHp,
            float Winner);
    }
}
