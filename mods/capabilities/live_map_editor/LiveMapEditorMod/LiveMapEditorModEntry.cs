using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using LiveMapEditorMod.Runtime;
using LiveMapEditorMod.Systems;
using LiveMapEditorMod.UI;

namespace LiveMapEditorMod;

public sealed class LiveMapEditorModEntry : IMod
{
    private readonly LiveMapEditorRuntime _runtime = new();
    private readonly LiveMapEditorPanelController _panelController;
    private bool _systemInstalled;

    public LiveMapEditorModEntry()
    {
        _panelController = new LiveMapEditorPanelController(_runtime);
    }

    public void OnLoad(IModContext context)
    {
        context.Log("[LiveMapEditorMod] Loaded - F4 toggles the in-session map editor panel.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _panelController.Dispose();
        _runtime.Dispose();
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("LiveMapEditorMod requires GameEngine.");
        EnsureInput(engine);
        await _panelController.InitializeAsync(context).ConfigureAwait(false);
        if (!_systemInstalled)
        {
            engine.RegisterPresentationSystem(new LiveMapEditorPresentationSystem(engine, _runtime, _panelController));
            _systemInstalled = true;
        }
    }

    private static void EnsureInput(GameEngine engine)
    {
        PlayerInputHandler input = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("LiveMapEditorMod requires CoreInputMod PlayerInputHandler.");
        if (!input.HasContext(LiveMapEditorIds.InputContext))
        {
            throw new InvalidOperationException($"Missing input context: {LiveMapEditorIds.InputContext}");
        }

        RequireAction(input, LiveMapEditorIds.TogglePanelAction);
        RequireAction(input, LiveMapEditorIds.PrimaryAction);
        RequireAction(input, LiveMapEditorIds.SecondaryAction);
        RequireAction(input, LiveMapEditorIds.PointerAction);
        input.PushContext(LiveMapEditorIds.InputContext);
    }

    private static void RequireAction(PlayerInputHandler input, string actionId)
    {
        if (!input.HasAction(actionId))
        {
            throw new InvalidOperationException($"Missing input action: {actionId}");
        }
    }
}
