using System;
using System.Collections.Generic;
using Ludots.Core.Engine;

namespace Ludots.Core.Persistence
{
    public sealed class CheckpointCoordinator
    {
        private readonly WorldSnapshotService _snapshots;
        private readonly List<WorldSaveSnapshot> _checkpoints = new();
        private bool _requested;

        public CheckpointCoordinator(WorldSnapshotService? snapshots = null) { _snapshots = snapshots ?? new WorldSnapshotService(); }
        public IReadOnlyList<WorldSaveSnapshot> Checkpoints => _checkpoints;

        public void RequestCheckpoint()
        {
            _requested = true;
        }

        internal bool TryCaptureFromCompletedTick(GameEngine engine, out WorldSaveSnapshot? snapshot)
        {
            snapshot = null;
            if (!_requested) return false;
            _requested = false;
            snapshot = CaptureFromCompletedTick(engine);
            return true;
        }

        internal WorldSaveSnapshot CaptureFromCompletedTick(GameEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            WorldSaveSnapshot snapshot = _snapshots.Capture(engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
            _checkpoints.Add(snapshot);
            return snapshot;
        }
    }
}
