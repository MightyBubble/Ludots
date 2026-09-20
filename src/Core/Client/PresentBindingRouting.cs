using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Window-point → PresentBinding index routing shared by host-surface picking
    /// (<see cref="PresentBindingScreenRayProvider"/>) and the AgentBridge seat tools. Rect
    /// membership is half-open: a shared edge belongs to the later binding in seat order.
    /// Contract: a point outside every declared rect routes to the first binding in seat order —
    /// picking never silently drops the point. Callers pass a positive host resolution and a
    /// non-empty, seat-ordered binding list.
    /// </summary>
    public static class PresentBindingRouting
    {
        public static int RouteWindowPoint(
            Vector2 windowPoint,
            Vector2 hostResolution,
            IReadOnlyList<(string SeatId, PresentBinding Binding)> bindings)
        {
            float nx = windowPoint.X / hostResolution.X;
            float ny = windowPoint.Y / hostResolution.Y;
            for (int i = 0; i < bindings.Count; i++)
            {
                Vector4 rect = bindings[i].Binding.NormalizedScreenRect;
                if (nx >= rect.X && nx < rect.X + rect.Z && ny >= rect.Y && ny < rect.Y + rect.W)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
