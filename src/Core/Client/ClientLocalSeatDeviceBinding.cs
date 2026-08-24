using System;
using System.Collections.Generic;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Device-to-seat ownership for the client-local seat table (term governance: devices
    /// belong to seats; nothing below the seat may address a device directly). Hot-plug
    /// routing: a Connected device joins the sole seat when exactly one seat exists —
    /// matching the single-seat cardinality the engine enforces today — while multi-seat
    /// clients bind explicitly via BindDevice.
    /// </summary>
    public sealed class ClientLocalSeatDeviceBinding
    {
        private readonly ClientLocalSeatRegistry _seats;
        private readonly List<Binding> _bindings = new();

        private readonly struct Binding
        {
            public Binding(string seatId, InputDeviceDescriptor device)
            {
                SeatId = seatId;
                Device = device;
            }

            public string SeatId { get; }
            public InputDeviceDescriptor Device { get; }
        }

        public ClientLocalSeatDeviceBinding(ClientLocalSeatRegistry seats)
        {
            ArgumentNullException.ThrowIfNull(seats);
            _seats = seats;
        }

        public void BindDevice(string seatId, InputDeviceDescriptor device)
        {
            ClientLocalSeat seat = _seats.Require(seatId);
            if (string.IsNullOrWhiteSpace(device.DeviceId))
            {
                throw new ArgumentException("Device id is required.", nameof(device));
            }

            RemoveBinding(device.DeviceId);
            _bindings.Add(new Binding(seat.SeatId, device));
        }

        public void UnbindDevice(string deviceId)
        {
            RemoveBinding(deviceId);
        }

        public IReadOnlyList<InputDeviceDescriptor> GetDevices(string seatId)
        {
            ClientLocalSeat seat = _seats.Require(seatId);
            int seatSlot = SeatSlotOf(seat.SeatId);
            List<InputDeviceDescriptor> devices = new();
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (string.Equals(_bindings[i].SeatId, seat.SeatId, StringComparison.Ordinal))
                {
                    devices.Add(_bindings[i].Device with { SeatSlot = seatSlot });
                }
            }

            return devices;
        }

        public bool TryGetSeatForDevice(string deviceId, out string seatId)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    seatId = _bindings[i].SeatId;
                    return true;
                }
            }

            seatId = string.Empty;
            return false;
        }

        public void HandleDeviceChange(InputDeviceChangeEvent change)
        {
            if (change.Kind == InputDeviceChangeKind.Disconnected)
            {
                RemoveBinding(change.Device.DeviceId);
                return;
            }

            if (TryGetSeatForDevice(change.Device.DeviceId, out _) ||
                !_seats.TryGetSoleSeat(out ClientLocalSeat soleSeat))
            {
                return;
            }

            _bindings.Add(new Binding(soleSeat.SeatId, change.Device));
        }

        private void RemoveBinding(string deviceId)
        {
            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    _bindings.RemoveAt(i);
                }
            }
        }

        private int SeatSlotOf(string seatId)
        {
            IReadOnlyList<string> order = _seats.SeatIds;
            for (int i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], seatId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
