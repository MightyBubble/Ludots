using System;
using System.Collections.Generic;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Client
{
    /// <summary>
    /// 界面控件把动作按下沿注入到座位的输入通道：和设备输入、机器人走同一条汇合层。
    /// 按下保持两次引擎更新再松开，权威快照恰好看到一次按下沿，动作也不会一直按着。
    /// 多座位走各自通道；只有唯一的本地座位才落到全局输入处理器。
    /// </summary>
    public sealed class ClientLocalSeatActionEdges
    {
        public const int ReleaseHoldUpdates = 2;

        private readonly Func<ClientLocalSeatInputRuntime?> _seatInput;
        private readonly Func<ClientLocalSeatRegistry?> _seats;
        private readonly Func<PlayerInputHandler?> _globalHandler;
        private readonly List<PendingRelease> _pendingReleases = new();

        public ClientLocalSeatActionEdges(
            Func<ClientLocalSeatInputRuntime?> seatInput,
            Func<ClientLocalSeatRegistry?> seats,
            Func<PlayerInputHandler?> globalHandler)
        {
            _seatInput = seatInput ?? throw new ArgumentNullException(nameof(seatInput));
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
            _globalHandler = globalHandler ?? throw new ArgumentNullException(nameof(globalHandler));
        }

        public int PendingReleases => _pendingReleases.Count;

        /// <summary>座位没有输入通道、也不是唯一本地座位时返回 null。</summary>
        public PlayerInputHandler? ResolveHandler(string seatId)
        {
            if (string.IsNullOrWhiteSpace(seatId))
            {
                throw new ArgumentException("Action edges attribute to a seat.", nameof(seatId));
            }

            ClientLocalSeatInputRuntime? seatInput = _seatInput();
            if (seatInput != null && seatInput.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel))
            {
                return channel.Handler;
            }

            // Policy twin: TriggerGraphActionBindingSystem (read side) routes multi-seat per channel
            // and sole-seat through the global reader — keep the two in lockstep.
            ClientLocalSeatRegistry? seats = _seats();
            PlayerInputHandler? global = _globalHandler();
            return seats != null && global != null && seats.Count == 1 && seats.SeatIds[0] == seatId ? global : null;
        }

        public void Press(PlayerInputHandler handler, string actionId)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (!handler.HasAction(actionId))
            {
                throw new InvalidOperationException($"Action '{actionId}' is not declared in the input config action catalog.");
            }

            handler.InjectButtonPress(actionId);
            _pendingReleases.Add(new PendingRelease(handler, actionId, ReleaseHoldUpdates));
        }

        public void Update()
        {
            for (int i = _pendingReleases.Count - 1; i >= 0; i--)
            {
                PendingRelease entry = _pendingReleases[i];
                if (entry.RemainingUpdates <= 1)
                {
                    _pendingReleases.RemoveAt(i);
                    entry.Handler.InjectButtonRelease(entry.ActionId);
                }
                else
                {
                    _pendingReleases[i] = entry with { RemainingUpdates = entry.RemainingUpdates - 1 };
                }
            }
        }

        private readonly record struct PendingRelease(PlayerInputHandler Handler, string ActionId, int RemainingUpdates);
    }
}
