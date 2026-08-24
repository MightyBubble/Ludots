using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Input.Runtime;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Device first-class abstraction: device-to-seat binding, hot-plug routing through the
    /// seat domain (no entity/player surface), and AgentBridge mock devices enumerable via
    /// the same IInputDeviceWatcher contract as host hardware.
    /// </summary>
    [TestFixture]
    public sealed class InputDeviceBindingTests
    {
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
            Assert.That(binding.TryGetSeatForDevice("gamepad-0", out string seatId), Is.True);
            Assert.That(seatId, Is.EqualTo("seat-1"));
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
            Assert.That(binding.TryGetSeatForDevice("gamepad-0", out _), Is.False);
        }

        [Test]
        public void ConnectedDevice_RoutesToSoleSeat()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-1"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));

            Assert.That(binding.GetDevices("seat-1").Count, Is.EqualTo(1));
            Assert.That(binding.TryGetSeatForDevice("gamepad-0", out string seatId), Is.True);
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
            Assert.That(binding.TryGetSeatForDevice("gamepad-0", out _), Is.False);
        }

        [Test]
        public void MultiSeat_DoesNotAutoBind_ExplicitBindingRoutesAndMoves()
        {
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("seat-a"));
            seats.Add(new ClientLocalSeat("seat-b"));
            var binding = new ClientLocalSeatDeviceBinding(seats);

            binding.HandleDeviceChange(new InputDeviceChangeEvent(Gamepad(), InputDeviceChangeKind.Connected));
            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(0));
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(0));

            binding.BindDevice("seat-b", Gamepad());
            Assert.That(binding.TryGetSeatForDevice("gamepad-0", out string firstSeat), Is.True);
            Assert.That(firstSeat, Is.EqualTo("seat-b"));
            Assert.That(binding.GetDevices("seat-b")[0].SeatSlot, Is.EqualTo(1));

            binding.BindDevice("seat-a", Gamepad());
            Assert.That(binding.GetDevices("seat-b").Count, Is.EqualTo(0));
            Assert.That(binding.GetDevices("seat-a").Count, Is.EqualTo(1));
            Assert.That(binding.GetDevices("seat-a")[0].SeatSlot, Is.EqualTo(0));
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
    }
}
