namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Entity-side carrier for a child instance's owned subtree. Present only on entities created
    /// from a child reference whose <c>childrenMode</c> is <c>instance</c>; the referenced
    /// definition's shared <see cref="PresenterDefinition.Children"/> stays untouched.
    /// </summary>
    public struct PresenterInstanceChildren
    {
        public ChildPresenterRef[] Children;
    }
}
