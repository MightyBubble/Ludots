namespace Ludots.Core.Presentation.Tween
{
    public interface ITweenSink
    {
        bool TryApply(in TweenTarget target, float value);
    }
}
