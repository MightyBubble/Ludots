using System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;

namespace NarrativeFrontendMod.Runtime;

/// <summary>
/// Options list visibility for dialogue: CreatePanel owns lifetime; this only Show/Hide.
/// Shared by NarrativeStoryBridge and flagship NarrativeShowcase publisher.
/// </summary>
public static class NarrativeDialogueChoicePanels
{
    public const string PanelType = "panel.narrative.choices";

    public static void SyncVisibility(GameEngine engine, DialogueRuntime dialogue)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dialogue);

        if (engine.GetService(CoreServiceKeys.PanelActivationApi) is not PanelActivationApi activation)
        {
            throw new InvalidOperationException(
                "Dialogue choice PanelHost requires PanelActivationApi.");
        }

        if (dialogue.TryGetActiveView(out DialogueView view) && view.Choices.Count > 0)
        {
            activation.ShowPanel(PanelType);
        }
        else
        {
            activation.HidePanel(PanelType);
        }
    }

    public static void Hide(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (engine.GetService(CoreServiceKeys.PanelActivationApi) is not PanelActivationApi activation)
        {
            return;
        }

        activation.HidePanel(PanelType);
    }
}
