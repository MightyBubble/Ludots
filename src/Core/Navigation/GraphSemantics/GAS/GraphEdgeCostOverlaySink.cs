using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphSemantics.GAS.Components;

namespace Ludots.Core.Navigation.GraphSemantics.GAS
{
    public sealed class GraphEdgeCostOverlaySink : IAttributeSink
    {
        private readonly GraphEdgeCostOverlay _overlay;
        private readonly QueryDescription _query = new QueryDescription().WithAll<AttributeBuffer, GraphEdgeRef>();

        public GraphEdgeCostOverlaySink(GraphEdgeCostOverlay overlay)
        {
            _overlay = overlay ?? throw new System.ArgumentNullException(nameof(overlay));
        }

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            bool resetAdd = false;
            bool resetMul = false;
            bool resetBlocked = false;

            for (int i = 0; i < count; i++)
            {
                var e = entries[start + i];
                if (e.ResetPolicy != AttributeBindingResetPolicy.ResetToZeroPerLogicFrame) continue;
                if (e.Channel == 0) resetAdd = true;
                if (e.Channel == 1) resetMul = true;
                if (e.Channel == 2) resetBlocked = true;
            }

            if (resetAdd && _overlay.CostAdd.Length != 0) System.Array.Clear(_overlay.CostAdd, 0, _overlay.CostAdd.Length);
            if (resetMul && _overlay.CostMul.Length != 0) System.Array.Clear(_overlay.CostMul, 0, _overlay.CostMul.Length);
            if (resetBlocked && _overlay.Blocked.Length != 0) System.Array.Clear(_overlay.Blocked, 0, _overlay.Blocked.Length);

            var job = new ApplyJob
            {
                Entries = entries,
                Start = start,
                Count = count,
                CostAdd = _overlay.CostAdd,
                CostMul = _overlay.CostMul,
                Blocked = _overlay.Blocked
            };

            world.InlineEntityQuery<ApplyJob, AttributeBuffer, GraphEdgeRef>(in _query, ref job);
        }

        private struct ApplyJob : IForEachWithEntity<AttributeBuffer, GraphEdgeRef>
        {
            public AttributeBindingEntry[] Entries;
            public int Start;
            public int Count;
            public float[] CostAdd;
            public float[] CostMul;
            public byte[] Blocked;

            public void Update(Entity entity, ref AttributeBuffer attr, ref GraphEdgeRef edgeRef)
            {
                int edgeId = edgeRef.EdgeId;
                if ((uint)edgeId >= (uint)CostAdd.Length) return;

                for (int i = 0; i < Count; i++)
                {
                    var b = Entries[Start + i];
                    float value = attr.GetCurrent(b.AttributeId) * b.Scale;
                    switch (b.Channel)
                    {
                        case 0:
                            CostAdd[edgeId] = ApplyFloat(CostAdd[edgeId], value, b.Mode);
                            break;
                        case 1:
                            CostMul[edgeId] = ApplyFloat(CostMul[edgeId], value, b.Mode);
                            break;
                        case 2:
                            byte v = value > 0f ? (byte)1 : (byte)0;
                            Blocked[edgeId] = ApplyByte(Blocked[edgeId], v, b.Mode);
                            break;
                    }
                }
            }

            private static float ApplyFloat(float current, float value, AttributeBindingMode mode)
            {
                return mode == AttributeBindingMode.Override ? value : current + value;
            }

            private static byte ApplyByte(byte current, byte value, AttributeBindingMode mode)
            {
                if (mode == AttributeBindingMode.Override) return value;
                return (byte)((current | value) != 0 ? 1 : 0);
            }
        }
    }
}

