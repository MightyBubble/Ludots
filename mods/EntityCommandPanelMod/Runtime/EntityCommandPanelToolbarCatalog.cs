using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Platform.Abstractions;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Buttons a mod declares in <c>UI/entity_command_panel_toolbars.json</c>.
    /// Each toolbar names the panel it binds to. Pressing a button injects its input action.
    /// Highlight is a preference pair or an interaction context.
    /// </summary>
    public sealed class EntityCommandPanelToolbarCatalog
    {
        public const string RelativePath = "UI/entity_command_panel_toolbars.json";

        public const string PanelsRelativePath = "UI/entity_command_panel_toolbar_panels.json";

        public static readonly ServiceKey<EntityCommandPanelToolbarCatalog> ServiceKey = new("EntityCommandPanelMod.ToolbarCatalog");

        private readonly FrozenSet<string> _panels;
        private readonly ToolbarDeclaration[] _toolbars;

        private EntityCommandPanelToolbarCatalog(FrozenSet<string> panels, ToolbarDeclaration[] toolbars)
        {
            _panels = panels;
            _toolbars = toolbars;
        }

        public static EntityCommandPanelToolbarCatalog Require(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (engine.TryGetService(ServiceKey, out EntityCommandPanelToolbarCatalog? cached) && cached != null)
            {
                return cached;
            }

            EntityCommandPanelToolbarCatalog loaded = Load(engine);
            engine.SetService(ServiceKey, loaded);
            return loaded;
        }

        public static EntityCommandPanelToolbarCatalog Load(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            FrozenSet<string> panels = LoadPanels(engine);
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                engine.ConfigCatalog,
                RelativePath,
                ConfigMergePolicy.ArrayById,
                "id");
            IReadOnlyList<MergedConfigEntry> merged = engine.ConfigPipeline.MergeArrayByIdFromCatalog(in entry, engine.ConfigConflictReport);
            InteractionPrefCatalog prefs = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                ?? throw Error("requires InteractionPrefCatalog.");
            InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw Error("requires InteractionContextProfileRegistry.");
            if (!engine.TryGetService(CoreServiceKeys.InputActionIds, out FrozenSet<string>? actionIds) || actionIds == null)
            {
                throw Error("requires InputActionIds.");
            }

            var toolbars = new ToolbarDeclaration[merged.Count];
            var toolbarIds = new HashSet<string>(StringComparer.Ordinal);
            var buttonIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < merged.Count; i++)
            {
                toolbars[i] = ParseToolbar(engine, merged[i], prefs, profiles, actionIds, panels);
                if (!toolbarIds.Add(toolbars[i].Id))
                {
                    throw Error($"toolbar '{toolbars[i].Id}' is declared more than once.");
                }

                for (int b = 0; b < toolbars[i].Buttons.Length; b++)
                {
                    string buttonId = toolbars[i].Buttons[b].Id;
                    if (!buttonIds.Add(buttonId))
                    {
                        throw Error($"button '{buttonId}' is declared more than once.");
                    }
                }
            }

            return new EntityCommandPanelToolbarCatalog(panels, toolbars);
        }

        public bool ContainsPanel(string panelId)
        {
            return !string.IsNullOrEmpty(panelId) && _panels.Contains(panelId);
        }

        public bool ContainsToolbar(string toolbarId)
        {
            for (int i = 0; i < _toolbars.Length; i++)
            {
                if (string.Equals(_toolbars[i].Id, toolbarId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasToolbarForPanel(string panelId)
        {
            for (int i = 0; i < _toolbars.Length; i++)
            {
                if (string.Equals(_toolbars[i].Panel, panelId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool PanelUsesContextVisibility(string panelId)
        {
            for (int i = 0; i < _toolbars.Length; i++)
            {
                if (string.Equals(_toolbars[i].Panel, panelId, StringComparison.Ordinal) &&
                    _toolbars[i].ContextProfileIds.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsPanelVisible(GameEngine engine, string panelId, Entity rep)
        {
            return TryGetVisible(engine, panelId, rep, out _);
        }

        public string VisibleToolbarId(GameEngine engine, string panelId, Entity rep)
        {
            return RequireVisible(engine, panelId, rep).Id;
        }

        public string VisibleTitle(GameEngine engine, string panelId, Entity rep)
        {
            ToolbarDeclaration toolbar = RequireVisible(engine, panelId, rep);
            return TextFor(toolbar, engine.CurrentMapSession?.MapId.Value, title: true);
        }

        public string VisibleSubtitle(GameEngine engine, string panelId, Entity rep)
        {
            ToolbarDeclaration toolbar = RequireVisible(engine, panelId, rep);
            return TextFor(toolbar, engine.CurrentMapSession?.MapId.Value, title: false);
        }

        public int CopyVisibleButtons(
            GameEngine engine,
            string panelId,
            Entity rep,
            Span<EntityCommandPanelToolbarButtonView> destination,
            out int total)
        {
            if (!TryGetVisible(engine, panelId, rep, out ToolbarDeclaration? toolbar) || toolbar == null)
            {
                total = 0;
                return 0;
            }

            total = toolbar.Buttons.Length;
            int count = Math.Min(destination.Length, total);
            if (count == 0)
            {
                return 0;
            }

            bool[] active = Evaluate(engine, toolbar, rep);
            for (int i = 0; i < count; i++)
            {
                destination[i] = ToView(toolbar.Buttons[i], active[i]);
            }

            return count;
        }

        public uint HighlightRevision(GameEngine engine, string panelId, Entity rep)
        {
            if (!TryGetVisible(engine, panelId, rep, out ToolbarDeclaration? toolbar) || toolbar == null)
            {
                return 0;
            }

            bool[] active = Evaluate(engine, toolbar, rep);
            uint revision = 0;
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i])
                {
                    revision ^= (uint)(i + 1);
                }
            }

            return revision;
        }

        public string ActiveButtonId(GameEngine engine, string panelId, Entity rep)
        {
            ToolbarDeclaration toolbar = RequireVisible(engine, panelId, rep);
            bool[] active = Evaluate(engine, toolbar, rep);
            string? found = null;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i])
                {
                    continue;
                }

                if (found != null)
                {
                    throw Error($"toolbar '{toolbar.Id}' highlights both '{found}' and '{toolbar.Buttons[i].Id}'.");
                }

                found = toolbar.Buttons[i].Id;
            }

            if (found == null)
            {
                throw Error($"toolbar '{toolbar.Id}' has no highlighted button.");
            }

            return found;
        }

        public string ButtonLabel(GameEngine engine, string panelId, Entity rep, string buttonId)
        {
            ToolbarDeclaration toolbar = RequireVisible(engine, panelId, rep);
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                if (string.Equals(toolbar.Buttons[i].Id, buttonId, StringComparison.Ordinal))
                {
                    return toolbar.Buttons[i].Label;
                }
            }

            throw Error($"toolbar '{toolbar.Id}' has no button '{buttonId}'.");
        }

        public bool TryActivate(GameEngine engine, string panelId, Entity rep, string buttonId)
        {
            if (!TryGetVisible(engine, panelId, rep, out ToolbarDeclaration? toolbar) || toolbar == null)
            {
                return false;
            }

            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                if (!string.Equals(toolbar.Buttons[i].Id, buttonId, StringComparison.Ordinal))
                {
                    continue;
                }

                Inject(engine, rep, toolbar.Buttons[i].ActionId);
                return true;
            }

            return false;
        }

        public void Activate(GameEngine engine, string panelId, Entity rep, string buttonId)
        {
            if (!TryActivate(engine, panelId, rep, buttonId))
            {
                throw Error($"panel '{panelId}' has no visible button '{buttonId}'.");
            }
        }

        private ToolbarDeclaration RequireVisible(GameEngine engine, string panelId, Entity rep)
        {
            if (!TryGetVisible(engine, panelId, rep, out ToolbarDeclaration? toolbar) || toolbar == null)
            {
                throw Error($"panel '{panelId}' has no visible toolbar for map '{engine.CurrentMapSession?.MapId.Value ?? ""}'.");
            }

            return toolbar;
        }

        private bool TryGetVisible(GameEngine engine, string panelId, Entity rep, out ToolbarDeclaration? toolbar)
        {
            toolbar = null;
            for (int i = 0; i < _toolbars.Length; i++)
            {
                if (!string.Equals(_toolbars[i].Panel, panelId, StringComparison.Ordinal) ||
                    !Matches(engine, _toolbars[i], rep))
                {
                    continue;
                }

                if (toolbar != null)
                {
                    throw Error($"toolbars '{toolbar.Id}' and '{_toolbars[i].Id}' are both visible on panel '{panelId}'.");
                }

                toolbar = _toolbars[i];
            }

            return toolbar != null;
        }

        private static bool Matches(GameEngine engine, ToolbarDeclaration toolbar, Entity rep)
        {
            if (toolbar.Maps.Length > 0)
            {
                string? mapId = engine.CurrentMapSession?.MapId.Value;
                for (int i = 0; i < toolbar.Maps.Length; i++)
                {
                    if (string.Equals(toolbar.Maps[i], mapId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (rep == Entity.Null || !engine.World.IsAlive(rep))
            {
                throw Error($"toolbar '{toolbar.Id}' context visibility requires the rep it is rendering for.");
            }

            InteractionContextInstanceRuntime contexts = engine.GetService(CoreServiceKeys.InteractionContextInstances)
                ?? throw Error("context visibility requires InteractionContextInstances.");
            for (int i = 0; i < toolbar.ContextProfileIds.Length; i++)
            {
                if (contexts.IsActive(rep, toolbar.ContextProfileIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool[] Evaluate(GameEngine engine, ToolbarDeclaration toolbar, Entity rep)
        {
            if (rep == Entity.Null || !engine.World.IsAlive(rep))
            {
                throw Error($"toolbar '{toolbar.Id}' requires the rep it is rendering for.");
            }

            var active = new bool[toolbar.Buttons.Length];
            // A live context is the gesture that is open now. Preference highlights stay dark
            // while one is open, so a stored preference and that gesture do not both light.
            if (TryActivateContextHighlight(engine, rep, toolbar, active))
            {
                return active;
            }

            ApplyPrefHighlights(engine, rep, toolbar, active);
            return active;
        }

        private static bool TryActivateContextHighlight(
            GameEngine engine,
            Entity rep,
            ToolbarDeclaration toolbar,
            bool[] active)
        {
            bool anyContext = false;
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                if (toolbar.Buttons[i].Highlight == HighlightKind.Context)
                {
                    anyContext = true;
                    break;
                }
            }

            if (!anyContext)
            {
                return false;
            }

            InteractionContextInstanceRuntime contexts = engine.GetService(CoreServiceKeys.InteractionContextInstances)
                ?? throw Error($"toolbar '{toolbar.Id}' requires InteractionContextInstances.");
            int matched = -1;
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                ButtonDeclaration button = toolbar.Buttons[i];
                if (button.Highlight != HighlightKind.Context || !contexts.IsActive(rep, button.ContextProfileId))
                {
                    continue;
                }

                if (matched >= 0)
                {
                    throw Error(
                        $"toolbar '{toolbar.Id}' highlights both '{toolbar.Buttons[matched].Id}' and '{button.Id}'.");
                }

                matched = i;
            }

            if (matched < 0)
            {
                return false;
            }

            active[matched] = true;
            return true;
        }

        private void ApplyPrefHighlights(GameEngine engine, Entity rep, ToolbarDeclaration toolbar, bool[] active)
        {
            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw Error($"toolbar '{toolbar.Id}' requires InteractionPref on the possessed rep.");
            }

            InteractionPrefCatalog prefs = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                ?? throw Error("requires InteractionPrefCatalog.");
            var seenKeys = new HashSet<int>();
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                ButtonDeclaration button = toolbar.Buttons[i];
                if (button.Highlight != HighlightKind.Pref || !seenKeys.Add(button.PrefKeyId))
                {
                    continue;
                }

                // Ability template id 0 is the player level. IsPref calls this same ResolvePref
                // after it picks the slot ability; a toolbar highlight is not slot-scoped.
                int resolved = pref.ResolvePref(0, button.PrefKeyId);
                if (resolved <= 0)
                {
                    throw Error($"toolbar '{toolbar.Id}' preference '{button.PrefKeyName}' is not seeded on the possessed rep.");
                }

                bool claimed = false;
                for (int other = 0; other < toolbar.Buttons.Length; other++)
                {
                    ButtonDeclaration candidate = toolbar.Buttons[other];
                    if (candidate.Highlight != HighlightKind.Pref || candidate.PrefKeyId != button.PrefKeyId)
                    {
                        continue;
                    }

                    if (candidate.PrefValueId != resolved)
                    {
                        continue;
                    }

                    active[other] = true;
                    claimed = true;
                }

                if (!claimed)
                {
                    throw Error(
                        $"toolbar '{toolbar.Id}' preference '{button.PrefKeyName}' value '{prefs.ValueName(resolved)}' has no highlight.");
                }
            }
        }

        private static string TextFor(ToolbarDeclaration toolbar, string? mapId, bool title)
        {
            if (!string.IsNullOrEmpty(mapId))
            {
                for (int i = 0; i < toolbar.ByMap.Length; i++)
                {
                    MapText text = toolbar.ByMap[i];
                    if (!string.Equals(text.Map, mapId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string? chosen = title ? text.Title : text.Subtitle;
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        return chosen;
                    }
                }
            }

            return title ? toolbar.Title : toolbar.Subtitle;
        }

        private static EntityCommandPanelToolbarButtonView ToView(ButtonDeclaration button, bool active)
        {
            return new EntityCommandPanelToolbarButtonView(button.Id, button.Label, active, button.Accent);
        }

        // A single local seat reads the global handler. When several seats exist, the press
        // goes to the one channel whose possessed rep is the rep the caller passed.
        private static void Inject(GameEngine engine, Entity rep, string actionId)
        {
            if (rep == Entity.Null || !engine.World.IsAlive(rep))
            {
                throw Error($"action '{actionId}' requires the rep it is injecting for.");
            }

            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw Error($"action '{actionId}' requires ClientLocalSeatRegistry.");
            string? seatId = null;
            IReadOnlyList<string> seatIds = seats.SeatIds;
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(seatIds[i]);
                if (!seat.HasPossession || seat.PossessedRep != rep)
                {
                    continue;
                }

                if (seatId != null)
                {
                    throw Error($"action '{actionId}' rep is possessed by both '{seatId}' and '{seat.SeatId}'.");
                }

                seatId = seat.SeatId;
            }

            if (seatId == null)
            {
                throw Error($"action '{actionId}' has no local seat possessing the rep.");
            }

            PlayerInputHandler? handler;
            if (seats.Count <= 1)
            {
                handler = engine.GetService(CoreServiceKeys.InputHandler);
            }
            else
            {
                ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                    ?? throw Error($"action '{actionId}' requires ClientLocalSeatInputRuntime.");
                if (!seatInput.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel))
                {
                    throw Error($"action '{actionId}' cannot attribute to seat '{seatId}'.");
                }

                handler = channel.Handler;
            }

            if (handler == null)
            {
                throw Error($"action '{actionId}' has no input handler.");
            }

            if (!handler.HasAction(actionId))
            {
                throw Error($"action '{actionId}' is not a declared input action.");
            }

            handler.InjectButtonPress(actionId);
        }

        private static FrozenSet<string> LoadPanels(GameEngine engine)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                engine.ConfigCatalog,
                PanelsRelativePath,
                ConfigMergePolicy.ArrayById,
                "id");
            IReadOnlyList<MergedConfigEntry> merged = engine.ConfigPipeline.MergeArrayByIdFromCatalog(in entry, engine.ConfigConflictReport);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < merged.Count; i++)
            {
                MergedConfigEntry panel = merged[i];
                string where = $"'{PanelsRelativePath}' fragment '{panel.SourceUri}' panel '{panel.Id}'";
                RejectUnknown(panel.Node, where, "id");
                string id = RequireToken(ReadString(panel.Node, "id", where), $"{where}.id");
                if (!string.Equals(id, panel.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{where}.id '{id}' does not match the merged id '{panel.Id}'.");
                }

                if (!ids.Add(id))
                {
                    throw new InvalidOperationException($"{where} repeats panel '{id}'.");
                }
            }

            return ids.ToFrozenSet(StringComparer.Ordinal);
        }

        private static ToolbarDeclaration ParseToolbar(
            GameEngine engine,
            MergedConfigEntry entry,
            InteractionPrefCatalog prefs,
            InteractionContextProfileRegistry profiles,
            FrozenSet<string> actionIds,
            FrozenSet<string> panels)
        {
            JsonObject obj = entry.Node;
            string where = $"fragment '{entry.SourceUri}' toolbar '{entry.Id}'";
            RejectUnknown(obj, where, "id", "title", "subtitle", "panel", "maps", "contexts", "byMap", "buttons");
            string id = RequireToken(ReadString(obj, "id", where), $"{where}.id");
            if (!string.Equals(id, entry.Id, StringComparison.Ordinal))
            {
                throw Error($"{where}.id '{id}' does not match the merged id '{entry.Id}'.");
            }

            string title = RequireToken(ReadString(obj, "title", where), $"{where}.title");
            string subtitle = RequireToken(ReadString(obj, "subtitle", where), $"{where}.subtitle");
            string panel = RequireToken(ReadString(obj, "panel", where), $"{where}.panel");
            if (!panels.Contains(panel))
            {
                throw Error($"{where} cannot bind to panel '{panel}'. Declare that panel in '{PanelsRelativePath}'.");
            }
            bool hasMaps = obj.ContainsKey("maps");
            bool hasContexts = obj.ContainsKey("contexts");
            if (hasMaps == hasContexts)
            {
                throw Error($"{where} must declare exactly one of maps or contexts.");
            }

            string[] maps = hasMaps ? ReadStringArray(obj, "maps", where) : Array.Empty<string>();
            string[] contextNames = hasContexts ? ReadStringArray(obj, "contexts", where) : Array.Empty<string>();
            if (maps.Length == 0 && contextNames.Length == 0)
            {
                throw Error($"{where} visibility list is empty.");
            }

            var seenMaps = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < maps.Length; i++)
            {
                maps[i] = RequireToken(maps[i], $"{where}.maps[{i}]");
                if (!seenMaps.Add(maps[i]))
                {
                    throw Error($"{where}.maps[{i}] repeats '{maps[i]}'.");
                }

                RequireKnownMap(engine, maps[i], $"{where}.maps[{i}]");
            }

            var contextProfileIds = new int[contextNames.Length];
            var seenContexts = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < contextNames.Length; i++)
            {
                contextNames[i] = RequireToken(contextNames[i], $"{where}.contexts[{i}]");
                if (!seenContexts.Add(contextNames[i]))
                {
                    throw Error($"{where}.contexts[{i}] repeats '{contextNames[i]}'.");
                }

                contextProfileIds[i] = RequireProfile(profiles, contextNames[i], $"{where}.contexts[{i}]");
            }

            if (obj.ContainsKey("byMap") && !hasMaps)
            {
                throw Error($"{where}.byMap requires maps.");
            }

            MapText[] byMap = ReadByMap(obj, where, seenMaps);
            ButtonDeclaration[] buttons = ReadButtons(obj, where, prefs, profiles, actionIds);
            return new ToolbarDeclaration(id, panel, title, subtitle, maps, contextProfileIds, byMap, buttons);
        }

        private static MapText[] ReadByMap(JsonObject obj, string where, HashSet<string> maps)
        {
            if (!obj.TryGetPropertyValue("byMap", out JsonNode? node) || node == null)
            {
                return Array.Empty<MapText>();
            }

            if (node is not JsonArray array)
            {
                throw Error($"{where}.byMap must be an array.");
            }

            var texts = new MapText[array.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject text)
                {
                    throw Error($"{where}.byMap[{i}] must be an object.");
                }

                string itemWhere = $"{where}.byMap[{i}]";
                RejectUnknown(text, itemWhere, "map", "title", "subtitle");
                string map = RequireToken(ReadString(text, "map", itemWhere), $"{itemWhere}.map");
                if (!maps.Contains(map))
                {
                    throw Error($"{itemWhere}.map '{map}' is not listed in maps.");
                }

                if (!seen.Add(map))
                {
                    throw Error($"{itemWhere}.map repeats '{map}'.");
                }

                string? title = OptionalToken(text, "title", itemWhere);
                string? subtitle = OptionalToken(text, "subtitle", itemWhere);
                if (title == null && subtitle == null)
                {
                    throw Error($"{itemWhere} must declare title or subtitle.");
                }

                texts[i] = new MapText(map, title, subtitle);
            }

            return texts;
        }

        private static ButtonDeclaration[] ReadButtons(
            JsonObject obj,
            string where,
            InteractionPrefCatalog prefs,
            InteractionContextProfileRegistry profiles,
            FrozenSet<string> actionIds)
        {
            if (obj["buttons"] is not JsonArray array || array.Count == 0)
            {
                throw Error($"{where}.buttons must be a non-empty array.");
            }

            var buttons = new ButtonDeclaration[array.Count];
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenPref = new HashSet<(int Key, int Value)>();
            var seenContext = new HashSet<int>();
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject button)
                {
                    throw Error($"{where}.buttons[{i}] must be an object.");
                }

                string buttonWhere = $"{where}.buttons[{i}]";
                RejectUnknown(button, buttonWhere, "id", "label", "accent", "actionId", "highlight");
                string id = RequireToken(ReadString(button, "id", buttonWhere), $"{buttonWhere}.id");
                if (!seenIds.Add(id))
                {
                    throw Error($"{buttonWhere}.id repeats '{id}'.");
                }

                string label = RequireToken(ReadString(button, "label", buttonWhere), $"{buttonWhere}.label");
                string accent = OptionalToken(button, "accent", buttonWhere) ?? string.Empty;
                string actionId = RequireToken(ReadString(button, "actionId", buttonWhere), $"{buttonWhere}.actionId");
                if (!actionIds.Contains(actionId))
                {
                    throw Error($"{buttonWhere}.actionId '{actionId}' is not a declared input action.");
                }

                HighlightKind highlight = HighlightKind.None;
                int prefKeyId = 0;
                int prefValueId = 0;
                string prefKeyName = string.Empty;
                int contextProfileId = 0;
                string contextId = string.Empty;
                if (button.ContainsKey("highlight"))
                {
                    if (button["highlight"] is not JsonObject highlightObj)
                    {
                        throw Error($"{buttonWhere}.highlight must be an object.");
                    }

                    string highlightWhere = $"{buttonWhere}.highlight";
                    RejectUnknown(highlightObj, highlightWhere, "pref", "context");
                    bool hasPref = highlightObj.ContainsKey("pref");
                    bool hasContext = highlightObj.ContainsKey("context");
                    if (hasPref == hasContext)
                    {
                        throw Error($"{highlightWhere} must declare exactly one of pref or context.");
                    }

                    if (hasPref)
                    {
                        if (highlightObj["pref"] is not JsonObject prefObj)
                        {
                            throw Error($"{highlightWhere}.pref must be an object.");
                        }

                        string prefWhere = $"{highlightWhere}.pref";
                        RejectUnknown(prefObj, prefWhere, "key", "value");
                        prefKeyName = RequireToken(ReadString(prefObj, "key", prefWhere), $"{prefWhere}.key");
                        string prefValueName = RequireToken(ReadString(prefObj, "value", prefWhere), $"{prefWhere}.value");
                        prefKeyId = prefs.Keys.GetId(prefKeyName);
                        if (prefKeyId == prefs.Keys.InvalidId)
                        {
                            throw Error($"{prefWhere}.key '{prefKeyName}' is not a declared preference key.");
                        }

                        prefValueId = prefs.Values.GetId(prefValueName);
                        if (prefValueId == prefs.Values.InvalidId || !prefs.Allows(prefKeyId, prefValueId))
                        {
                            throw Error($"{prefWhere}.value '{prefValueName}' is not allowed for key '{prefKeyName}'.");
                        }

                        if (!seenPref.Add((prefKeyId, prefValueId)))
                        {
                            throw Error($"{prefWhere} repeats key '{prefKeyName}' value '{prefValueName}'.");
                        }

                        highlight = HighlightKind.Pref;
                    }
                    else
                    {
                        contextId = RequireToken(ReadString(highlightObj, "context", highlightWhere), $"{highlightWhere}.context");
                        contextProfileId = RequireProfile(profiles, contextId, $"{highlightWhere}.context");
                        if (!seenContext.Add(contextProfileId))
                        {
                            throw Error($"{highlightWhere}.context repeats '{contextId}'.");
                        }

                        highlight = HighlightKind.Context;
                    }
                }

                buttons[i] = new ButtonDeclaration(
                    id,
                    label,
                    accent,
                    actionId,
                    highlight,
                    prefKeyId,
                    prefValueId,
                    prefKeyName,
                    contextProfileId,
                    contextId);
            }

            return buttons;
        }

        private static int RequireProfile(InteractionContextProfileRegistry profiles, string contextId, string field)
        {
            if (!profiles.ProfileIdRegistry.TryGetId(contextId, out int profileId) || !profiles.IsInstalled(profileId))
            {
                throw Error($"{field} '{contextId}' is not an installed interaction context.");
            }

            return profileId;
        }

        private static void RequireKnownMap(GameEngine engine, string mapId, string field)
        {
            if (engine.MapManager is MapManager manager && manager.GetDefinition(new MapId(mapId)) != null)
            {
                return;
            }

            if (engine.ConfigPipeline.CollectFragments($"Maps/{mapId}.json").Count > 0)
            {
                return;
            }

            throw Error($"{field} '{mapId}' is not a loaded map.");
        }

        private static void RejectUnknown(JsonObject obj, string where, params string[] allowed)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(pair.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw Error($"{where} contains unknown field '{pair.Key}'.");
                }
            }
        }

        private static string[] ReadStringArray(JsonObject obj, string field, string where)
        {
            if (obj[field] is not JsonArray array)
            {
                throw Error($"{where}.{field} must be an array.");
            }

            var values = new string[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonValue value || !value.TryGetValue<string>(out string? text) || text == null)
                {
                    throw Error($"{where}.{field}[{i}] must be a string.");
                }

                values[i] = text;
            }

            return values;
        }

        private static string ReadString(JsonObject obj, string field, string where)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text) ||
                text == null)
            {
                throw Error($"{where}.{field} must be a string.");
            }

            return text;
        }

        private static string? OptionalToken(JsonObject obj, string field, string where)
        {
            if (!obj.ContainsKey(field))
            {
                return null;
            }

            return RequireToken(ReadString(obj, field, where), $"{where}.{field}");
        }

        private static string RequireToken(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw Error($"{field} must be a non-empty token without surrounding whitespace.");
            }

            return value;
        }

        private static InvalidOperationException Error(string message)
        {
            return new InvalidOperationException($"'{RelativePath}' {message}");
        }

        private enum HighlightKind : byte
        {
            None = 0,
            Pref = 1,
            Context = 2
        }

        private readonly record struct MapText(string Map, string? Title, string? Subtitle);

        private readonly record struct ButtonDeclaration(
            string Id,
            string Label,
            string Accent,
            string ActionId,
            HighlightKind Highlight,
            int PrefKeyId,
            int PrefValueId,
            string PrefKeyName,
            int ContextProfileId,
            string ContextId);

        private sealed class ToolbarDeclaration
        {
            public ToolbarDeclaration(
                string id,
                string panel,
                string title,
                string subtitle,
                string[] maps,
                int[] contextProfileIds,
                MapText[] byMap,
                ButtonDeclaration[] buttons)
            {
                Id = id;
                Panel = panel;
                Title = title;
                Subtitle = subtitle;
                Maps = maps;
                ContextProfileIds = contextProfileIds;
                ByMap = byMap;
                Buttons = buttons;
            }

            public string Id { get; }
            public string Panel { get; }
            public string Title { get; }
            public string Subtitle { get; }
            public string[] Maps { get; }
            public int[] ContextProfileIds { get; }
            public MapText[] ByMap { get; }
            public ButtonDeclaration[] Buttons { get; }
        }
    }
}
