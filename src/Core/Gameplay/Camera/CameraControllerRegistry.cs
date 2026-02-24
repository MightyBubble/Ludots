using System;
using System.Collections.Generic;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    public readonly struct CameraControllerBuildServices
    {
        public PlayerInputHandler Input { get; }

        public CameraControllerBuildServices(PlayerInputHandler input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
        }
    }

    public sealed class CameraControllerRegistry
    {
        private readonly Dictionary<string, Func<object?, CameraControllerBuildServices, ICameraController>> _factories = new(StringComparer.Ordinal);

        public void Register(string id, Func<object?, CameraControllerBuildServices, ICameraController> factory)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Camera controller id is required.", nameof(id));
            _factories[id] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public ICameraController Create(CameraControllerRequest request, CameraControllerBuildServices services)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Id)) throw new InvalidOperationException("CameraControllerRequest.Id is required.");

            if (!_factories.TryGetValue(request.Id, out var factory))
            {
                throw new InvalidOperationException($"Camera controller '{request.Id}' is not registered.");
            }

            return factory(request.Config, services);
        }
    }
}

