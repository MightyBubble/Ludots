using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace NarrativeShowcaseMod.UI
{
    internal sealed class NarrativeShowcasePanelController
    {
        private UiScene? _mountedScene;
        private string _signature = string.Empty;

        public void MountOrRefresh(UIRoot root, GameEngine engine)
        {
            var signature = BuildSignature(engine);
            if (ReferenceEquals(root.Scene, _mountedScene) && string.Equals(signature, _signature, StringComparison.Ordinal))
            {
                return;
            }

            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            var scene = new UiScene(textMeasurer, imageSizeProvider);
            int nextId = 1;
            scene.Mount(BuildRoot(engine).Build(scene.Dispatcher, ref nextId));
            root.MountScene(scene);
            root.IsDirty = true;
            _mountedScene = scene;
            _signature = signature;
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (ReferenceEquals(root.Scene, _mountedScene))
            {
                root.ClearScene();
            }

            _mountedScene = null;
            _signature = string.Empty;
        }

        private UiElementBuilder BuildRoot(GameEngine engine)
        {
            var director = engine.GetService(CoreServiceKeys.NarrativeDirector);
            var choices = director?.GetCurrentChoices() ?? Array.Empty<NarrativeDialogueChoiceDefinition>();
            var choiceBuilders = new List<UiElementBuilder>(choices.Count + 1)
            {
                Ui.Text("Choices").FontSize(12f).Bold().Color("#F6C56B")
            };
            if (choices.Count == 0)
            {
                choiceBuilders.Add(Ui.Text("Enter: advance line | Tab: skip cinematic | E: interact with elder or shrine").FontSize(12f).Color("#93A4B8").WhiteSpace(UiWhiteSpace.Normal));
            }
            else
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    choiceBuilders.Add(Ui.Text($"{i + 1}. {choices[i].Text}").FontSize(12f).Color("#F5F7FA").WhiteSpace(UiWhiteSpace.Normal));
                }
            }

            return Ui.Card(
                Ui.Text("Narrative Showcase").FontSize(22f).Bold().Color("#F5F7FA"),
                Ui.Text("Quest + dialogue + cinematic layered on top of ECS movement, GAS combat, trigger callbacks, and virtual cameras.").FontSize(12f).Color("#B8C4D4").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Quest").FontSize(12f).Bold().Color("#F6C56B"),
                Ui.Text(director?.BuildQuestSummary() ?? "NarrativeDirector unavailable").FontSize(12f).Color("#F5F7FA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(director?.BuildObjectiveSummary() ?? string.Empty).FontSize(12f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Dialogue").FontSize(12f).Bold().Color("#F6C56B"),
                Ui.Text(director?.BuildDialogueSummary() ?? "No active dialogue").FontSize(12f).Color("#F5F7FA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Cinematic").FontSize(12f).Bold().Color("#F6C56B"),
                Ui.Text(director?.BuildCinematicSummary() ?? "No active cinematic").FontSize(12f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Variables").FontSize(12f).Bold().Color("#F6C56B"),
                Ui.Text(director?.BuildVariableSummary(NarrativeShowcaseIds.TrustVariableId, NarrativeShowcaseIds.LoreVariableId, NarrativeShowcaseIds.EndingVariableId) ?? string.Empty).FontSize(12f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Column(choiceBuilders.ToArray()).Gap(6f),
                Ui.Text("Combat: select Arcweaver, right-click move, then use Q/W/E/R/Space once the trial beast appears.").FontSize(12f).Color("#93A4B8").WhiteSpace(UiWhiteSpace.Normal)
            ).Width(440f)
             .Padding(16f)
             .Gap(10f)
             .Radius(20f)
             .Background("#0D1520")
             .Absolute(16f, 16f)
             .ZIndex(25);
        }

        private static string BuildSignature(GameEngine engine)
        {
            var director = engine.GetService(CoreServiceKeys.NarrativeDirector);
            return string.Join("||",
                director?.BuildQuestSummary() ?? string.Empty,
                director?.BuildObjectiveSummary() ?? string.Empty,
                director?.BuildDialogueSummary() ?? string.Empty,
                director?.BuildCinematicSummary() ?? string.Empty,
                director?.BuildVariableSummary(NarrativeShowcaseIds.TrustVariableId, NarrativeShowcaseIds.LoreVariableId, NarrativeShowcaseIds.EndingVariableId) ?? string.Empty,
                director?.GetCurrentChoices().Count.ToString() ?? "0");
        }
    }
}
