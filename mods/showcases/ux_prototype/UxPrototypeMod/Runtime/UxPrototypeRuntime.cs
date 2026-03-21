using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using UxPrototypeMod.UI;

namespace UxPrototypeMod.Runtime;

internal sealed class UxPrototypeRuntime
{
    private readonly UxPrototypeScenarioState _state = new();
    private readonly UxPrototypePanelController _panelController;
    private EntityCommandPanelHandle _commandPanelHandle = EntityCommandPanelHandle.Invalid;
    private Entity _commandPanelTarget = Entity.Null;
    private bool _inputContextActive;
    private bool _renderDebugCaptured;
    private bool _previousDrawTerrain = true;
    private bool _previousDrawPrimitives = true;
    private bool _previousDrawDebugDraw = true;
    private bool _previousDrawSkiaUi = true;

    public UxPrototypeScenarioState State => _state;

    public UxPrototypeRuntime()
    {
        _panelController = new UxPrototypePanelController(_state);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        bool prototypeActive = UxPrototypeIds.IsPrototypeMap(activeMapId);
        var input = context.Get(CoreServiceKeys.InputHandler);
        var viewModeManager = ResolveViewModeManager(engine);

        if (prototypeActive)
        {
            ActivateInputContext(input);
            EnsureDefaultMode(viewModeManager);
            ApplyPrototypeRenderDefaults(engine);
            _state.EnsureInitialized(engine, activeMapId!);
            RefreshPanel(engine);
        }
        else
        {
            ClearPrototypeMode(viewModeManager);
            DeactivateInputContext(input);
            RestoreRenderDebug(engine);
            ClearPanelIfOwned(context);
            ClearCommandPanel(engine);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        var mapId = context.Get(CoreServiceKeys.MapId);
        if (!UxPrototypeIds.IsPrototypeMap(mapId.Value))
        {
            return Task.CompletedTask;
        }

        ClearPrototypeMode(ResolveViewModeManager(engine));
        DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
        RestoreRenderDebug(engine);
        ClearPanelIfOwned(context);
        ClearCommandPanel(engine);
        return Task.CompletedTask;
    }

    public void Update(GameEngine engine, float dt)
    {
        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (!UxPrototypeIds.IsPrototypeMap(activeMapId))
        {
            return;
        }

        _state.Update(engine, dt);
    }

    public void RefreshPanel(GameEngine engine)
    {
        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
        if (!UxPrototypeIds.IsPrototypeMap(activeMapId))
        {
            ClearPanelIfOwned(engine);
            ClearCommandPanel(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.MountOrRefresh(root, engine, ResolveViewModeManager(engine));
        SyncCommandPanel(engine);
    }

    private void ActivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || _inputContextActive)
        {
            return;
        }

        EnsureInputSchema(input);
        input.PushContext("UxPrototype.Controls");
        _inputContextActive = true;
    }

    private void DeactivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || !_inputContextActive)
        {
            return;
        }

        input.PopContext("UxPrototype.Controls");
        _inputContextActive = false;
    }

    private void ClearPanelIfOwned(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.ClearIfOwned(root);
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.ClearIfOwned(root);
    }

    private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
               managerObj is ViewModeManager manager
            ? manager
            : null;
    }

    private static void EnsureDefaultMode(ViewModeManager? viewModeManager)
    {
        if (viewModeManager == null)
        {
            return;
        }

        if (!UxPrototypeIds.IsPrototypeMode(viewModeManager.ActiveMode?.Id))
        {
            viewModeManager.SwitchTo(UxPrototypeIds.PlayModeId);
        }
    }

    private static void ClearPrototypeMode(ViewModeManager? viewModeManager)
    {
        if (viewModeManager != null && UxPrototypeIds.IsPrototypeMode(viewModeManager.ActiveMode?.Id))
        {
            viewModeManager.ClearActiveMode();
        }
    }

    private static void EnsureInputSchema(PlayerInputHandler input)
    {
        if (!input.HasContext("UxPrototype.Controls"))
        {
            throw new InvalidOperationException("Missing input context: UxPrototype.Controls");
        }

        string[] required =
        {
            "Stop",
            "QueueModifier"
        };

        for (int i = 0; i < required.Length; i++)
        {
            if (!input.HasAction(required[i]))
            {
                throw new InvalidOperationException($"Missing input action: {required[i]}");
            }
        }
    }

    private void ApplyPrototypeRenderDefaults(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        if (!_renderDebugCaptured)
        {
            _previousDrawTerrain = renderDebug.DrawTerrain;
            _previousDrawPrimitives = renderDebug.DrawPrimitives;
            _previousDrawDebugDraw = renderDebug.DrawDebugDraw;
            _previousDrawSkiaUi = renderDebug.DrawSkiaUi;
            _renderDebugCaptured = true;
        }

        renderDebug.DrawTerrain = true;
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawDebugDraw = false;
        renderDebug.DrawSkiaUi = true;
    }

    private void RestoreRenderDebug(GameEngine engine)
    {
        if (!_renderDebugCaptured)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawTerrain = _previousDrawTerrain;
            renderDebug.DrawPrimitives = _previousDrawPrimitives;
            renderDebug.DrawDebugDraw = _previousDrawDebugDraw;
            renderDebug.DrawSkiaUi = _previousDrawSkiaUi;
        }

        _renderDebugCaptured = false;
    }

    private void SyncCommandPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.EntityCommandPanelService) is not IEntityCommandPanelService panelService)
        {
            throw new InvalidOperationException("UxPrototypeMod requires EntityCommandPanelService from EntityCommandPanelMod.");
        }

        Entity selected = ResolveSelectedEntity(engine);
        if (selected == Entity.Null)
        {
            if (_commandPanelHandle.IsValid)
            {
                panelService.SetVisible(_commandPanelHandle, visible: false);
            }

            _commandPanelTarget = Entity.Null;
            return;
        }

        if (!_commandPanelHandle.IsValid ||
            !panelService.TryGetState(_commandPanelHandle, out _))
        {
            _commandPanelHandle = panelService.Open(new EntityCommandPanelOpenRequest
            {
                TargetEntity = selected,
                SourceId = UxPrototypeEntityCommandPanelSource.SourceId,
                InstanceKey = "uxprototype.selection",
                Anchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 16f),
                Size = new EntityCommandPanelSize(420f, 280f),
                InitialGroupIndex = 0,
                StartVisible = true
            });
            _commandPanelTarget = selected;
            return;
        }

        if (_commandPanelTarget != selected)
        {
            panelService.RebindTarget(_commandPanelHandle, selected);
            _commandPanelTarget = selected;
        }

        panelService.SetVisible(_commandPanelHandle, visible: true);
    }

    private void ClearCommandPanel(GameEngine engine)
    {
        if (!_commandPanelHandle.IsValid ||
            engine.GetService(CoreServiceKeys.EntityCommandPanelService) is not IEntityCommandPanelService panelService)
        {
            _commandPanelHandle = EntityCommandPanelHandle.Invalid;
            _commandPanelTarget = Entity.Null;
            return;
        }

        panelService.Close(_commandPanelHandle);
        _commandPanelHandle = EntityCommandPanelHandle.Invalid;
        _commandPanelTarget = Entity.Null;
    }

    private static Entity ResolveSelectedEntity(GameEngine engine)
    {
        return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected)
            ? selected
            : Entity.Null;
    }
}
