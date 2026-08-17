using System;
using System.Numerics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Present-side metrics for one <see cref="PresentBinding"/> — not LogicView logical aspect.
    /// </summary>
    public sealed class PresentBindingSurface : IViewController
    {
        public PresentBindingSurface(PresentBinding binding, float fovYDeg)
        {
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            Binding = binding;
            Fov = fovYDeg;
        }

        public PresentBinding Binding { get; }

        public Vector2 Resolution => Binding.PresentResolutionPx;

        public float Fov { get; }

        public float AspectRatio
        {
            get
            {
                Vector2 resolution = Resolution;
                if (resolution.Y <= 0f)
                {
                    throw new InvalidOperationException("PresentBinding resolution height must be positive.");
                }

                return resolution.X / resolution.Y;
            }
        }
    }
}
