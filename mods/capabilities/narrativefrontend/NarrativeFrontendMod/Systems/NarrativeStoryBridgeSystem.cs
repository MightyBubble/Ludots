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
    /// Projects active DialogueView onto NarrativeFrontend when the current map opts in via tag
    /// <c>narrative.frontend.project</c>. Content mods start dialogue via TriggerGraph StartDialogue;
    /// flagship NarrativeShowcase keeps its own publisher and must not also carry this tag.
    /// </summary>
    internal sealed class NarrativeStoryBridgeSystem : ISystem<float>
    {
        public const string ProjectDialogueMapTag = "narrative.frontend.project";
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
            if (!CurrentMapOptsIntoProjection())
            {
                _service.Clear(OwnerId);
                return;
            }

            if (_engine.GetService(CoreServiceKeys.DialogueRuntime) is not DialogueRuntime dialogue)
            {
                throw new InvalidOperationException(
                    $"Map tagged '{ProjectDialogueMapTag}' requires DialogueRuntime for narrative frontend projection.");
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
            if (surfaces.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Active dialogue '{view.DialogueId}' projected zero visible surfaces; check presentation profiles.");
            }

            string signature = BuildSignature(view, surfaces.Count);
            _service.Publish(new NarrativeFrontendPageState(
                OwnerId,
                signature,
                true,
                string.Empty,
                surfaces));
        }

        private bool CurrentMapOptsIntoProjection()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null || tags.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], ProjectDialogueMapTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
