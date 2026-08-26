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
        string validationColor = state.IsValid && state.CanSaveToMod ? "#7CFC9A" : "#FF8A80";
        return Ui.Column(
                Ui.Panel(
                        Ui.Text("数据结构作者工作台").FontSize(20f).Bold().Color("#F4F7FB"),
                        Ui.Text(state.Guide).FontSize(12f).Color("#B7C3D4").WhiteSpace(UiWhiteSpace.Normal),
                        LayerTabs(state),
                        LayerBody(state),
                        Ui.Text($"预览  schema={state.SchemaId}  record={state.WorkbenchRecordId}")
                            .FontSize(12f)
                            .Color("#E8EEF7"),
                        Ui.Text($"绑定  {state.SelectedPinName} → {state.SelectedBindingPath} · 值 {state.BindingValueText} ({state.BindingTypeText})")
                            .FontSize(12f)
                            .Color("#E8EEF7")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text($"Enum  {state.RarityName}/{state.RarityValue} · Tags {state.TagCount} · Source {state.SourceMode}")
                            .FontSize(12f)
                            .Color("#E8EEF7"),
                        Ui.Text(string.IsNullOrEmpty(state.AuthoringError)
                                ? (state.IsValid ? "校验  通过" : $"校验  失败 ×{state.ErrorCount}")
                                : $"校验  {state.AuthoringError}")
                            .FontSize(13f)
                            .Bold()
                            .Color(validationColor)
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.AuthoringStatus).FontSize(12f).Color("#D7E2F0").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Status).FontSize(11f).Color("#9AA8BC").WhiteSpace(UiWhiteSpace.Normal),
                        DemoRow(state),
                        Ui.Button(
                                state.CanSaveToMod ? "保存到目标 Mod" : "保存已禁用",
                                _ =>
                                {
                                    if (_engine == null || !state.CanSaveToMod)
                                    {
                                        return;
                                    }

                                    Run(r => r.SaveAuthoringToMod(_engine));
                                })
                            .Id(ConfigurableDataSchemaIds.ExportButtonElementId)
                            .Padding(12f, 8f)
                            .Radius(6f)
                            .Background(state.CanSaveToMod ? "#2F6FED" : "#4A5568")
                            .Color("#FFFFFF"),
                        Ui.Button(
                                state.CanExport ? "导出验收副本" : "导出副本已禁用",
                                _ =>
                                {
                                    if (_engine == null || !state.CanExport)
                                    {
                                        return;
                                    }

                                    Run(r => r.ExportAuthorAssets(_engine));
                                })
                            .Padding(12f, 8f)
                            .Radius(6f)
                            .Background(state.CanExport ? "#355A3A" : "#4A5568")
                            .Color("#FFFFFF"),
                        string.IsNullOrEmpty(state.SaveTargetRoot)
                            ? Ui.Text("尚未绑定保存目标").FontSize(11f).Color("#8FA0B5")
                            : Ui.Text($"保存目标  {state.SaveTargetRoot}").FontSize(11f).Color("#8FA0B5").WhiteSpace(UiWhiteSpace.Normal))
                    .Id(ConfigurableDataSchemaIds.WorkbenchRootElementId)
                    .Width(460f)
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

    private UiElementBuilder LayerTabs(ConfigurableDataSchemaSnapshot state)
    {
        return Ui.Row(
                LayerButton("Schema", DataSchemaAuthoringLayer.Schema, state),
                LayerButton("Record", DataSchemaAuthoringLayer.Record, state),
                LayerButton("Binding", DataSchemaAuthoringLayer.Binding, state),
                LayerButton("Preview", DataSchemaAuthoringLayer.Preview, state))
            .Gap(6f);
    }

    private UiElementBuilder LayerButton(string label, DataSchemaAuthoringLayer layer, ConfigurableDataSchemaSnapshot state)
    {
        bool active = state.AuthoringLayer == layer;
        return Ui.Button(label, _ => Run(r => r.SetAuthoringLayer(_engine!, layer)))
            .Padding(10f, 6f)
            .Radius(6f)
            .Background(active ? "#2F6FED" : "#243041")
            .Color("#F4F7FB");
    }

    private UiElementBuilder LayerBody(ConfigurableDataSchemaSnapshot state)
    {
        return state.AuthoringLayer switch
        {
            DataSchemaAuthoringLayer.Schema => SchemaLayer(state),
            DataSchemaAuthoringLayer.Record => RecordLayer(state),
            DataSchemaAuthoringLayer.Binding => BindingLayer(state),
            _ => PreviewLayer(state),
        };
    }

    private UiElementBuilder SchemaLayer(ConfigurableDataSchemaSnapshot state)
    {
        return Ui.Column(
                Ui.Text("Schema Designer").FontSize(14f).Bold().Color("#F4F7FB"),
                Ui.Text($"目标 struct：{state.SchemaId}").FontSize(12f).Color("#E8EEF7"),
                Ui.Text($"新字段  {state.NewFieldName} · {state.NewFieldType} · required={state.NewFieldRequired}")
                    .FontSize(12f)
                    .Color("#E8EEF7"),
                Ui.Row(
                        Button("字段名", () => Run(r => r.AuthoringCycleFieldName(_engine!))),
                        Button("类型", () => Run(r => r.AuthoringCycleFieldType(_engine!))),
                        Button("必填", () => Run(r => r.AuthoringToggleRequired(_engine!))),
                        Button("添加字段", () => Run(r => r.AuthoringAddField(_engine!))))
                    .Gap(6f),
                Button("给 rarity 增加 Epic=9", () => Run(r => r.AuthoringAddEpicEnum(_engine!))))
            .Gap(6f);
    }

    private UiElementBuilder RecordLayer(ConfigurableDataSchemaSnapshot state)
    {
        return Ui.Column(
                Ui.Text("Record Editor").FontSize(14f).Bold().Color("#F4F7FB"),
                Ui.Text(state.AuthoringRecordSummary).FontSize(12f).Color("#E8EEF7"),
                Ui.Row(
                        Button("Scout", () => Run(r => r.AuthoringSelectRecord(_engine!, ConfigurableDataSchemaIds.ScoutPresetId))),
                        Button("Tank", () => Run(r => r.AuthoringSelectRecord(_engine!, ConfigurableDataSchemaIds.TankPresetId))),
                        Button("Workbench", () => Run(r => r.AuthoringSelectRecord(_engine!, ConfigurableDataSchemaIds.WorkbenchRecordId))))
                    .Gap(6f),
                Ui.Row(
                        Button("X -1", () => Run(r => r.AuthoringNudgeX(_engine!, -1))),
                        Button("X +1", () => Run(r => r.AuthoringNudgeX(_engine!, 1))),
                        Button("Rarity", () => Run(r => r.AuthoringCycleRarity(_engine!))))
                    .Gap(6f),
                Ui.Row(
                        Button("+tag", () => Run(r => r.AuthoringAddTag(_engine!))),
                        Button("-tag", () => Run(r => r.AuthoringRemoveTag(_engine!))))
                    .Gap(6f))
            .Gap(6f);
    }

    private UiElementBuilder BindingLayer(ConfigurableDataSchemaSnapshot state)
    {
        var pathButtons = new List<UiElementBuilder>();
        if (_engine?.GlobalContext.TryGetValue(ConfigurableDataSchemaIds.RuntimeServiceKey, out object? runtimeObj) == true &&
            runtimeObj is ConfigurableDataSchemaRuntime runtime)
        {
            foreach (string path in runtime.Authoring.EnumerateBindingPaths(ConfigurableDataSchemaIds.SchemaId))
            {
                string captured = path;
                bool active = string.Equals(path, state.SelectedBindingPath, StringComparison.Ordinal);
                pathButtons.Add(
                    Ui.Button(path, _ => Run(r => r.AuthoringSelectBindingPath(_engine!, captured)))
                        .Padding(8f, 5f)
                        .Radius(5f)
                        .Background(active ? "#2F6FED" : "#243041")
                        .Color("#F4F7FB"));
            }
        }

        return Ui.Column(
                Ui.Text("Panel Binding Editor").FontSize(14f).Bold().Color("#F4F7FB"),
                Ui.Text($"当前 pin：{state.SelectedPinName}").FontSize(12f).Color("#E8EEF7"),
                Ui.Row(
                        Button("pin:x", () => Run(r => r.AuthoringSelectPin(_engine!, "x"))),
                        Button("pin:name", () => Run(r => r.AuthoringSelectPin(_engine!, "name"))),
                        Button("pin:rarity", () => Run(r => r.AuthoringSelectPin(_engine!, "rarity"))),
                        Button("pin:tags", () => Run(r => r.AuthoringSelectPin(_engine!, "tags"))))
                    .Gap(6f),
                Ui.Row(
                        Button("Data source", () => Run(r => r.AuthoringSetPinSource(_engine!, "data"))),
                        Button("Graph source", () => Run(r => r.AuthoringSetPinSource(_engine!, "graph"))))
                    .Gap(6f),
                Ui.Text("路径树（点选，不手写）").FontSize(12f).Color("#B7C3D4"),
                Ui.Column(pathButtons.ToArray()).Gap(4f))
            .Gap(6f);
    }

    private UiElementBuilder PreviewLayer(ConfigurableDataSchemaSnapshot state)
    {
        return Ui.Column(
                Ui.Text("Preview & Diagnostics").FontSize(14f).Bold().Color("#F4F7FB"),
                Ui.Text($"右侧面板：{state.ActivePanelId}").FontSize(12f).Color("#E8EEF7"),
                Ui.Text($"Source mode：{state.SourceMode}（换肤不改数据来源）").FontSize(12f).Color("#E8EEF7"),
                Ui.Text(state.CanSaveToMod ? "可以保存到目标 Mod。" : "存在阻塞错误，保存禁用。")
                    .FontSize(12f)
                    .Color(state.CanSaveToMod ? "#7CFC9A" : "#FF8A80"))
            .Gap(6f);
    }

    private UiElementBuilder DemoRow(ConfigurableDataSchemaSnapshot state)
    {
        return Ui.Column(
                Ui.Text("快速消融").FontSize(12f).Bold().Color("#B7C3D4"),
                Ui.Row(
                        Button("Source", () => Run(r => r.CycleSourceMode(_engine!))),
                        Button("Path", () => Run(r => r.CycleBindingFocus(_engine!))),
                        Button("缺名字", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.MissingRequired))),
                        Button("错稀有度", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.UnknownEnum))),
                        Button("修复", () => Run(r => r.InjectInvalid(_engine!, DataSchemaInvalidCase.None))))
                    .Gap(6f))
            .Gap(4f);
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
