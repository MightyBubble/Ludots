using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Platform.Abstractions;
using Ludots.Core.Gameplay.MapTriggers;
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
    /// Highlight is an exhaustive preference, an exact preference match, or an interaction context.
    /// A subtitle may be a template filled from those preferences and the current map's integer variables.
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
            for (int i = 0; i < merged.Count; i++)
            {
                toolbars[i] = ParseToolbar(engine, merged[i], prefs, profiles, actionIds, panels);
                if (!toolbarIds.Add(toolbars[i].Id))
                {
                    throw Error($"toolbar '{toolbars[i].Id}' is declared more than once.");
                }
            }

            // Activate resolves a button id on the one visible toolbar. The same id may
            // therefore appear on two toolbars only when those two cannot be visible together.
            for (int i = 0; i < toolbars.Length; i++)
            {
                for (int j = i + 1; j < toolbars.Length; j++)
                {
                    if (CanBeVisibleTogether(toolbars[i], toolbars[j]))
                    {
                        RejectSharedButtonIds(toolbars[i], toolbars[j]);
                    }
                }
            }

            return new EntityCommandPanelToolbarCatalog(panels, toolbars);
        }

        private static bool CanBeVisibleTogether(ToolbarDeclaration left, ToolbarDeclaration right)
        {
            if (!string.Equals(left.Panel, right.Panel, StringComparison.Ordinal))
            {
                return false;
            }

            if (left.Maps.Length == 0 || right.Maps.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < left.Maps.Length; i++)
            {
                for (int j = 0; j < right.Maps.Length; j++)
                {
                    if (string.Equals(left.Maps[i], right.Maps[j], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RejectSharedButtonIds(ToolbarDeclaration left, ToolbarDeclaration right)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < left.Buttons.Length; i++)
            {
                ids.Add(left.Buttons[i].Id);
            }

            for (int i = 0; i < right.Buttons.Length; i++)
            {
                if (ids.Contains(right.Buttons[i].Id))
                {
                    throw Error(
                        $"button '{right.Buttons[i].Id}' is declared on both '{left.Id}' and '{right.Id}', which can be visible together.");
                }
            }
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
            return TitleFor(toolbar, engine.CurrentMapSession?.MapId.Value);
        }

        public string VisibleSubtitle(GameEngine engine, string panelId, Entity rep)
        {
            ToolbarDeclaration toolbar = RequireVisible(engine, panelId, rep);
            return ResolveSubtitle(engine, toolbar, rep);
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
            uint revision = Hash(ResolveSubtitle(engine, toolbar, rep));
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
            // A live context is the gesture that is open now. Exhaustive preference highlights
            // stay dark while one is open. An exact match still lights: it does not claim the row.
            if (!TryActivateContextHighlight(engine, rep, toolbar, active))
            {
                ApplyPrefHighlights(engine, rep, toolbar, active);
            }

            ApplyEqualsHighlights(engine, rep, toolbar, active);
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

        private static void ApplyEqualsHighlights(GameEngine engine, Entity rep, ToolbarDeclaration toolbar, bool[] active)
        {
            bool any = false;
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                if (toolbar.Buttons[i].Highlight == HighlightKind.Equals)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return;
            }

            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw Error($"toolbar '{toolbar.Id}' requires InteractionPref on the possessed rep.");
            }

            InteractionPrefCatalog prefs = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                ?? throw Error("requires InteractionPrefCatalog.");
            for (int i = 0; i < toolbar.Buttons.Length; i++)
            {
                ButtonDeclaration button = toolbar.Buttons[i];
                if (button.Highlight != HighlightKind.Equals)
                {
                    continue;
                }

                int resolved = pref.ResolvePref(0, button.PrefKeyId);
                if (resolved <= 0)
                {
                    throw Error($"toolbar '{toolbar.Id}' preference '{button.PrefKeyName}' is not seeded on the possessed rep.");
                }

                if (resolved == button.PrefValueId)
                {
                    active[i] = true;
                }
            }
        }

        private static string TitleFor(ToolbarDeclaration toolbar, string? mapId)
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

                    if (!string.IsNullOrEmpty(text.Title))
                    {
                        return text.Title;
                    }
                }
            }

            return toolbar.Title;
        }

        private static string ResolveSubtitle(GameEngine engine, ToolbarDeclaration toolbar, Entity rep)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!string.IsNullOrEmpty(mapId))
            {
                for (int i = 0; i < toolbar.ByMap.Length; i++)
                {
                    MapText text = toolbar.ByMap[i];
                    if (string.Equals(text.Map, mapId, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(text.Subtitle))
                    {
                        return text.Subtitle;
                    }
                }
            }

            SubtitleBinding subtitle = toolbar.Subtitle;
            if (subtitle.Literal != null)
            {
                return subtitle.Literal;
            }

            string template = subtitle.Template ?? string.Empty;
            var filled = new StringBuilder(template.Length + 32);
            int cursor = 0;
            while (cursor < template.Length)
            {
                int open = template.IndexOf('{', cursor);
                if (open < 0)
                {
                    filled.Append(template, cursor, template.Length - cursor);
                    break;
                }

                filled.Append(template, cursor, open - cursor);
                int close = template.IndexOf('}', open + 1);
                string name = template.Substring(open + 1, close - open - 1);
                filled.Append(ResolveSlot(engine, toolbar, rep, subtitle, name));
                cursor = close + 1;
            }

            return filled.ToString();
        }

        private static string ResolveSlot(
            GameEngine engine,
            ToolbarDeclaration toolbar,
            Entity rep,
            SubtitleBinding subtitle,
            string name)
        {
            SubtitleSlot slot = default;
            bool found = false;
            for (int i = 0; i < subtitle.Slots.Length; i++)
            {
                if (string.Equals(subtitle.Slots[i].Name, name, StringComparison.Ordinal))
                {
                    slot = subtitle.Slots[i];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw Error($"toolbar '{toolbar.Id}' subtitle slot '{name}' is not declared.");
            }

            if (slot.MapVar)
            {
                MapVariableStore? variables = engine.CurrentMapSession?.Variables;
                if (variables == null)
                {
                    throw Error($"toolbar '{toolbar.Id}' subtitle map variable '{slot.MapVarName}' requires the current map's variables.");
                }

                return variables.ReadInt(slot.MapVarName).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (rep == Entity.Null || !engine.World.IsAlive(rep) || !engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw Error($"toolbar '{toolbar.Id}' subtitle preference '{slot.PrefKeyName}' requires InteractionPref on the possessed rep.");
            }

            int resolved = pref.ResolvePref(0, slot.PrefKeyId);
            if (resolved <= 0)
            {
                throw Error($"toolbar '{toolbar.Id}' preference '{slot.PrefKeyName}' is not seeded on the possessed rep.");
            }

            if (!slot.Labels.TryGetValue(resolved, out string? label))
            {
                InteractionPrefCatalog prefs = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                    ?? throw Error("requires InteractionPrefCatalog.");
                throw Error(
                    $"toolbar '{toolbar.Id}' subtitle preference '{slot.PrefKeyName}' value '{prefs.ValueName(resolved)}' has no label.");
            }

            return label;
        }

        private static uint Hash(string text)
        {
            uint hash = 2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619;
            }

            return hash;
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
            if (!obj.TryGetPropertyValue("subtitle", out JsonNode? subtitleNode) || subtitleNode == null)
            {
                throw Error($"{where}.subtitle is required.");
            }
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
            SubtitleBinding subtitle = ReadSubtitle(engine, subtitleNode, where, prefs, maps);
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
            var exhaustiveKeys = new HashSet<int>();
            var equalsKeys = new HashSet<int>();
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
                    RejectUnknown(highlightObj, highlightWhere, "pref", "context", "equals");
                    bool hasPref = highlightObj.ContainsKey("pref");
                    bool hasContext = highlightObj.ContainsKey("context");
                    bool hasEquals = highlightObj.ContainsKey("equals");
                    int highlightCount = (hasPref ? 1 : 0) + (hasContext ? 1 : 0) + (hasEquals ? 1 : 0);
                    if (highlightCount != 1)
                    {
                        throw Error($"{highlightWhere} must declare exactly one of pref, equals, or context.");
                    }

                    if (hasPref || hasEquals)
                    {
                        string field = hasPref ? "pref" : "equals";
                        if (highlightObj[field] is not JsonObject prefObj)
                        {
                            throw Error($"{highlightWhere}.{field} must be an object.");
                        }

                        string prefWhere = $"{highlightWhere}.{field}";
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

                        if (hasPref)
                        {
                            exhaustiveKeys.Add(prefKeyId);
                            highlight = HighlightKind.Pref;
                        }
                        else
                        {
                            equalsKeys.Add(prefKeyId);
                            highlight = HighlightKind.Equals;
                        }
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

            foreach (int keyId in equalsKeys)
            {
                if (exhaustiveKeys.Contains(keyId))
                {
                    throw Error($"{where} preference '{prefs.KeyName(keyId)}' cannot be both an exhaustive pref highlight and an equals highlight.");
                }
            }

            return buttons;
        }

        private static SubtitleBinding ReadSubtitle(
            GameEngine engine,
            JsonNode node,
            string where,
            InteractionPrefCatalog prefs,
            string[] maps)
        {
            if (node is JsonValue literalValue && literalValue.TryGetValue<string>(out string? literal) && literal != null)
            {
                return SubtitleBinding.FromLiteral(RequireToken(literal, $"{where}.subtitle"));
            }

            if (node is not JsonObject obj)
            {
                throw Error($"{where}.subtitle must be a string or an object.");
            }

            string subtitleWhere = $"{where}.subtitle";
            RejectUnknown(obj, subtitleWhere, "template", "slots");
            string template = RequireToken(ReadString(obj, "template", subtitleWhere), $"{subtitleWhere}.template");
            string[] placeholders = ReadPlaceholders(template, subtitleWhere);
            SubtitleSlot[] slots = ReadSubtitleSlots(engine, obj, subtitleWhere, prefs, maps, placeholders);
            return SubtitleBinding.Templated(template, slots);
        }

        private static string[] ReadPlaceholders(string template, string where)
        {
            var names = new List<string>();
            for (int i = 0; i < template.Length; i++)
            {
                char current = template[i];
                if (current == '}')
                {
                    throw Error($"{where}.template has a closing brace without an opening brace.");
                }

                if (current != '{')
                {
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    throw Error($"{where}.template has an opening brace without a closing brace.");
                }

                string name = template.Substring(i + 1, close - i - 1);
                if (name.Length == 0 || name.IndexOf('{') >= 0 || name.IndexOf(' ') >= 0)
                {
                    throw Error($"{where}.template placeholder '{name}' is not a slot name.");
                }

                names.Add(name);
                i = close;
            }

            return names.ToArray();
        }

        private static SubtitleSlot[] ReadSubtitleSlots(
            GameEngine engine,
            JsonObject subtitle,
            string where,
            InteractionPrefCatalog prefs,
            string[] maps,
            string[] placeholders)
        {
            if (subtitle["slots"] is not JsonArray array)
            {
                throw Error($"{where}.slots must be an array.");
            }

            var slots = new SubtitleSlot[array.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject slot)
                {
                    throw Error($"{where}.slots[{i}] must be an object.");
                }

                string slotWhere = $"{where}.slots[{i}]";
                RejectUnknown(slot, slotWhere, "name", "pref", "labels", "mapVar");
                string name = RequireToken(ReadString(slot, "name", slotWhere), $"{slotWhere}.name");
                if (!seen.Add(name))
                {
                    throw Error($"{slotWhere}.name repeats '{name}'.");
                }

                bool hasPref = slot.ContainsKey("pref");
                bool hasMapVar = slot.ContainsKey("mapVar");
                if (hasPref == hasMapVar)
                {
                    throw Error($"{slotWhere} must declare exactly one of pref or mapVar.");
                }

                if (hasMapVar)
                {
                    if (slot.ContainsKey("labels"))
                    {
                        throw Error($"{slotWhere}.labels is only valid on a pref slot.");
                    }

                    if (maps.Length == 0)
                    {
                        throw Error($"{slotWhere}.mapVar requires the toolbar to list maps.");
                    }

                    string varName = RequireToken(ReadString(slot, "mapVar", slotWhere), $"{slotWhere}.mapVar");
                    for (int m = 0; m < maps.Length; m++)
                    {
                        RequireIntMapVariable(engine, maps[m], varName, slotWhere);
                    }

                    slots[i] = new SubtitleSlot(name, 0, string.Empty, true, varName, new Dictionary<int, string>());
                    continue;
                }

                string prefKeyName = RequireToken(ReadString(slot, "pref", slotWhere), $"{slotWhere}.pref");
                int prefKeyId = prefs.Keys.GetId(prefKeyName);
                if (prefKeyId == prefs.Keys.InvalidId)
                {
                    throw Error($"{slotWhere}.pref '{prefKeyName}' is not a declared preference key.");
                }

                if (slot["labels"] is not JsonObject labels)
                {
                    throw Error($"{slotWhere}.labels must be an object.");
                }

                IReadOnlyList<int> allowed = prefs.AllowedValueIds(prefKeyId);
                var byValue = new Dictionary<int, string>(allowed.Count);
                var labeled = new HashSet<int>();
                foreach (KeyValuePair<string, JsonNode?> pair in labels)
                {
                    string valueName = RequireToken(pair.Key, $"{slotWhere}.labels key");
                    int valueId = prefs.Values.GetId(valueName);
                    if (valueId == prefs.Values.InvalidId || !prefs.Allows(prefKeyId, valueId))
                    {
                        throw Error($"{slotWhere}.labels '{valueName}' is not allowed for key '{prefKeyName}'.");
                    }

                    if (pair.Value is not JsonValue labelValue ||
                        !labelValue.TryGetValue<string>(out string? label) ||
                        label == null)
                    {
                        throw Error($"{slotWhere}.labels '{valueName}' must be a string.");
                    }

                    byValue[valueId] = RequireToken(label, $"{slotWhere}.labels '{valueName}'");
                    labeled.Add(valueId);
                }

                for (int v = 0; v < allowed.Count; v++)
                {
                    if (!labeled.Contains(allowed[v]))
                    {
                        throw Error($"{slotWhere}.labels is missing '{prefs.ValueName(allowed[v])}' for key '{prefKeyName}'.");
                    }
                }

                slots[i] = new SubtitleSlot(name, prefKeyId, prefKeyName, false, string.Empty, byValue);
            }

            var used = new HashSet<string>(placeholders, StringComparer.Ordinal);
            for (int i = 0; i < placeholders.Length; i++)
            {
                if (!seen.Contains(placeholders[i]))
                {
                    throw Error($"{where}.template placeholder '{placeholders[i]}' has no slot.");
                }
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (!used.Contains(slots[i].Name))
                {
                    throw Error($"{where}.slots '{slots[i].Name}' is not used by the template.");
                }
            }

            return slots;
        }

        private static void RequireIntMapVariable(GameEngine engine, string mapId, string varName, string field)
        {
            List<JsonNode> fragments = engine.ConfigPipeline.CollectFragments($"Maps/{mapId}.json");
            var types = new Dictionary<string, MapVariableType>(StringComparer.Ordinal);
            for (int i = 0; i < fragments.Count; i++)
            {
                JsonNode? variablesNode = null;
                if (fragments[i] is JsonObject root)
                {
                    foreach (KeyValuePair<string, JsonNode?> pair in root)
                    {
                        if (string.Equals(pair.Key, "Variables", StringComparison.OrdinalIgnoreCase))
                        {
                            variablesNode = pair.Value;
                            break;
                        }
                    }
                }

                List<MapVariableDeclaration> declared = MapVariableDeclarations.Parse(variablesNode, mapId);
                for (int v = 0; v < declared.Count; v++)
                {
                    MapVariableDeclaration item = declared[v];
                    string name = item.Name ?? string.Empty;
                    if (item.Delete == true)
                    {
                        types.Remove(name);
                        continue;
                    }

                    if (types.TryGetValue(name, out MapVariableType existing) && existing != item.Type)
                    {
                        throw Error($"{field} map '{mapId}' redeclares variable '{name}' with a different type.");
                    }

                    types[name] = item.Type;
                }
            }

            if (!types.TryGetValue(varName, out MapVariableType type))
            {
                throw Error($"{field} map variable '{varName}' is not declared on map '{mapId}'.");
            }

            if (type != MapVariableType.Int)
            {
                throw Error($"{field} map variable '{varName}' on map '{mapId}' must be int.");
            }
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
            Context = 2,
            Equals = 3
        }

        private readonly record struct MapText(string Map, string? Title, string? Subtitle);

        private readonly record struct SubtitleSlot(
            string Name,
            int PrefKeyId,
            string PrefKeyName,
            bool MapVar,
            string MapVarName,
            Dictionary<int, string> Labels);

        private sealed class SubtitleBinding
        {
            private SubtitleBinding(string? literal, string? template, SubtitleSlot[] slots)
            {
                Literal = literal;
                Template = template;
                Slots = slots;
            }

            public string? Literal { get; }

            public string? Template { get; }

            public SubtitleSlot[] Slots { get; }

            public static SubtitleBinding FromLiteral(string text) => new(text, null, Array.Empty<SubtitleSlot>());

            public static SubtitleBinding Templated(string template, SubtitleSlot[] slots) => new(null, template, slots);
        }

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
                SubtitleBinding subtitle,
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
            public SubtitleBinding Subtitle { get; }
            public string[] Maps { get; }
            public int[] ContextProfileIds { get; }
            public MapText[] ByMap { get; }
            public ButtonDeclaration[] Buttons { get; }
        }
    }
}
