using System;
using System.Collections.Generic;
using Ludots.Core.Engine;

namespace Ludots.Core.Persistence
{
    public sealed class ReplayRecorder
    {
        private readonly AuthoritativeFrameStream _frames = new();
        private readonly ReplayArchiveCodec _codec;
        private WorldSaveSnapshot? _checkpoint;

        public ReplayRecorder(ReplayArchiveCodec? codec = null) { _codec = codec ?? new ReplayArchiveCodec(); }
        public bool HasCheckpoint => _checkpoint != null;
        public int FrameCount => _frames.Frames.Count;

        public void SetCheckpoint(WorldSaveSnapshot checkpoint)
        {
            _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
            _frames.Clear();
        }

        public void Record(AuthoritativeFrame frame)
        {
            if (_checkpoint == null) throw new SaveContextException("Replay checkpoint must be set before recording authoritative frames.");
            _frames.Append(frame);
        }

        public ReplayArchive BuildArchive()
        {
            if (_checkpoint == null) throw new SaveContextException("Replay checkpoint is required.");
            return new ReplayArchive(
                new ReplayHeader(
                    ReplayHeader.CurrentSchemaVersion,
                    _checkpoint.Header.ModSetHash,
                    _checkpoint.Header.RegistryFingerprint,
                    _checkpoint.Header.MapId,
                    _checkpoint.Header.Tick,
                    0),
                _checkpoint,
                _frames.Frames).Validate();
        }

        public byte[] Encode() => _codec.Encode(BuildArchive());
    }

    public sealed class ReplayPlayer
    {
        public ReplayArchive Archive { get; }
        public ReplayPlayer(ReplayArchive archive) { Archive = (archive ?? throw new ArgumentNullException(nameof(archive))).Validate(); }

        public void PlayFromCheckpoint(
            GameEngine engine,
            WorldRestoreService restoreService,
            Action<AuthoritativeFrame> applyFrame)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (restoreService == null) throw new ArgumentNullException(nameof(restoreService));
            if (applyFrame == null) throw new ArgumentNullException(nameof(applyFrame));
            SaveContextValidator.Validate(Archive.Checkpoint.Header, engine);
            restoreService.Restore(engine, Archive.Checkpoint);
            for (int i = 0; i < Archive.Frames.Count; i++) applyFrame(Archive.Frames[i]);
        }
    }
}
