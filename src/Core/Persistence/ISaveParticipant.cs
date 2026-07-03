using System.Text.Json.Nodes;

namespace Ludots.Core.Persistence
{
    public interface ISaveParticipant
    {
        string DomainKey { get; }
        JsonNode CaptureState();
        void RestoreState(JsonNode state);
    }
}
