using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using DialogueAuthorKitShowcaseMod.Systems;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;

namespace DialogueAuthorKitShowcaseMod.Runtime
{
    internal sealed class DialogueAuthorKitRuntime
    {
        private readonly IModContext _context;
        private readonly DialogueAuthorKitFrontendConfig _frontend;

        internal DialogueAuthorKitRuntime(IModContext context)
        {
            _context = context;
            using var stream = context.GetResource($"{context.ModId}:assets/Frontend/narrative_frontend.json");
            _frontend = DialogueAuthorKitFrontendConfig.Load(stream);
        }

        public Task HandleGameStartAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue("DialogueAuthorKit.SystemsInstalled", out object? installed) &&
                installed is true)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["DialogueAuthorKit.SystemsInstalled"] = true;
            engine.GlobalContext[DialogueAuthorKitIds.RuntimeKey] = this;
            engine.RegisterPresentationSystem(new DialogueAuthorKitPresentationSystem(engine, this));
            return Task.CompletedTask;
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string activeMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            bool active = string.Equals(activeMapId, DialogueAuthorKitIds.MapId, StringComparison.OrdinalIgnoreCase);
            var input = context.Get(CoreServiceKeys.InputHandler);
            if (active)
            {
                if (input != null && input.HasContext(DialogueAuthorKitIds.InputContext))
                {
                    input.PushContext(DialogueAuthorKitIds.InputContext);
                }

                engine.GlobalContext[DialogueAuthorKitIds.ActiveMapKey] = true;
                EnsureDialogueStarted(engine);
                RefreshPanel(engine);
            }
            else
            {
                if (input != null)
                {
                    input.PopContext(DialogueAuthorKitIds.InputContext);
                }

                ClearFrontend(engine);
                engine.GlobalContext[DialogueAuthorKitIds.ActiveMapKey] = false;
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value ?? string.Empty;
            if (!string.Equals(mapId, DialogueAuthorKitIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            var input = context.Get(CoreServiceKeys.InputHandler);
            if (input != null)
            {
                input.PopContext(DialogueAuthorKitIds.InputContext);
            }

            ClearFrontend(engine);
            engine.GlobalContext[DialogueAuthorKitIds.ActiveMapKey] = false;
            engine.GlobalContext[DialogueAuthorKitIds.BootstrappedKey] = false;
            return Task.CompletedTask;
        }

        public Task HandleDialogueChangedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine || !IsActive(engine))
            {
                return Task.CompletedTask;
            }

            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        internal bool IsActive(GameEngine engine) =>
            engine.GlobalContext.TryGetValue(DialogueAuthorKitIds.ActiveMapKey, out object? flag) &&
            flag is true;

        internal void RefreshPanel(GameEngine engine)
        {
            if (!IsActive(engine))
            {
                ClearFrontend(engine);
                return;
            }

            if (engine.GetService(NarrativeFrontendServiceKeys.Service) is not NarrativeFrontendService frontend ||
                engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            EnsureDialogueStarted(engine);
            frontend.Publish(BuildPage(engine, dialogue));
        }

        private void EnsureDialogueStarted(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            if (dialogue.HasActiveDialogue)
            {
                return;
            }

            if (engine.GlobalContext.TryGetValue(DialogueAuthorKitIds.BootstrappedKey, out object? boot) &&
                boot is true)
            {
                return;
            }

            dialogue.StartDialogue(DialogueAuthorKitIds.DialogueId);
            engine.GlobalContext[DialogueAuthorKitIds.BootstrappedKey] = true;
        }

        private NarrativeFrontendPageState BuildPage(GameEngine engine, DialogueRuntime dialogue)
        {
            var surfaces = new List<NarrativeFrontendSurfaceModel>
            {
                BuildPromptSurface(dialogue),
                BuildVariablesSurface(engine)
            };

            if (dialogue.TryGetActiveView(out DialogueView view))
            {
                StoryPresentationProjector projector = engine.GetService(CoreServiceKeys.StoryPresentationProjector)
                    ?? throw new InvalidOperationException(
                        "Dialogue author kit requires StoryPresentationProjector.");
                StoryPresentationFrame frame = projector.ProjectDialogue(view);
                PresentationDisplayResolver? display = engine.GetService(CoreServiceKeys.PresentationDisplayResolver);
                NarrativeFrontendPageState storyPage = StoryPresentationFrontendAdapter.ToPage(
                    _frontend.OwnerId,
                    frame,
                    display);
                if (storyPage.Surfaces != null)
                {
                    for (int i = 0; i < storyPage.Surfaces.Count; i++)
                    {
                        surfaces.Add(ApplyChrome(storyPage.Surfaces[i]));
                    }
                }
            }

            surfaces.RemoveAll(static s => !s.Visible);
            return new NarrativeFrontendPageState(
                _frontend.OwnerId,
                BuildSignature(dialogue, surfaces.Count),
                true,
                _frontend.BackdropHex,
                surfaces);
        }

        private NarrativeFrontendSurfaceModel ApplyChrome(NarrativeFrontendSurfaceModel surface)
        {
            DialogueAuthorKitSurfaceConfig? config = surface.Kind switch
            {
                NarrativeFrontendSurfaceKind.OverlayDialogue => _frontend.OverlayDialogue,
                NarrativeFrontendSurfaceKind.ChoiceList => _frontend.ChoiceList,
                _ => null
            };
            if (config == null)
            {
                return surface;
            }

            string title = surface.Kind == NarrativeFrontendSurfaceKind.ChoiceList &&
                           !string.IsNullOrWhiteSpace(config.Title)
                ? config.Title
                : surface.Title;

            return surface with
            {
                Title = title,
                Subtitle = string.IsNullOrWhiteSpace(surface.Subtitle) ? config.Eyebrow : surface.Subtitle,
                Footer = string.IsNullOrWhiteSpace(surface.Footer) ? config.Footer : surface.Footer,
                Anchor = config.ResolveAnchor(),
                Width = config.Width > 0f ? config.Width : surface.Width,
                OffsetX = config.OffsetX,
                OffsetY = config.OffsetY,
                ZIndex = config.ZIndex > 0 ? config.ZIndex : surface.ZIndex,
                AccentHex = string.IsNullOrWhiteSpace(surface.AccentHex) ? config.AccentHex : surface.AccentHex
            };
        }

        private NarrativeFrontendSurfaceModel BuildPromptSurface(DialogueRuntime dialogue)
        {
            string body = dialogue.HasActiveDialogue &&
                          dialogue.TryGetActiveView(out DialogueView view) &&
                          view.Choices.Count > 0
                ? _frontend.Hints.ChoicePrompt
                : _frontend.Hints.ExplorePrompt;
            string footer = _frontend.Hints.SkinHint;
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{_frontend.OwnerId}.PromptRibbon",
                Kind: NarrativeFrontendSurfaceKind.PromptRibbon,
                Anchor: _frontend.PromptRibbon.ResolveAnchor(),
                Title: string.IsNullOrWhiteSpace(_frontend.Hints.PromptTitle)
                    ? _frontend.PromptRibbon.Title
                    : _frontend.Hints.PromptTitle,
                Body: body,
                Footer: footer,
                Width: _frontend.PromptRibbon.Width,
                OffsetX: _frontend.PromptRibbon.OffsetX,
                OffsetY: _frontend.PromptRibbon.OffsetY,
                ZIndex: _frontend.PromptRibbon.ZIndex,
                AccentHex: _frontend.PromptRibbon.AccentHex);
        }

        private NarrativeFrontendSurfaceModel BuildVariablesSurface(GameEngine engine)
        {
            var items = new List<NarrativeFrontendSurfaceItem>();
            MapVariableStore? variables = engine.CurrentMapSession?.Variables;
            for (int i = 0; i < _frontend.Variables.Length; i++)
            {
                DialogueAuthorKitVariableHudConfig row = _frontend.Variables[i];
                string display = variables == null
                    ? "—"
                    : variables.ReadInt(row.VariableId).ToString();
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: row.Label,
                    Value: display,
                    AccentHex: row.AccentHex,
                    Active: !string.Equals(display, "0", StringComparison.Ordinal)));
            }

            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{_frontend.OwnerId}.Variables",
                Kind: NarrativeFrontendSurfaceKind.StatusPanel,
                Anchor: _frontend.VariablesPanel.ResolveAnchor(),
                Title: _frontend.VariablesPanel.Title,
                Subtitle: _frontend.VariablesPanel.Eyebrow,
                Footer: _frontend.VariablesPanel.Footer,
                Items: items,
                Width: _frontend.VariablesPanel.Width,
                OffsetX: _frontend.VariablesPanel.OffsetX,
                OffsetY: _frontend.VariablesPanel.OffsetY,
                ZIndex: _frontend.VariablesPanel.ZIndex,
                AccentHex: _frontend.VariablesPanel.AccentHex);
        }

        private static string BuildSignature(DialogueRuntime dialogue, int surfaceCount)
        {
            if (!dialogue.TryGetActiveView(out DialogueView view))
            {
                return $"idle|{surfaceCount}";
            }

            return $"{view.DialogueId}|{view.NodeId}|{view.Choices.Count}|{surfaceCount}";
        }

        private void ClearFrontend(GameEngine engine)
        {
            if (engine.GetService(NarrativeFrontendServiceKeys.Service) is NarrativeFrontendService frontend)
            {
                frontend.Clear(_frontend.OwnerId);
            }
        }
    }
}
