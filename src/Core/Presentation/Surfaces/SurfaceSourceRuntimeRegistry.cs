using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Requests;

namespace Ludots.Core.Presentation.Surfaces
{
    public sealed class SurfaceSourceRecord
    {
        public int SourceStableId;
        public int PresenterDefinitionId;
        public int RenderPresenterDefinitionId;
        public int ScopeId;
        public int RenderScopeId;
        public SurfaceSourceRequest Request;
        public SurfacePayloadSnapshot Payload;
        public int PayloadVersion;
        public int MeshAssetId;
        public Entity Entity;
        public Entity RenderPresenterEntity;
        public bool Dirty = true;
        public bool PendingRemoval;
    }

    public sealed class SurfaceSourceRuntimeRegistry
    {
        private readonly Dictionary<int, SurfaceSourceRecord> _records = new();

        public int CurrentFrame { get; private set; }

        internal int Count => _records.Count;

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
                    PresenterDefinitionId = request.PresenterDefinitionId,
                    ScopeId = request.ScopeId,
                    Request = request,
                    Payload = payload,
                    PayloadVersion = payload.Version,
                    Entity = Entity.Null,
                    Dirty = true,
                };
                _records.Add(request.StableId, record);
                return;
            }

            record.PendingRemoval = false;
            if (record.ScopeId != request.ScopeId ||
                record.PresenterDefinitionId != request.PresenterDefinitionId ||
                record.PayloadVersion != payload.Version)
            {
                record.Dirty = true;
            }

            record.ScopeId = request.ScopeId;
            record.PresenterDefinitionId = request.PresenterDefinitionId;
            record.Request = request;
            record.Payload = payload;
            record.PayloadVersion = payload.Version;
        }

        public void MarkPendingRemoval(int stableId)
        {
            if (stableId <= 0 || !_records.TryGetValue(stableId, out SurfaceSourceRecord? record))
            {
                return;
            }

            record.PendingRemoval = true;
        }

        public bool Remove(int stableId)
        {
            return _records.Remove(stableId);
        }
    }
}
