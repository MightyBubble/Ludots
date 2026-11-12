namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// 呈现隐藏。CameraCullingSystem 仍独占 <see cref="CullState.IsVisible"/>；
    /// 这个组件在剔除结论之后再挡一次，不改写剔除结果。
    /// </summary>
    public struct PresentationHidden
    {
        public bool Hidden;
    }
}
