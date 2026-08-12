using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Client
{
    public readonly record struct LocalSeatLaunchBinding(
        string SeatId,
        int PlayerId,
        string? ControlSchemeId = null);

    /// <summary>
    /// Client-local seat table (Epic #896). Seats are I/O channels that may possess a participant;
    /// they do not own players. AI participants need no seat.
    /// </summary>
    public sealed class ClientLocalSeatRegistry
    {
        private readonly Dictionary<string, ClientLocalSeat> _seats = new(StringComparer.Ordinal);
        private readonly List<string> _order = new();

        public int Count => _seats.Count;

        public IReadOnlyList<string> SeatIds => _order;

        public void Clear()
        {
            _seats.Clear();
            _order.Clear();
        }

        public void ReplaceAll(IReadOnlyList<ClientLocalSeat> seats)
        {
            Clear();
            if (seats == null)
            {
                throw new ArgumentNullException(nameof(seats));
            }

            for (int i = 0; i < seats.Count; i++)
            {
                Add(seats[i]);
            }
        }

        public void Add(ClientLocalSeat seat)
        {
            if (seat == null)
            {
                throw new ArgumentNullException(nameof(seat));
            }

            if (_seats.ContainsKey(seat.SeatId))
            {
                throw new InvalidOperationException($"Duplicate client local seat id '{seat.SeatId}'.");
            }

            _seats.Add(seat.SeatId, seat);
            _order.Add(seat.SeatId);
        }

        public bool TryGet(string seatId, out ClientLocalSeat seat)
        {
            seat = null!;
            if (string.IsNullOrWhiteSpace(seatId))
            {
                return false;
            }

            return _seats.TryGetValue(seatId.Trim(), out seat!);
        }

        public ClientLocalSeat Require(string seatId)
        {
            if (!TryGet(seatId, out ClientLocalSeat seat))
            {
                throw new InvalidOperationException($"Client local seat '{seatId}' is not registered.");
            }

            return seat;
        }

        public void SetPossession(string seatId, int playerId, Entity repEntity)
        {
            ClientLocalSeat seat = Require(seatId);
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            if (repEntity == Entity.Null)
            {
                throw new ArgumentException("Possessed rep entity is required.", nameof(repEntity));
            }

            seat.PossessedPlayerId = playerId;
            seat.PossessedRep = repEntity;
        }

        public void ClearPossession(string seatId)
        {
            ClientLocalSeat seat = Require(seatId);
            seat.PossessedPlayerId = 0;
            seat.PossessedRep = Entity.Null;
        }

        public void SetPresentBinding(string seatId, PresentBinding? binding)
        {
            Require(seatId).PresentBinding = binding;
        }

        /// <summary>
        /// Cardinality assert for single-seat clients. Fails if seat count is not exactly one or possession is empty.
        /// This is not an "active seat" slot.
        /// </summary>
        public Entity RequireSolePossessedRep()
        {
            if (_order.Count != 1)
            {
                throw new InvalidOperationException(
                    $"RequireSolePossessedRep requires exactly one client local seat (have {_order.Count}).");
            }

            ClientLocalSeat seat = _seats[_order[0]];
            if (!seat.HasPossession)
            {
                throw new InvalidOperationException($"Client local seat '{seat.SeatId}' has no possession.");
            }

            return seat.PossessedRep;
        }

        public bool TryGetSolePossessedRep(out Entity rep)
        {
            rep = Entity.Null;
            if (!TryGetSoleSeat(out ClientLocalSeat seat) || !seat.HasPossession)
            {
                return false;
            }

            rep = seat.PossessedRep;
            return true;
        }

        public bool TryGetSoleSeat(out ClientLocalSeat seat)
        {
            seat = null!;
            if (_order.Count != 1)
            {
                return false;
            }

            seat = _seats[_order[0]];
            return true;
        }

        public int CopyPossessedReps(Span<Entity> destination)
        {
            int written = 0;
            for (int i = 0; i < _order.Count && written < destination.Length; i++)
            {
                ClientLocalSeat seat = _seats[_order[i]];
                if (!seat.HasPossession)
                {
                    continue;
                }

                destination[written++] = seat.PossessedRep;
            }

            return written;
        }
    }

    public sealed class ClientLocalSeat
    {
        public ClientLocalSeat(string seatId, string? controlSchemeId = null)
        {
            if (string.IsNullOrWhiteSpace(seatId))
            {
                throw new ArgumentException("Seat id is required.", nameof(seatId));
            }

            SeatId = seatId.Trim();
            ControlSchemeId = string.IsNullOrWhiteSpace(controlSchemeId) ? null : controlSchemeId.Trim();
        }

        public string SeatId { get; }
        public string? ControlSchemeId { get; set; }
        public int PossessedPlayerId { get; set; }
        public Entity PossessedRep { get; set; }
        public PresentBinding? PresentBinding { get; set; }

        public bool HasPossession => PossessedPlayerId > 0 && PossessedRep != Entity.Null;
    }
}
