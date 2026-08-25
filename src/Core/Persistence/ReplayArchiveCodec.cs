using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ludots.Core.Persistence
{
    public sealed class ReplayArchiveCodec
    {
        private const uint Magic = 0x5254444C;
        private const ushort Version = 1;
        private const int HashLength = 32;
        private const int HeaderSize = 4 + 2 + 2 + 4 + HashLength;

        public byte[] Encode(ReplayArchive archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            archive.Validate();
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new ReplayDto(archive));
            byte[] result = new byte[HeaderSize + payload.Length];
            BinaryWriter writer = new(new MemoryStream(result));
            writer.Write(Magic); writer.Write(Version); writer.Write((ushort)0); writer.Write(payload.Length);
            byte[] hash = SHA256.HashData(payload); hash.CopyTo(result, 12);
            payload.CopyTo(result, HeaderSize);
            return result;
        }

        public ReplayArchive Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < HeaderSize) throw new SaveContextException("Replay container is shorter than its frame header.");
            if (BitConverter.ToUInt32(bytes, 0) != Magic || BitConverter.ToUInt16(bytes, 4) != Version)
                throw new SaveContextException("Replay container magic or format version is invalid.");
            if (BitConverter.ToUInt16(bytes, 6) != 0) throw new SaveContextException("Replay reserved flags are not supported.");
            int length = BitConverter.ToInt32(bytes, 8);
            if (length < 0 || length != bytes.Length - HeaderSize) throw new SaveContextException("Replay payload length is invalid.");
            byte[] payload = bytes.AsSpan(HeaderSize).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), bytes.AsSpan(12, HashLength)))
                throw new SaveContextException("Replay payload hash mismatch.");
            try
            {
                ReplayDto? dto = JsonSerializer.Deserialize<ReplayDto>(payload);
                if (dto == null) throw new SaveContextException("Replay payload is empty.");
                return dto.ToArchive().Validate();
            }
            catch (JsonException ex) { throw new SaveContextException($"Replay payload is invalid: {ex.Message}"); }
        }

        private sealed class ReplayDto
        {
            public ReplayHeader Header { get; set; } = null!;
            public WorldSaveSnapshotDto Checkpoint { get; set; } = null!;
            public List<FrameDto> Frames { get; set; } = new();
            public ReplayDto() { }
            public ReplayDto(ReplayArchive archive) { Header = archive.Header; Checkpoint = new(archive.Checkpoint); foreach (var frame in archive.Frames) Frames.Add(new(frame)); }
            public ReplayArchive ToArchive() { var actions = new List<AuthoritativeFrame>(); foreach (var frame in Frames) actions.Add(frame.ToFrame()); return new ReplayArchive(Header, Checkpoint.ToSnapshot(), actions); }
        }

        private sealed class WorldSaveSnapshotDto
        {
            public SaveContextHeader Header { get; set; } = null!;
            public string Domains { get; set; } = string.Empty;
            public string WorldBytes { get; set; } = string.Empty;
            public WorldSaveSnapshotDto() { }
            public WorldSaveSnapshotDto(WorldSaveSnapshot snapshot) { Header = snapshot.Header; Domains = snapshot.Domains.ToJsonString(); WorldBytes = Convert.ToBase64String(snapshot.WorldBytes); }
            public WorldSaveSnapshot ToSnapshot() { return new WorldSaveSnapshot(Header, System.Text.Json.Nodes.JsonNode.Parse(Domains)!.AsObject(), Convert.FromBase64String(WorldBytes)); }
        }

        private sealed class FrameDto
        {
            public long Sequence { get; set; }
            public int Tick { get; set; }
            public List<ActionDto> Actions { get; set; } = new();
            public FrameDto() { }
            public FrameDto(AuthoritativeFrame frame) { Sequence = frame.Sequence; Tick = frame.Tick; foreach (var action in frame.Actions) Actions.Add(new(action)); }
            public AuthoritativeFrame ToFrame() { var result = new List<AuthoritativeAction>(); foreach (var action in Actions) result.Add(action.ToAction()); return new AuthoritativeFrame(Sequence, Tick, result); }
        }

        private sealed class ActionDto
        {
            public string ActionId { get; set; } = string.Empty;
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public bool IsDown { get; set; }
            public bool Pressed { get; set; }
            public bool Released { get; set; }
            public ActionDto() { }
            public ActionDto(AuthoritativeAction action) { ActionId = action.ActionId; X = action.Value.X; Y = action.Value.Y; Z = action.Value.Z; IsDown = action.IsDown; Pressed = action.Pressed; Released = action.Released; }
            public AuthoritativeAction ToAction() { return new(ActionId, new Vector3(X, Y, Z), IsDown, Pressed, Released); }
        }
    }
}
