using System;
using System.Collections.Generic;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Payload value types an event parameter may carry. Phase one covers the types
    /// current payload keys already use; bool / region / team wait on the map
    /// variable type contract and fail closed at parse time until then.
    /// </summary>
    public enum EventParamType : byte
    {
        Entity = 0,
        Int = 1,
        Float = 2,
        String = 3,
    }

    /// <summary>Subscription scope an event declares; dispatch implementations for
    /// entity / global scopes land with their own bridging slices.</summary>
    public enum EventScope : byte
    {
        Map = 0,
        Entity = 1,
        Global = 2,
    }

    public sealed record EventParamSchema(
        string Name,
        EventParamType Type,
        string PayloadKey,
        bool Optional = false);

    public sealed record EventSchema(
        string EventName,
        EventScope Scope,
        IReadOnlyList<EventParamSchema> Params)
    {
        public bool DeclaresPayloadKey(string payloadKey)
        {
            for (int i = 0; i < Params.Count; i++)
            {
                if (string.Equals(Params[i].PayloadKey, payloadKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
