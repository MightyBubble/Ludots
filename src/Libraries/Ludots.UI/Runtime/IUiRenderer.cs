namespace Ludots.UI.Runtime;

/// <summary>
/// Platform-agnostic UI scene renderer.
/// The concrete implementation (e.g. Skia, GPU, engine-native) lives
/// in a separate adapter assembly — never in Ludots.UI itself.
/// </summary>
public interface IUiRenderer
{
    /// <summary>
    /// 把渲染器自己的目标画布更新成场景当前的样子，清屏也归它管：画布还留着上一帧时只清掉并重画
    /// <see cref="UiScene.GetPaintDamage"/> 覆盖的区域，否则整张清掉重画；画完调 <see cref="UiScene.AcceptPaint"/>。
    /// </summary>
    void Render(UiScene scene, float width, float height);
}
