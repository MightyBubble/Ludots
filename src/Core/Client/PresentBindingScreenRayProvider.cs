using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Host-surface picking under multiple PresentBindings: a pointer point routes to the binding
    /// whose normalized rect contains it and is answered with that binding's own camera and
    /// binding-local surface metrics — never a merged camera. With a sole binding (or before any
    /// binding exists) it delegates to the host's single-binding provider unchanged.
    /// </summary>
    public sealed class PresentBindingScreenRayProvider : IScreenRayProvider, IPresentationCameraSnapshotScope
    {
        private readonly GameEngine _engine;
        private readonly CoreScreenRayProvider _sole;
        private readonly Func<float>? _presentationAlphaProvider;
        private readonly List<(string SeatId, PresentBinding Binding)> _bindings = new(4);
        private CoreScreenRayProvider? _routed;

        public PresentBindingScreenRayProvider(
            GameEngine engine,
            CoreScreenRayProvider soleProvider,
            Func<float>? presentationAlphaProvider = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _sole = soleProvider ?? throw new ArgumentNullException(nameof(soleProvider));
            _presentationAlphaProvider = presentationAlphaProvider;
        }

        void IPresentationCameraSnapshotScope.BeginPresentationFrame()
        {
            ((IPresentationCameraSnapshotScope)_sole).BeginPresentationFrame();
            if (_routed is IPresentationCameraSnapshotScope routedScope)
            {
                routedScope.BeginPresentationFrame();
            }
        }

        void IPresentationCameraSnapshotScope.EndPresentationFrame()
        {
            ((IPresentationCameraSnapshotScope)_sole).EndPresentationFrame();
            if (_routed is IPresentationCameraSnapshotScope routedScope)
            {
                routedScope.EndPresentationFrame();
            }
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            IViewController? hostView = ResolveHostView();
            if (hostView == null)
            {
                return _sole.GetRay(screenPosition);
            }

            ClientLocalSeatAccess.CopyPresentBindings(_engine, _bindings);
            if (_bindings.Count <= 1)
            {
                return _sole.GetRay(screenPosition);
            }

            Vector2 hostResolution = hostView.Resolution;
            if (hostResolution.X <= 0f || hostResolution.Y <= 0f)
            {
                return _sole.GetRay(screenPosition);
            }

            float nx = screenPosition.X / hostResolution.X;
            float ny = screenPosition.Y / hostResolution.Y;
            int routeIndex = -1;
            for (int i = 0; i < _bindings.Count; i++)
            {
                Vector4 rect = _bindings[i].Binding.NormalizedScreenRect;
                // Half-open containment: a shared edge belongs to the later binding in seat order.
                if (nx >= rect.X && nx < rect.X + rect.Z && ny >= rect.Y && ny < rect.Y + rect.W)
                {
                    routeIndex = i;
                    break;
                }
            }

            if (routeIndex < 0)
            {
                // Point outside every declared rect: fall back to the first binding in seat order.
                routeIndex = 0;
            }

            PresentBinding binding = _bindings[routeIndex].Binding;
            Vector4 routedRect = binding.NormalizedScreenRect;
            var localPoint = new Vector2(
                screenPosition.X - (routedRect.X * hostResolution.X),
                screenPosition.Y - (routedRect.Y * hostResolution.Y));
            CameraManager camera = ClientLocalSeatAccess.RequireLogicViews(_engine).RequireCamera(binding.LogicViewId);
            CoreScreenRayProvider routed = _routed ??= CreateRoutedProvider(camera, hostView);
            routed.Rebind(camera, new PresentBindingSurface(binding, hostView.Fov));
            return routed.GetRay(localPoint);
        }

        private CoreScreenRayProvider CreateRoutedProvider(CameraManager camera, IViewController hostView)
        {
            var routed = new CoreScreenRayProvider(camera, hostView);
            if (_presentationAlphaProvider != null)
            {
                routed.BindPresentationAlphaProvider(_presentationAlphaProvider);
            }

            return routed;
        }

        private IViewController? ResolveHostView()
        {
            return _engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? view)
                ? view
                : null;
        }
    }
}
