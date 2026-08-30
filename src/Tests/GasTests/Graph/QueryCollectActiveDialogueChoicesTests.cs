using System;
using Arch.Core;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    [TestFixture]
    public sealed class QueryCollectActiveDialogueChoicesTests
    {
        [SetUp]
        public void SetUp()
        {
            DialogueChoiceIdRegistry.ResetForReload();
        }

        [TearDown]
        public void TearDown()
        {
            DialogueChoiceIdRegistry.ResetForReload();
        }

        [Test]
        public void CollectActiveDialogueChoices_WithoutBinder_Throws()
        {
            using World world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            int[] storage = new int[4];
            Assert.Throws<InvalidOperationException>(() =>
            {
                api.CollectActiveDialogueChoices(storage.AsSpan());
            });
        }

        [Test]
        public void CollectActiveDialogueChoices_EmptySession_WritesZero()
        {
            using World world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            api.BindCollectActiveDialogueChoices(_ => 0);
            Span<int> buffer = stackalloc int[4];
            Assert.That(api.CollectActiveDialogueChoices(buffer), Is.EqualTo(0));
        }

        [Test]
        public void DialogueChoiceIdRegistry_RegistersCompositeKeys()
        {
            int first = DialogueChoiceIdRegistry.Register("Dialogue.A", "go");
            int second = DialogueChoiceIdRegistry.Register("Dialogue.B", "go");
            Assert.That(first, Is.Not.EqualTo(DialogueChoiceIdRegistry.InvalidId));
            Assert.That(second, Is.Not.EqualTo(DialogueChoiceIdRegistry.InvalidId));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(DialogueChoiceIdRegistry.TrySplit(first, out string dialogueId, out string choiceId), Is.True);
            Assert.That(dialogueId, Is.EqualTo("Dialogue.A"));
            Assert.That(choiceId, Is.EqualTo("go"));
        }

        [Test]
        public void DialogueDefinitionRegistry_RegistersChoiceIdsOnLoad()
        {
            var registry = new DialogueDefinitionRegistry();
            registry.Register(new DialogueDefinition
            {
                Id = "Dialogue.Test",
                EntryNode = "n1",
                Nodes =
                {
                    new DialogueNodeDefinition
                    {
                        Id = "n1",
                        LineId = "line.a",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition { Id = "yes", LineId = "line.yes" },
                            new DialogueChoiceDefinition { Id = "no", LineId = "line.no" },
                        }
                    }
                }
            });

            Assert.That(DialogueChoiceIdRegistry.GetId("Dialogue.Test", "yes"), Is.GreaterThan(0));
            Assert.That(DialogueChoiceIdRegistry.GetId("Dialogue.Test", "no"), Is.GreaterThan(0));
        }

        [Test]
        public void DialogueDefinitionRegistry_DuplicateChoiceId_FailsClosed()
        {
            var registry = new DialogueDefinitionRegistry();
            Assert.Throws<InvalidOperationException>(() => registry.Register(new DialogueDefinition
            {
                Id = "Dialogue.Dup",
                EntryNode = "n1",
                Nodes =
                {
                    new DialogueNodeDefinition
                    {
                        Id = "n1",
                        LineId = "line.a",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition { Id = "same", LineId = "line.1" },
                        }
                    },
                    new DialogueNodeDefinition
                    {
                        Id = "n2",
                        LineId = "line.b",
                        PresentationProfile = "story.dialogue_overlay",
                        Choices =
                        {
                            new DialogueChoiceDefinition { Id = "same", LineId = "line.2" },
                        }
                    }
                }
            }));
        }
    }
}
