using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Scripting;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;

namespace NarrativeSlicesMod.Runtime
{
    /// <summary>
    /// Feeds NarrativeDirector dialogue/cinematic views into the NarrativeFrontend presenter
    /// chain: dialogues render as an overlay with choice items, cinematic steps as a subtitle
    /// surface.
    /// </summary>
    internal sealed class NarrativeSlicesPanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;

        public NarrativeSlicesPanelPresentationSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_engine.GetService(NarrativeFrontendServiceKeys.Service) is not NarrativeFrontendService frontend ||
                _engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.Equals(mapId, NarrativeSlicesIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                frontend.Clear(NarrativeSlicesIds.FrontendOwnerId);
                return;
            }

            if (director.TryGetActiveCinematicView(out NarrativeCinematicView cinematic))
            {
                frontend.Publish(new NarrativeFrontendPageState(
                    NarrativeSlicesIds.FrontendOwnerId,
                    $"cine:{cinematic.CinematicId}:{cinematic.StepId}",
                    Visible: true,
                    Surfaces: new[]
                    {
                        new NarrativeFrontendSurfaceModel(
                            "slices.subtitle",
                            NarrativeFrontendSurfaceKind.SubtitleBubble,
                            NarrativeFrontendAnchor.BottomCenter,
                            cinematic.SpeakerName,
                            Body: cinematic.BodyText,
                            Width: 900f),
                    }));
                return;
            }

            if (director.TryGetActiveDialogueView(out NarrativeDialogueView dialogue))
            {
                var items = new List<NarrativeFrontendSurfaceItem>(dialogue.Choices.Count);
                for (int i = 0; i < dialogue.Choices.Count; i++)
                {
                    items.Add(new NarrativeFrontendSurfaceItem(
                        dialogue.Choices[i].Text,
                        Shortcut: $"[{i + 1}]",
                        Active: true));
                }

                frontend.Publish(new NarrativeFrontendPageState(
                    NarrativeSlicesIds.FrontendOwnerId,
                    $"dlg:{dialogue.DialogueId}:{dialogue.NodeId}:{dialogue.BodyText.Length}",
                    Visible: true,
                    Surfaces: new[]
                    {
                        new NarrativeFrontendSurfaceModel(
                            "slices.dialogue",
                            NarrativeFrontendSurfaceKind.OverlayDialogue,
                            NarrativeFrontendAnchor.BottomCenter,
                            dialogue.SpeakerName,
                            Body: dialogue.BodyText,
                            Items: items,
                            Width: 900f),
                    }));
                return;
            }

            frontend.Clear(NarrativeSlicesIds.FrontendOwnerId);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
