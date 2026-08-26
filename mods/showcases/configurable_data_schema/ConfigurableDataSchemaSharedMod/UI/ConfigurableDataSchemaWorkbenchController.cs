using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using ConfigurableDataSchemaSharedMod.Runtime;

namespace ConfigurableDataSchemaSharedMod.UI;

internal sealed class ConfigurableDataSchemaWorkbenchController
{
    private ReactivePage<ConfigurableDataSchemaSnapshot>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public void MountOrRefresh(UIRoot root, GameEngine engine, ConfigurableDataSchemaSnapshot state)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }

        _engine = engine;
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer)!;
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider)!;
            _page = new ReactivePage<ConfigurableDataSchemaSnapshot>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.ConfigurableDataSchema.Workbench", UiSurfaceSegment.Overlay, priority: 40),
            _page);
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_lease.IsValid &&
            _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _page = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ConfigurableDataSchemaSnapshot> context)
    {
        ConfigurableDataSchemaSnapshot state = context.State;
        string validationColor = state.IsValid ? "#7CFC9A" : "#FF8A80";
        return Ui.Column(
                Ui.Panel(
                        Ui.Text("数据结构工作台").FontSize(20f).Bold().Color("#F4F7FB"),
                        Ui.Text(state.Guide).FontSize(12f).Color("#B7C3D4").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text($"Schema  {state.SchemaId}").FontSize(12f).Color("#E8EEF7"),
                        Ui.Text($"Preset  {state.PresetRecordId} → {state.WorkbenchRecordId}").FontSize(12f).Color("#E8EEF7"),
                        Ui.Text($"Binding  {state.BindingPath} = {state.BindingValueText} ({state.BindingTypeText})")
                            .FontSize(12f)
                            .Color("#E8EEF7")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text($"Tags  length {state.TagCount}").FontSize(12f).Color("#E8EEF7"),
                        Ui.Text($"Enum  {state.RarityName} / {state.RarityValue}").FontSize(12f).Color("#E8EEF7"),
                        Ui.Text($"Source  {state.SourceMode} · Panel  {state.ActivePanelId}")
                            .FontSize(12f)
                            .Color("#E8EEF7")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.IsValid
                                ? "校验  通过"
                                : $"校验  失败 ×{state.ErrorCount} · {state.FirstErrorPath}")
                            .FontSize(13f)
                            .Bold()
                            .Color(validationColor)
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Status).FontSize(12f).Color("#D7E2F0").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Row(
                                Button("Scout", () => Run(r => r.SelectPreset(_engine!, ConfigurableDataSchemaIds.ScoutPresetId))),
                                Button("Tank", () => Run(r => r.SelectPreset(_engine!, ConfigurableDataSchemaIds.TankPresetId))),
                                Button("Source", () => Run(r => r.CycleSourceMode(_engine!))),
                                Button("Path", () => Run(r => r.CycleBindingFocus(_engine!))))
                            .Gap(8f),
                        Ui.Row(
                                Button("X -1", () => Run(r => r.NudgePositionX(_engine!, -1f))),
                                Button("X +1", () => Run(r => r.NudgePositionX(_engine!, 1f))),
                                Button("Rarity", () => Run(r => r.CycleRarity(_engine!))))
                            .Gap(8f),
                        Ui.Row(
                                Button("缺名字", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.MissingRequired))),
                                Button("错稀有度", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.UnknownEnum))),
                                Button("修复", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.None))))
                            .Gap(8f),
                        Ui.Button(
                                state.CanExport ? "导出作者资产" : "导出已禁用",
                                _ =>
                                {
                                    if (_engine == null || !state.CanExport)
                                    {
                                        return;
                                    }

                                    Run(r => r.ExportAuthorAssets(_engine));
                                })
                            .Id(ConfigurableDataSchemaIds.ExportButtonElementId)
                            .Padding(12f, 8f)
                            .Radius(6f)
                            .Background(state.CanExport ? "#2F6FED" : "#4A5568")
                            .Color("#FFFFFF"),
                        string.IsNullOrEmpty(state.ExportPath)
                            ? Ui.Text("尚未导出").FontSize(11f).Color("#8FA0B5")
                            : Ui.Text($"导出目录  {state.ExportPath}").FontSize(11f).Color("#8FA0B5").WhiteSpace(UiWhiteSpace.Normal))
                    .Id(ConfigurableDataSchemaIds.WorkbenchRootElementId)
                    .Width(420f)
                    .Padding(16f)
                    .Gap(8f)
                    .Radius(10f)
                    .Background("#121821EE")
                    .Border(1f, ParseColor("#3A465A"))
                    .Absolute(16f, 16f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .ZIndex(42);
    }

    private UiElementBuilder Button(string label, Action action)
    {
        return Ui.Button(label, _ => action())
            .Padding(10f, 7f)
            .Radius(6f)
            .Background("#243041")
            .Color("#F4F7FB");
    }

    private void Run(Action<ConfigurableDataSchemaRuntime> action)
    {
        if (_engine?.GlobalContext.TryGetValue(ConfigurableDataSchemaIds.RuntimeServiceKey, out object? runtimeObj) == true &&
            runtimeObj is ConfigurableDataSchemaRuntime runtime)
        {
            action(runtime);
        }
    }

    private static UiColor ParseColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color '{hex}'.");
        }

        return color;
    }
}
