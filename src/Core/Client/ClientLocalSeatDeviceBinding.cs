using System;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Device-to-seat bindings for the client-local seat table (term governance: devices
    /// belong to seats; nothing below the seat may address a device directly). Distribution is
    /// device → bound-seat-set fan-out: one physical device may legally serve several seats
    /// (co-piloting, assist play, mirror acting) and its input reaches every bound seat — the
    /// engine never arbitrates the duplicate down to a single owner, never rewrites mappings,
    /// and never refuses to run. The first binding that overlaps another seat's device emits a
    /// one-shot warning naming the device and the seats involved; the author owns the
    /// consequences. Hot-plug routing: a Connected device joins the sole seat when exactly one
    /// seat exists, while multi-seat clients bind explicitly via BindDevice.
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

            if (IsDeviceBoundTo(seat.SeatId, device.DeviceId))
            {
                return;
            }

            var owners = new List<string>();
            CopySeatIdsForDevice(device.DeviceId, owners);
            if (owners.Count > 0)
            {
                owners.Add(seat.SeatId);
                Log.Warn(
                    in LogChannels.Input,
                    $"Input device '{device.DeviceId}' is now bound to multiple seats [{string.Join(", ", owners)}]; " +
                    "its input fans out to every bound seat and the engine does not arbitrate the overlap.");
            }

            _bindings.Add(new Binding(seat.SeatId, device));
        }

        /// <summary>Hot-unplug / explicit teardown: the device leaves every seat it was bound to.</summary>
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

        public bool IsDeviceBound(string deviceId)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every seat bound to the device, in seat-table order — the fan-out set a device's
        /// input must reach.
        /// </summary>
        public void CopySeatIdsForDevice(string deviceId, List<string> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            IReadOnlyList<string> order = _seats.SeatIds;
            for (int o = 0; o < order.Count; o++)
            {
                for (int i = 0; i < _bindings.Count; i++)
                {
                    if (string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal) &&
                        string.Equals(_bindings[i].SeatId, order[o], StringComparison.Ordinal))
                    {
                        destination.Add(order[o]);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Cardinality assert in the spirit of <c>RequireSolePossessedRep</c>: true only when
        /// exactly one seat owns the device. Devices with a multi-seat fan-out set must be
        /// consumed through <see cref="CopySeatIdsForDevice"/> instead.
        /// </summary>
        public bool TryGetSoleSeatForDevice(string deviceId, out string seatId)
        {
            seatId = string.Empty;
            string? found = null;
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (!string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    seatId = string.Empty;
                    return false;
                }

                found = _bindings[i].SeatId;
            }

            if (found == null)
            {
                return false;
            }

            seatId = found;
            return true;
        }

        public void HandleDeviceChange(InputDeviceChangeEvent change)
        {
            if (change.Kind == InputDeviceChangeKind.Disconnected)
            {
                RemoveBinding(change.Device.DeviceId);
                return;
            }

            if (IsDeviceBound(change.Device.DeviceId) ||
                !_seats.TryGetSoleSeat(out ClientLocalSeat soleSeat))
            {
                return;
            }

            _bindings.Add(new Binding(soleSeat.SeatId, change.Device));
        }

        private bool IsDeviceBoundTo(string seatId, string deviceId)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (string.Equals(_bindings[i].SeatId, seatId, StringComparison.Ordinal) &&
                    string.Equals(_bindings[i].Device.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
