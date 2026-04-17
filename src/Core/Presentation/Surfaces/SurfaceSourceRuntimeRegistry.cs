using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Requests;

namespace Ludots.Core.Presentation.Surfaces
{
    public sealed class SurfaceSourceRecord
    {
        public int SourceStableId;
        public int PerformerDefinitionId;
        public int ScopeId;
        public SurfaceSourceRequest Request;
        public SurfacePayloadSnapshot Payload;
        public int PayloadVersion;
        public int LastSeenFrame;
        public int MeshAssetId;
        public Entity Entity;
        public bool Dirty = true;
        public bool PendingRemoval;
    }

    public sealed class SurfaceSourceRuntimeRegistry
    {
        private readonly Dictionary<int, SurfaceSourceRecord> _records = new();

        public int CurrentFrame { get; private set; }

        public IEnumerable<SurfaceSourceRecord> Records => _records.Values;

        public int BeginFrame()
        {
            return ++CurrentFrame;
        }

        public void Upsert(in SurfaceSourceRequest request, in SurfacePayloadSnapshot payload, int frame)
        {
            if (request.StableId <= 0)
            {
                throw new InvalidOperationException("SurfaceSourceRequest requires a positive StableId.");
            }

            if (!_records.TryGetValue(request.StableId, out SurfaceSourceRecord? record))
            {
                record = new SurfaceSourceRecord
                {
                    SourceStableId = request.StableId,
                    PerformerDefinitionId = request.PerformerDefinitionId,
                    ScopeId = request.ScopeId,
                    Request = request,
                    Payload = payload,
                    PayloadVersion = payload.Version,
                    LastSeenFrame = frame,
                    Entity = Entity.Null,
                    Dirty = true,
                };
                _records.Add(request.StableId, record);
                return;
            }

            record.LastSeenFrame = frame;
            record.PendingRemoval = false;
            if (record.ScopeId != request.ScopeId ||
                record.PerformerDefinitionId != request.PerformerDefinitionId ||
                record.PayloadVersion != payload.Version)
            {
                record.Dirty = true;
            }

            record.ScopeId = request.ScopeId;
            record.PerformerDefinitionId = request.PerformerDefinitionId;
            record.Request = request;
            record.Payload = payload;
            record.PayloadVersion = payload.Version;
        }

        public void MarkStaleAsPendingRemoval()
        {
            foreach (SurfaceSourceRecord record in _records.Values)
            {
                if (record.LastSeenFrame != CurrentFrame)
                {
                    record.PendingRemoval = true;
                }
            }
        }

        public bool Remove(int stableId)
        {
            return _records.Remove(stableId);
        }
    }
}
