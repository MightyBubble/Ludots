using System;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI.Runtime.Actions;

public sealed class UiActionContext
{
	public UiScene Scene { get; }

	public UiEvent Event { get; }

	public UiNode TargetNode { get; }

	public UiActionContext(UiScene scene, UiEvent evt, UiNode targetNode)
	{
		Scene = scene ?? throw new ArgumentNullException("scene");
		Event = evt ?? throw new ArgumentNullException("evt");
		TargetNode = targetNode ?? throw new ArgumentNullException("targetNode");
	}
}
