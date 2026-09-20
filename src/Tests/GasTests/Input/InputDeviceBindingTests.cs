using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Diagnostics;
using Ludots.Core.Input.Runtime;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Device first-class abstraction: device-to-seat binding with device → bound-seat-set
    /// fan-out (one device may legally serve several seats; the engine never arbitrates a
    /// duplicate down to a single owner), hot-plug routing through the seat domain, and
    /// AgentBridge mock devices enumerable via the same IInputDeviceWatcher contract as host
    /// hardware.
    /// </summary>
    [TestFixture]
    public sealed class InputDeviceBindingTests
    {
        private RecordingLogBackend _log = null!;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLogBackend();
            Log.Initialize(_log);
        }

        [TearDown]
        public void TearDown()
        {
            Log.Initialize(NullLogBackend.Instance);
            _log.Dispose();
        }

        private static InputDeviceDescriptor Gamepad(int index = 0, string name = "Xbox Controller") =>
            new($"gamepad-{index}", InputDeviceKind.Gamepad, name, -1);

        [Test]
        public void BindDevice_StoresDeviceOnSeatWithSeatSlot()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.BindDevice("seat-1", Gamepad());

            IReadOnlyList<InputDeviceDescriptor> devices = binding.GetDevices("seat-1");
            Assert.That(devices.Count, Is.EqualTo(1));
            Assert.That(devices[0].DeviceId, Is.EqualTo("gamepad-0"));
            Assert.That(devices[0].Kind, Is.EqualTo(InputDeviceKind.Gamepad));
            Assert.That(devices[0].SeatSlot, Is.EqualTo(0));
            Assert.That(binding.TryGetSoleSeatForDevice("gamepad-0", out string seatId), Is.True);
            Assert.That(seatId, Is.EqualTo("seat-1"));
            Assert.That(_log.Warnings.Count, Is.EqualTo(0), "a sole owner is not a duplicate binding.");
        }

        [Test]
        public void BindDevice_UnknownSeat_FailsFast()
        {
            var binding = new ClientLocalSeatDeviceBinding(new ClientLocalSeatRegistry());

            Assert.Throws<InvalidOperationException>(() => binding.BindDevice("ghost-seat", Gamepad()));
            Assert.Throws<InvalidOperationException>(() => binding.GetDevices("ghost-seat"));
        }

        [Test]
        public void UnbindDevice_RemovesBinding()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);
            binding.BindDevice("seat-1", Gamepad());

            binding.UnbindDevice("gamepad-0");

            Assert.That(binding.GetDevices("seat-1").Count, Is.EqualTo(0));
            Assert.That(binding.IsDeviceBound("gamepad-0"), Is.False);
        }

        [Test]
        public void ConnectedDevice_RoutesToSoleSeat()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));

            Assert.That(binding.GetDevices("seat-1").Count, Is.EqualTo(1));
            Assert.That(binding.TryGetSoleSeatForDevice("gamepad-0", out string seatId), Is.True);
            Assert.That(seatId, Is.EqualTo("seat-1"));
        }

        [Test]
        public void DisconnectedDevice_IsRemovedFromItsSeat()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);
            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Disconnected));

            Assert.That(binding.GetDevices("seat-1").Count, Is.EqualTo(0));
            Assert.That(binding.IsDeviceBound("gamepad-0"), Is.False);
        }

        [Test]
        public void MultiSeat_DoesNotAutoBind_ExplicitBindingRoutes()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-a"));
            seats.Add(new ClientLocalSeat("seat-b"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));
            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(0));
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(0));

            binding.BindDevice("seat-b", Gamepad());
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(1));
            Assert.That(binding.GetDevices("seat-b")[0].SeatSlot, Is.EqualTo(1));
        }

        [Test]
        public void SameDeviceBoundToMultipleSeats_FansOutWithOneShotWarning()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-a"));
            seats.Add(new ClientLocalSeat("seat-b"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.BindDevice("seat-b", Gamepad());
            Assert.That(_log.Warnings.Count, Is.EqualTo(0), "the first binding owns the device alone.");

            binding.BindDevice("seat-a", Gamepad());

            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(1), "duplicate binding fans out: both seats keep the device.");
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(1), "the previous owner is not silently evicted.");
            Assert.That(binding.TryGetSoleSeatForDevice("gamepad-0", out _), Is.False,
                "a multi-seat device has no sole owner; consumers must use the fan-out set.");
            var fanOut = new List<string>();
            binding.CopySeatIdsForDevice("gamepad-0", fanOut);
            Assert.That(fanOut, Is.EqualTo(new[] { "seat-a", "seat-b" }).AsCollection,
                "the fan-out set follows seat-table order.");

            Assert.That(_log.Warnings.Count, Is.EqualTo(1), "the duplicate binding warns exactly once at the binding point.");
            Assert.That(_log.Warnings[0], Does.Contain("gamepad-0"));
            Assert.That(_log.Warnings[0], Does.Contain("seat-a"));
            Assert.That(_log.Warnings[0], Does.Contain("seat-b"));

            binding.BindDevice("seat-a", Gamepad());
            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(1), "rebinding the same seat does not duplicate entries.");
            Assert.That(_log.Warnings.Count, Is.EqualTo(1), "a same-seat rebind is not a new duplicate binding.");
        }

        [Test]
        public void DisconnectedDevice_LeavesEveryBoundSeat()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-a"));
            seats.Add(new ClientLocalSeat("seat-b"));
            var binding = new ClientLocalSeatDeviceBinding(seats);
            binding.BindDevice("seat-a", Gamepad());
            binding.BindDevice("seat-b", Gamepad());

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Disconnected));

            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(0));
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(0));
        }

        [Test]
        public void SyntheticInputDevice_IsEnumerableAsWatcherAndBindable()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            IInputDeviceWatcher watcher = new SyntheticInputDevice().WatchAsDeviceWatcher();
            IReadOnlyList<InputDeviceDescriptor> devices = watcher.GetConnectedDevices();

            Assert.That(devices.Count, Is.EqualTo(2));
            Assert.That(devices[0].DeviceId, Is.EqualTo("synthetic-keyboard"));
            Assert.That(devices[0].Kind, Is.EqualTo(InputDeviceKind.Keyboard));
            Assert.That(devices[1].DeviceId, Is.EqualTo("synthetic-mouse"));
            Assert.That(devices[1].Kind, Is.EqualTo(InputDeviceKind.Mouse));
            Assert.That(devices[0].SeatSlot, Is.EqualTo(-1));

            foreach (InputDeviceDescriptor device in devices)
            {
                binding.BindDevice("seat-1", device);
            }

            Assert.That(binding.GetDevices("seat-1").Count, Is.EqualTo(2));
        }

        [Test]
        public void DeviceChanges_NeverTouchSeatPossession()
        {
            World world = World.Create();
            try
            {
                Entity rep = world.Create();
                var seats = new ClientLocalSeatRegistry();
                seats.Add(new ClientLocalSeat("seat-1") { PossessedPlayerId = 7, PossessedRep = rep });
                var binding = new ClientLocalSeatDeviceBinding(seats);

                binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));
                binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Disconnected));

                ClientLocalSeat seat = seats.Require("seat-1");
                Assert.That(seat.PossessedPlayerId, Is.EqualTo(7));
                Assert.That(seat.PossessedRep, Is.EqualTo(rep));
                Assert.That(seats.PresentBindingCount, Is.EqualTo(0));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        private sealed class RecordingLogBackend : ILogBackend
        {
            public readonly List<string> Warnings = new();

            public void Write(LogLevel level, in LogChannel channel, string message)
            {
                if (level == LogLevel.Warning)
                {
                    Warnings.Add(message);
                }
            }

            public void Flush() { }
            public void Dispose() { }
        }
    }
}
