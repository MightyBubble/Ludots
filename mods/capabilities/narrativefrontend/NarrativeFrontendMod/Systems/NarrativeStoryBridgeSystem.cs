using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using NarrativeFrontendMod.Config;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.Systems
{
    /// <summary>
    /// Built-in wire: DialogueRuntime active view → StoryPresentationProjector → NarrativeFrontendService.
    /// Content mods declare hosts in Frontend/narrative_hosts.json; no per-showcase Runtime C#.
    /// </summary>
    internal sealed class NarrativeStoryBridgeSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeFrontendService _service;
        private readonly NarrativeFrontendHostCatalog _hosts;
        private readonly HashSet<string> _bootstrappedMaps = new(StringComparer.OrdinalIgnoreCase);
        private string _activeOwnerId = string.Empty;
        private string _pushedInputContext = string.Empty;

        public NarrativeStoryBridgeSystem(
            GameEngine engine,
            NarrativeFrontendService service,
            NarrativeFrontendHostCatalog hosts)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_hosts.Hosts.Count == 0)
            {
                return;
            }

            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!_hosts.TryGetForMap(mapId, out NarrativeFrontendHostDefinition host))
            {
                TearDownActiveHost();
                return;
            }

            EnsureInputContext(host);
            EnsureBootstrapDialogue(host, mapId);

            if (_engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            if (!dialogue.TryGetActiveView(out DialogueView view))
            {
                if (!string.IsNullOrWhiteSpace(_activeOwnerId))
                {
                    _service.Clear(_activeOwnerId);
                    _activeOwnerId = string.Empty;
                }

                return;
            }

            StoryPresentationProjector projector = _engine.GetService(CoreServiceKeys.StoryPresentationProjector)
                ?? throw new InvalidOperationException(
                    "Narrative story bridge requires StoryPresentationProjector.");
            PresentationDisplayResolver? display = _engine.GetService(CoreServiceKeys.PresentationDisplayResolver);
            StoryPresentationFrame frame = projector.ProjectDialogue(view);
            NarrativeFrontendPageState storyPage = StoryPresentationFrontendAdapter.ToPage(
                host.OwnerId,
                frame,
                display);

            var surfaces = new List<NarrativeFrontendSurfaceModel>();
            surfaces.Add(BuildPromptSurface(host, dialogue));
            NarrativeFrontendSurfaceModel? variables = BuildVariablesSurface(host);
            if (variables != null)
            {
                surfaces.Add(variables);
            }

            if (storyPage.Surfaces != null)
            {
                for (int i = 0; i < storyPage.Surfaces.Count; i++)
                {
                    surfaces.Add(ApplyChrome(host, storyPage.Surfaces[i]));
                }
            }

            surfaces.RemoveAll(static s => !s.Visible);
            string signature = BuildSignature(view, surfaces.Count);
            _service.Publish(new NarrativeFrontendPageState(
                host.OwnerId,
                signature,
                true,
                host.BackdropHex,
                surfaces));
            _activeOwnerId = host.OwnerId;
        }

        private void EnsureBootstrapDialogue(NarrativeFrontendHostDefinition host, string mapId)
        {
            string startId = host.Bootstrap.StartDialogueId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(startId))
            {
                return;
            }

            if (_bootstrappedMaps.Contains(mapId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            if (dialogue.HasActiveDialogue)
            {
                _bootstrappedMaps.Add(mapId);
                return;
            }

            dialogue.StartDialogue(startId);
            _bootstrappedMaps.Add(mapId);
        }

        private void EnsureInputContext(NarrativeFrontendHostDefinition host)
        {
            string contextId = host.Bootstrap.InputContextId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(contextId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
            {
                return;
            }

            if (!input.HasContext(contextId))
            {
                return;
            }

            if (!string.Equals(_pushedInputContext, contextId, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(_pushedInputContext))
                {
                    input.PopContext(_pushedInputContext);
                }

                input.PushContext(contextId);
                _pushedInputContext = contextId;
            }
        }

        private void TearDownActiveHost()
        {
            if (!string.IsNullOrWhiteSpace(_activeOwnerId))
            {
                _service.Clear(_activeOwnerId);
                _activeOwnerId = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(_pushedInputContext) &&
                _engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
            {
                input.PopContext(_pushedInputContext);
                _pushedInputContext = string.Empty;
            }
        }

        private static NarrativeFrontendSurfaceModel ApplyChrome(
            NarrativeFrontendHostDefinition host,
            NarrativeFrontendSurfaceModel surface)
        {
            NarrativeFrontendSurfaceChromeConfig? chrome = surface.Kind switch
            {
                NarrativeFrontendSurfaceKind.OverlayDialogue => host.OverlayDialogue,
                NarrativeFrontendSurfaceKind.DialogueBubble => host.DialogueBubble,
                NarrativeFrontendSurfaceKind.StandingPortrait => host.StandingPortrait,
                NarrativeFrontendSurfaceKind.SubtitleBubble => host.SubtitleBubble,
                NarrativeFrontendSurfaceKind.ChoiceList => host.ChoiceList,
                NarrativeFrontendSurfaceKind.TransmissionOverlay => host.TransmissionOverlay,
                _ => null
            };
            if (chrome == null)
            {
                return surface;
            }

            string title = surface.Kind == NarrativeFrontendSurfaceKind.ChoiceList &&
                           !string.IsNullOrWhiteSpace(chrome.Title)
                ? chrome.Title
                : surface.Title;

            return surface with
            {
                Title = title,
                Subtitle = string.IsNullOrWhiteSpace(surface.Subtitle) ? chrome.Eyebrow : surface.Subtitle,
                Footer = string.IsNullOrWhiteSpace(surface.Footer) ? chrome.Footer : surface.Footer,
                Anchor = chrome.ResolveAnchor(),
                Width = chrome.Width > 0f ? chrome.Width : surface.Width,
                OffsetX = chrome.OffsetX,
                OffsetY = chrome.OffsetY,
                ZIndex = chrome.ZIndex > 0 ? chrome.ZIndex : surface.ZIndex,
                AccentHex = string.IsNullOrWhiteSpace(surface.AccentHex) ? chrome.AccentHex : surface.AccentHex
            };
        }

        private static NarrativeFrontendSurfaceModel BuildPromptSurface(
            NarrativeFrontendHostDefinition host,
            DialogueRuntime dialogue)
        {
            NarrativeFrontendSurfaceChromeConfig chrome = host.PromptRibbon;
            string body = dialogue.HasActiveDialogue &&
                          dialogue.TryGetActiveView(out DialogueView view) &&
                          view.Choices.Count > 0
                ? host.Hints.ChoicePrompt
                : host.Hints.ExplorePrompt;
            string title = !string.IsNullOrWhiteSpace(host.Hints.PromptTitle)
                ? host.Hints.PromptTitle
                : chrome.Title;
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{host.OwnerId}.PromptRibbon",
                Kind: NarrativeFrontendSurfaceKind.PromptRibbon,
                Anchor: chrome.ResolveAnchor(),
                Title: title,
                Body: body,
                Footer: host.Hints.SkinHint,
                Width: chrome.Width > 0f ? chrome.Width : 920f,
                OffsetX: chrome.OffsetX,
                OffsetY: chrome.OffsetY,
                ZIndex: chrome.ZIndex > 0 ? chrome.ZIndex : 55,
                AccentHex: chrome.AccentHex);
        }

        private NarrativeFrontendSurfaceModel? BuildVariablesSurface(NarrativeFrontendHostDefinition host)
        {
            if (host.Variables == null || host.Variables.Length == 0)
            {
                return null;
            }

            MapVariableStore? variables = _engine.CurrentMapSession?.Variables;
            var items = new List<NarrativeFrontendSurfaceItem>(host.Variables.Length);
            for (int i = 0; i < host.Variables.Length; i++)
            {
                NarrativeFrontendVariableHudConfig row = host.Variables[i];
                string display = variables == null
                    ? "—"
                    : variables.ReadInt(row.VariableId).ToString();
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: row.Label,
                    Value: display,
                    AccentHex: row.AccentHex,
                    Active: !string.Equals(display, "0", StringComparison.Ordinal)));
            }

            NarrativeFrontendSurfaceChromeConfig chrome = host.VariablesPanel;
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{host.OwnerId}.Variables",
                Kind: NarrativeFrontendSurfaceKind.StatusPanel,
                Anchor: chrome.ResolveAnchor(),
                Title: chrome.Title,
                Subtitle: chrome.Eyebrow,
                Footer: chrome.Footer,
                Items: items,
                Width: chrome.Width > 0f ? chrome.Width : 360f,
                OffsetX: chrome.OffsetX,
                OffsetY: chrome.OffsetY,
                ZIndex: chrome.ZIndex > 0 ? chrome.ZIndex : 41,
                AccentHex: chrome.AccentHex);
        }

        private static string BuildSignature(DialogueView view, int surfaceCount) =>
            $"{view.DialogueId}|{view.NodeId}|{view.Choices.Count}|{view.Progress01:0.00}|{surfaceCount}";
    }
}
