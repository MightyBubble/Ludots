using Ludots.UI.Runtime.Events;

namespace Ludots.UI.Runtime.Actions;

public sealed class UiActionContext
{
    public UiActionContext(UiScene scene, UiEvent evt, UiNode targetNode)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Event = evt ?? throw new ArgumentNullException(nameof(evt));
        TargetNode = targetNode ?? throw new ArgumentNullException(nameof(targetNode));
    }

    public UiScene Scene { get; }

    public UiEvent Event { get; }

    public UiNode TargetNode { get; }
}
