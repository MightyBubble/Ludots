namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// 一个视口座位当前看见的迷雾 ScopeKey。座位没有、未占有、或没有视野组时返回 false。
    /// </summary>
    public interface ISeatVisionScope
    {
        bool TryGetScopeKeyId(string seatId, out int scopeKeyId);
    }
}
