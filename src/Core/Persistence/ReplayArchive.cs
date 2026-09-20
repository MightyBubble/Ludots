using System;
using System.Collections.Generic;

namespace Ludots.Core.Persistence
{
    public sealed record ReplayHeader(
        int SchemaVersion,
        string ModSetHash,
        string RegistryFingerprint,
        string MapId,
        int CheckpointTick,
        long FirstFrameSequence)
    {
        public const int CurrentSchemaVersion = 1;
    }

    public sealed record ReplayArchive(
        ReplayHeader Header,
        WorldSaveSnapshot Checkpoint,
        IReadOnlyList<AuthoritativeFrame> Frames)
    {
        public ReplayArchive Validate()
        {
            if (Header == null || Checkpoint == null || Frames == null)
            {
                throw new SaveContextException("Replay archive header, checkpoint, and frames are required.");
            }

            if (Header.SchemaVersion != ReplayHeader.CurrentSchemaVersion)
            {
                throw new SaveContextException($"Replay schemaVersion mismatch: expected {ReplayHeader.CurrentSchemaVersion}, actual {Header.SchemaVersion}.");
            }

            if (!string.Equals(Header.MapId, Checkpoint.Header.MapId, StringComparison.Ordinal) ||
                !string.Equals(Header.ModSetHash, Checkpoint.Header.ModSetHash, StringComparison.Ordinal) ||
                !string.Equals(Header.RegistryFingerprint, Checkpoint.Header.RegistryFingerprint, StringComparison.Ordinal) ||
                Header.CheckpointTick != Checkpoint.Header.Tick)
            {
                throw new SaveContextException("Replay header does not match its checkpoint context.");
            }

            var stream = new AuthoritativeFrameStream(Header.FirstFrameSequence, Header.CheckpointTick);
            if (Header.FirstFrameSequence != 0)
            {
                throw new SaveContextException("Replay firstFrameSequence must be zero for a checkpoint archive.");
            }

            stream.AppendRange(Frames);
            return this;
        }
    }
}
