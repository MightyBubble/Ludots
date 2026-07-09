using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Camera
{
    public readonly struct PlatformManagedCameraUpdateContext
    {
        public PlatformManagedCameraUpdateContext(
            VirtualCameraDefinition definition,
            CameraState state,
            CameraBehaviorInputState behaviorInput,
            float deltaTimeSeconds,
            bool allowsUserInput)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            State = state ?? throw new ArgumentNullException(nameof(state));
            BehaviorInput = behaviorInput ?? throw new ArgumentNullException(nameof(behaviorInput));
            DeltaTimeSeconds = deltaTimeSeconds;
            AllowsUserInput = allowsUserInput;
        }

        public VirtualCameraDefinition Definition { get; }
        public CameraState State { get; }
        public CameraBehaviorInputState BehaviorInput { get; }
        public float DeltaTimeSeconds { get; }
        public bool AllowsUserInput { get; }
    }

    public interface IPlatformManagedCameraDriver
    {
        void PrimeDefinition(VirtualCameraDefinition definition);
        bool Update(PlatformManagedCameraUpdateContext context);
    }

    public sealed class PlatformManagedCameraDriverRegistry
    {
        private readonly Dictionary<string, IPlatformManagedCameraDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string driverId, IPlatformManagedCameraDriver driver)
        {
            if (string.IsNullOrWhiteSpace(driverId))
            {
                throw new ArgumentException("Driver id is required.", nameof(driverId));
            }

            _drivers[driverId] = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public bool TryGet(string driverId, out IPlatformManagedCameraDriver driver)
        {
            if (!string.IsNullOrWhiteSpace(driverId) &&
                _drivers.TryGetValue(driverId, out driver))
            {
                return true;
            }

            driver = null!;
            return false;
        }

        public IPlatformManagedCameraDriver Get(string driverId)
        {
            if (TryGet(driverId, out var driver))
            {
                return driver;
            }

            throw new InvalidOperationException($"Platform-managed camera driver '{driverId}' is not registered.");
        }
    }
}
