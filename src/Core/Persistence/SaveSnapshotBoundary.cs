using Ludots.Core.Engine;

namespace Ludots.Core.Persistence
{
    public readonly struct SaveSnapshotBoundary
    {
        private readonly SystemGroup _phase;
        private readonly bool _clean;

        private SaveSnapshotBoundary(SystemGroup phase, bool clean)
        {
            _phase = phase;
            _clean = clean;
        }

        public static SaveSnapshotBoundary CleanAfter(SystemGroup phase)
        {
            return new SaveSnapshotBoundary(phase, phase == SystemGroup.ClearPresentationFlags);
        }

        public static SaveSnapshotBoundary InProgress(SystemGroup phase)
        {
            return new SaveSnapshotBoundary(phase, clean: false);
        }

        public void EnsureClean()
        {
            if (!_clean)
            {
                throw new SaveContextException(
                    $"Save snapshot requires a clean tick boundary after {SystemGroup.ClearPresentationFlags}; current phase is {_phase}.");
            }
        }
    }
}
