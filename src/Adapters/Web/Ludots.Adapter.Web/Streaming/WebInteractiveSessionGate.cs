using System;

namespace Ludots.Adapter.Web.Streaming
{
    internal sealed class WebInteractiveSessionGate
    {
        public const string RejectionReason =
            "This game is already open in another browser tab. Close that tab, then reload this page.";

        private readonly object _gate = new();
        private string? _ownerSessionId;

        public bool TryAcquire(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Web session id cannot be empty.", nameof(sessionId));
            }

            lock (_gate)
            {
                if (_ownerSessionId != null)
                {
                    return false;
                }

                _ownerSessionId = sessionId;
                return true;
            }
        }

        public bool Release(string sessionId)
        {
            lock (_gate)
            {
                if (!string.Equals(_ownerSessionId, sessionId, StringComparison.Ordinal))
                {
                    return false;
                }

                _ownerSessionId = null;
                return true;
            }
        }
    }
}
