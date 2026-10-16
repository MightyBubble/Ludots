namespace Ludots.UI.Runtime;

/// <summary>
/// 上次画完后要重画的一个节点：绑定文字或矢量绘制表改过，或者增量排版挪了它的框。
/// <see cref="PreviousRect"/> 是上次画它时的布局框，没挪的节点等于现框。
/// </summary>
public readonly record struct UiPaintDamage(UiNode Node, UiRect PreviousRect);
