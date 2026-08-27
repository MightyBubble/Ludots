using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.Systems
{
    /// <summary>
    /// Built-in wire: active DialogueView → StoryPresentationProjector → NarrativeFrontendService.
    /// Content mods start dialogue via TriggerGraph StartDialogue (e.g. MapLoaded); this system only projects.
    /// Input contexts come from game.json startupInputContexts — not from a per-mod host schema.
    /// </summary>
    internal sealed class NarrativeStoryBridgeSystem : ISystem<float>
    {
        private const string OwnerId = "NarrativeFrontend.ActiveDialogue";

        private readonly GameEngine _engine;
        private readonly NarrativeFrontendService _service;

        public NarrativeStoryBridgeSystem(GameEngine engine, NarrativeFrontendService service)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                return;
            }

            if (!dialogue.TryGetActiveView(out DialogueView view))
            {
                _service.Clear(OwnerId);
                return;
            }

            StoryPresentationProjector projector = _engine.GetService(CoreServiceKeys.StoryPresentationProjector)
                ?? throw new InvalidOperationException(
                    "Narrative story bridge requires StoryPresentationProjector.");
            PresentationDisplayResolver? display = _engine.GetService(CoreServiceKeys.PresentationDisplayResolver);
            StoryPresentationFrame frame = projector.ProjectDialogue(view);
            NarrativeFrontendPageState storyPage = StoryPresentationFrontendAdapter.ToPage(
                OwnerId,
                frame,
                display);

            var surfaces = new List<NarrativeFrontendSurfaceModel>();
            if (storyPage.Surfaces != null)
            {
                for (int i = 0; i < storyPage.Surfaces.Count; i++)
                {
                    surfaces.Add(storyPage.Surfaces[i]);
                }
            }

            surfaces.RemoveAll(static s => !s.Visible);
            string signature = BuildSignature(view, surfaces.Count);
            _service.Publish(new NarrativeFrontendPageState(
                OwnerId,
                signature,
                true,
                string.Empty,
                surfaces));
        }

        private static string BuildSignature(DialogueView view, int surfaceCount)
        {
            return string.Concat(
                view.DialogueId,
                "|",
                view.NodeId,
                "|",
                view.Choices.Count.ToString(),
                "|",
                surfaceCount.ToString());
        }
    }
}
