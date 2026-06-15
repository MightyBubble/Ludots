namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// Adapter-visible generation for external presentation target lifecycle changes.
    /// This stays separate from retained visual content revision.
    /// </summary>
    public sealed class PresentationTargetGeneration
    {
        private int _generation;
        private bool _ready;

        public int Generation => _generation;

        public bool IsReady => _ready;

        public void MarkReady()
        {
            if (_ready)
            {
                return;
            }

            _ready = true;
            Advance();
        }

        public void MarkUnavailable()
        {
            if (!_ready)
            {
                return;
            }

            _ready = false;
            Advance();
        }

        public void MarkTargetChanged()
        {
            Advance();
        }

        public void MarkTargetChanged(bool ready)
        {
            _ready = ready;
            Advance();
        }

        private void Advance()
        {
            _generation = _generation == int.MaxValue ? 1 : _generation + 1;
        }
    }
}
