using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public enum MapVariableType
    {
        Int,
        Float
    }

    /// <summary>
    /// Map JSON <c>Variables[]</c> entry. Strict parsing (unknown fields, empty/duplicate
    /// names, missing/non-integral initials) happens in <see cref="MapVariableDeclarations.Parse"/>
    /// at map-config load and again in <see cref="MapVariableStore.Create"/> for code-built configs.
    /// </summary>
    public sealed class MapVariableDeclaration
    {
        public string Name { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MapVariableType Type { get; set; }

        /// <summary>Declared initial value. Required; for <see cref="MapVariableType.Int"/> it must be integral.</summary>
        public double? Initial { get; set; }
    }

    /// <summary>One variable in a save snapshot; only the field matching <see cref="Type"/> carries a live value.</summary>
    public sealed record MapVariableValueSnapshot(string Name, MapVariableType Type, int IntValue, float FloatValue);

    /// <summary>
    /// Save payload for a <see cref="MapVariableStore"/>. Revisions are not persisted: restore
    /// writes values directly onto the freshly declared slots (revisions stay at zero) and does
    /// not dispatch VariableChanged, so the first post-restore write diffs against the restored value.
    /// </summary>
    public sealed record MapVariableStoreSnapshot(IReadOnlyList<MapVariableValueSnapshot> Variables);

    public static class MapVariableDeclarations
    {
        private const string AllowedFields = "name, type, initial";

        /// <summary>
        /// Strict parse of the optional map JSON <c>Variables</c> array. Rejects unknown fields,
        /// non-array nodes, empty/whitespace names, duplicate (trimmed) names, missing initial,
        /// and non-integral int initials. <paramref name="context"/> names the map in errors.
        /// </summary>
        public static List<MapVariableDeclaration> Parse(JsonNode? variablesNode, string context)
        {
            if (variablesNode == null)
            {
                return new List<MapVariableDeclaration>();
            }

            if (variablesNode is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' Variables must be an array of variable declarations.");
            }

            var declarations = new List<MapVariableDeclaration>(array.Count);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                declarations.Add(ParseItem(array[i], i, context, seenNames));
            }

            return declarations;
        }

        private static MapVariableDeclaration ParseItem(
            JsonNode? item,
            int index,
            string context,
            HashSet<string> seenNames)
        {
            if (item is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' Variables[{index}] must be an object.");
            }

            string? name = null;
            MapVariableType type = MapVariableType.Int;
            bool hasType = false;
            double? initial = null;
            foreach (KeyValuePair<string, JsonNode?> field in obj)
            {
                switch (field.Key)
                {
                    case "name":
                        name = field.Value is JsonValue value && value.TryGetValue<string>(out string? text)
                            ? text
                            : throw new InvalidOperationException(
                                $"Map '{context}' Variables[{index}] field 'name' must be a string.");
                        break;
                    case "type":
                        hasType = true;
                        if (field.Value is not JsonValue typeValue ||
                            !typeValue.TryGetValue<string>(out string? typeText) ||
                            !TryParseType(typeText, out type))
                        {
                            throw new InvalidOperationException(
                                $"Map '{context}' Variables[{index}] field 'type' must be \"int\" or \"float\".");
                        }

                        break;

                    case "initial":
                        if (field.Value is not JsonValue initialValue ||
                            !initialValue.TryGetValue<double>(out double initialNumber))
                        {
                            throw new InvalidOperationException(
                                $"Map '{context}' Variables[{index}] field 'initial' must be a number.");
                        }

                        initial = initialNumber;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Map '{context}' Variables[{index}] has unknown field '{field.Key}'; allowed fields are {AllowedFields}.");
                }
            }

            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' Variables[{index}] requires a non-empty 'name'.");
            }

            if (!seenNames.Add(trimmed))
            {
                throw new InvalidOperationException(
                    $"Map '{context}' declares variable '{trimmed}' more than once.");
            }

            if (!hasType)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' variable '{trimmed}' requires a 'type' (\"int\" or \"float\").");
            }

            if (!initial.HasValue)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' variable '{trimmed}' requires an 'initial' value.");
            }

            if (type == MapVariableType.Int && Math.Floor(initial.Value) != initial.Value)
            {
                throw new InvalidOperationException(
                    $"Map '{context}' int variable '{trimmed}' initial {initial.Value} must be integral.");
            }

            return new MapVariableDeclaration
            {
                Name = trimmed,
                Type = type,
                Initial = initial.Value
            };
        }

        private static bool TryParseType(string text, out MapVariableType type)
        {
            if (string.Equals(text, "int", StringComparison.OrdinalIgnoreCase))
            {
                type = MapVariableType.Int;
                return true;
            }

            if (string.Equals(text, "float", StringComparison.OrdinalIgnoreCase))
            {
                type = MapVariableType.Float;
                return true;
            }

            type = MapVariableType.Int;
            return false;
        }
    }

    /// <summary>
    /// Map-scoped variable table owned by a <see cref="MapSession"/>. One slot per declared
    /// variable; reads and writes of undeclared names fail closed. Every successful write
    /// bumps that variable's revision; a value change fires the map-scoped
    /// <see cref="GameEvents.MapVariableChanged"/> event through the bound dispatcher.
    /// </summary>
    public sealed class MapVariableStore
    {
        public const string PayloadKeyVarName = MapTriggerEventPayloadKeys.VarName;
        public const string PayloadKeyNewValueInt = MapTriggerEventPayloadKeys.VarValueInt;
        public const string PayloadKeyNewValueFloat = MapTriggerEventPayloadKeys.VarValueFloat;
        public const string PayloadKeyOldValueInt = MapTriggerEventPayloadKeys.OldValueInt;
        public const string PayloadKeyOldValueFloat = MapTriggerEventPayloadKeys.OldValueFloat;

        private sealed class Slot
        {
            public required MapVariableType Type;
            public int IntValue;
            public float FloatValue;
            public uint Revision;
        }

        private readonly Dictionary<string, Slot> _slots;

        private MapVariableStore(MapId mapId, Dictionary<string, Slot> slots)
        {
            MapId = mapId;
            _slots = slots;
        }

        /// <summary>
        /// Dispatches a value change (var name + typed old/new pair) for the owning map;
        /// the engine binds this to TriggerManager.FireMapEventAsync with the payload
        /// keys above, skipping the fire entirely while no subscriber is registered.
        /// </summary>
        public MapVariableChangedHandler? VariableChangedDispatcher { get; set; }

        public MapId MapId { get; }

        public int Count => _slots.Count;

        public IEnumerable<string> Names => _slots.Keys;

        public static MapVariableStore Create(MapId mapId, IEnumerable<MapVariableDeclaration>? declarations)
        {
            var slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
            if (declarations != null)
            {
                foreach (MapVariableDeclaration declaration in declarations)
                {
                    string name = (declaration.Name ?? string.Empty).Trim();
                    if (name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId.Value}' variable declaration requires a non-empty name.");
                    }

                    if (slots.ContainsKey(name))
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId.Value}' declares variable '{name}' more than once.");
                    }

                    if (!declaration.Initial.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId.Value}' variable '{name}' requires an initial value.");
                    }

                    double initial = declaration.Initial.Value;
                    if (declaration.Type == MapVariableType.Int)
                    {
                        if (Math.Floor(initial) != initial)
                        {
                            throw new InvalidOperationException(
                                $"Map '{mapId.Value}' int variable '{name}' initial {initial} must be integral.");
                        }

                        if (initial > int.MaxValue || initial < int.MinValue)
                        {
                            throw new InvalidOperationException(
                                $"Map '{mapId.Value}' int variable '{name}' initial {initial} is outside the int range.");
                        }
                    }

                    slots[name] = new Slot
                    {
                        Type = declaration.Type,
                        IntValue = declaration.Type == MapVariableType.Int ? checked((int)initial) : 0,
                        FloatValue = declaration.Type == MapVariableType.Float ? (float)initial : 0f
                    };
                }
            }

            return new MapVariableStore(mapId, slots);
        }

        public bool Contains(string name)
            => _slots.ContainsKey(name ?? string.Empty);

        public int ReadInt(string name)
        {
            Slot slot = RequireSlot(name);
            return slot.Type == MapVariableType.Int
                ? slot.IntValue
                : throw WrongType(name, slot.Type, MapVariableType.Int);
        }

        public float ReadFloat(string name)
        {
            Slot slot = RequireSlot(name);
            return slot.Type == MapVariableType.Float
                ? slot.FloatValue
                : throw WrongType(name, slot.Type, MapVariableType.Float);
        }

        public uint GetRevision(string name) => RequireSlot(name).Revision;

        public MapVariableStoreSnapshot CaptureSnapshot()
        {
            var entries = new List<MapVariableValueSnapshot>(_slots.Count);
            foreach (KeyValuePair<string, Slot> pair in _slots)
            {
                Slot slot = pair.Value;
                entries.Add(new MapVariableValueSnapshot(pair.Key, slot.Type, slot.IntValue, slot.FloatValue));
            }

            entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return new MapVariableStoreSnapshot(entries);
        }

        public void RestoreSnapshot(MapVariableStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var pending = new List<(Slot Slot, MapVariableValueSnapshot Entry)>(snapshot.Variables.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Variables.Count; i++)
            {
                MapVariableValueSnapshot entry = snapshot.Variables[i];
                if (entry == null)
                {
                    throw new InvalidOperationException(
                        $"Map '{MapId.Value}' save variable entry at index {i} is null.");
                }

                if (!seen.Add(entry.Name))
                {
                    throw new InvalidOperationException(
                        $"Map '{MapId.Value}' save contains variable '{entry.Name}' more than once.");
                }

                if (!_slots.TryGetValue(entry.Name, out Slot? slot))
                {
                    throw new InvalidOperationException(
                        $"Map '{MapId.Value}' save contains variable '{entry.Name}' that the map does not declare.");
                }

                if (slot.Type != entry.Type)
                {
                    throw new InvalidOperationException(
                        $"Map '{MapId.Value}' variable '{entry.Name}' is declared as {slot.Type} but the save stores {entry.Type}.");
                }

                pending.Add((slot, entry));
            }

            if (pending.Count != _slots.Count)
            {
                foreach (string declared in _slots.Keys)
                {
                    if (!seen.Contains(declared))
                    {
                        throw new InvalidOperationException(
                            $"Map '{MapId.Value}' save is missing declared variable '{declared}'.");
                    }
                }
            }

            for (int i = 0; i < pending.Count; i++)
            {
                pending[i].Slot.IntValue = pending[i].Entry.IntValue;
                pending[i].Slot.FloatValue = pending[i].Entry.FloatValue;
            }
        }

        public void WriteInt(string name, int value)
        {
            Slot slot = RequireSlot(name);
            if (slot.Type != MapVariableType.Int)
            {
                throw WrongType(name, slot.Type, MapVariableType.Int);
            }

            int oldValue = slot.IntValue;
            bool changed = oldValue != value;
            slot.IntValue = value;
            slot.Revision++;
            if (changed)
            {
                VariableChangedDispatcher?.Invoke(MapId, name, MapVariableType.Int, oldValue, value, 0f, 0f);
            }
        }

        public void WriteFloat(string name, float value)
        {
            Slot slot = RequireSlot(name);
            if (slot.Type != MapVariableType.Float)
            {
                throw WrongType(name, slot.Type, MapVariableType.Float);
            }

            float oldValue = slot.FloatValue;
            bool changed = oldValue != value;
            slot.FloatValue = value;
            slot.Revision++;
            if (changed)
            {
                VariableChangedDispatcher?.Invoke(MapId, name, MapVariableType.Float, 0, 0, oldValue, value);
            }
        }

        private Slot RequireSlot(string name)
        {
            if (name == null || !_slots.TryGetValue(name, out Slot? slot))
            {
                throw new InvalidOperationException(
                    $"Map '{MapId.Value}' has no declared variable '{name}'.");
            }

            return slot;
        }

        private InvalidOperationException WrongType(string name, MapVariableType actual, MapVariableType requested)
            => new InvalidOperationException(
                $"Map '{MapId.Value}' variable '{name}' is declared as {actual}; {requested} access is not allowed.");
    }

    /// <summary>
    /// One notification per changed variable write. Exactly one old/new pair is
    /// meaningful per call, selected by <paramref name="type"/>; the other pair is zero.
    /// </summary>
    public delegate void MapVariableChangedHandler(
        MapId mapId,
        string varName,
        MapVariableType type,
        int oldInt,
        int newInt,
        float oldFloat,
        float newFloat);
}
