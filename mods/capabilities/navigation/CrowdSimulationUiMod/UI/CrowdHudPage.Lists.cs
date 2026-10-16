using System.Net;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 跟着会话名单走的选项：玩家、阵型、兵种行、检视段、部署与生成的选项格、放置模板。
/// 名单换了（会话换了、玩家数变了）才清空重建，节点用同一个标记解析器从片段建出，样式由 hud.css 统一给。
/// </summary>
internal sealed partial class CrowdHudPage
{
    private const string IconFill = "#3c3934";

    private static readonly string[] ShapePaths =
    [
        """<rect x="2" y="2" width="10" height="10"/>""",
        """<circle cx="7" cy="7" r="5.5"/>""",
        """<polygon points="7,1.5 12.5,12.5 1.5,12.5"/>""",
        """<polygon points="7,1 13,7 7,13 1,7"/>""",
        """<path d="M5 1h4v4h4v4H9v4H5V9H1V5h4z"/>""",
    ];

    private const string EyeSvg =
        """<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#827c73" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/><circle cx="12" cy="12" r="3"/></svg>""";

    private const string EyeOffSvg =
        """<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#827c73" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"/><path d="M14.084 14.158a3 3 0 0 1-4.242-4.242"/><path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"/><path d="m2 2 20 20"/></svg>""";

    private readonly UiMarkupLoader _fragments = new();
    private readonly List<CrowdHudClassSwitch> _playerChips = new();
    private readonly List<CrowdHudClassSwitch> _formationChips = new();
    private readonly List<CrowdHudClassSwitch> _layerRows = new();
    private readonly List<CrowdHudClassSwitch> _layerEyes = new();
    private readonly List<CrowdHudClassSwitch> _layerEyesOff = new();
    private readonly List<CrowdHudClassSwitch> _inspectLayerChips = new();
    private readonly List<CrowdHudClassSwitch> _inspectRadiusChips = new();
    private readonly List<CrowdHudClassSwitch> _deployTypeChips = new();
    private readonly List<CrowdHudClassSwitch> _deployRadiusChips = new();
    private readonly List<CrowdHudClassSwitch> _spawnTypeChips = new();
    private readonly List<CrowdHudClassSwitch> _spawnRadiusChips = new();
    private readonly List<CrowdHudClassSwitch> _structureChips = new();

    private void BuildPlayers(int playerCount)
    {
        UiElement host = Clear("crowd-players", _playerChips);
        int n = Math.Min(playerCount, MaxPlayers);
        for (int p = 1; p <= n; p++)
        {
            int player = p;
            string swatch = _swatches.Length > p && _swatches[p] is { } color
                ? $"""<div class="nf-swatch" style="background-color: {color};"></div>"""
                : """<div class="nf-swatch"></div>""";
            UiElement chip = Fragment($"""<button class="nf-chip nf-player">{swatch}<span>P{p}</span><span class="nf-player-count"></span></button>""");
            chip.Children[2].TextBinding = _playerCounts[p];
            chip.ClickHandler = _ => PickPlayer(player);
            host.AddChild(chip);
            _playerChips.Add(CrowdHudClassSwitch.Toggle(chip, "nf-player-on"));
        }

        Mark(true);
    }

    private void BuildCatalogLists(CrowdHudCatalog? c)
    {
        UiElement formations = Clear("crowd-formations", _formationChips);
        UiElement layers = Clear("crowd-layer-rows", _layerRows);
        _layerEyes.Clear();
        _layerEyesOff.Clear();
        UiElement inspectLayers = Clear("crowd-inspect-layers", _inspectLayerChips);
        UiElement inspectRadii = Clear("crowd-inspect-radii", _inspectRadiusChips);
        UiElement deployTypes = Clear("crowd-deploy-types", _deployTypeChips);
        UiElement deployRadii = Clear("crowd-deploy-radii", _deployRadiusChips);
        UiElement spawnTypes = Clear("crowd-spawn-types", _spawnTypeChips);
        UiElement spawnRadii = Clear("crowd-spawn-radii", _spawnRadiusChips);
        UiElement structures = Clear("crowd-structures", _structureChips);
        Mark(true);
        if (c == null) return;

        for (int i = 0; i < c.Formations.Count; i++)
        {
            int index = i;
            _formationChips.Add(Chip(formations, $"""<button class="nf-chip">{Html(c.Formations[i])}</button>""", "nf-chip-on", () => PickFormation(index)));
        }

        int types = Math.Min(c.UnitTypes.Count, MaxUnitTypes);
        for (int t = 0; t < types; t++)
        {
            int type = t;
            string icon = ShapeIcon(t);
            UiElement row = Fragment(
                $"""<button class="nf-layer-row">{icon}<div class="nf-layer-name"><span class="nf-text">{Html(c.UnitTypes[t])}</span></div><span class="nf-value nf-muted"></span>{EyeSvg}{EyeOffSvg}</button>""");
            row.Children[2].TextBinding = _typeCounts[t];
            row.ClickHandler = _ => ToggleUnitType(type);
            layers.AddChild(row);
            _layerRows.Add(CrowdHudClassSwitch.Toggle(row, "nf-layer-off"));
            _layerEyes.Add(CrowdHudClassSwitch.Visibility(row.Children[3]));
            _layerEyesOff.Add(CrowdHudClassSwitch.Visibility(row.Children[4]));

            _deployTypeChips.Add(OptionChip(deployTypes, $"""<button class="nf-chip nf-chip-sm nf-chip-icon">{icon}</button>""", () => ToggleUnitType(type)));
            _spawnTypeChips.Add(Chip(spawnTypes, $"""<button class="nf-chip nf-chip-sm nf-chip-icon">{icon}</button>""", "nf-chip-on", () => PickSpawnType(type)));
        }

        for (int i = 0; i < c.AgentTypes.Count; i++)
        {
            int index = i;
            _inspectLayerChips.Add(Chip(inspectLayers, $"""<button class="nf-chip">{Html(c.AgentTypes[i])}</button>""", "nf-chip-on", () => PickInspectLayer(index)));
        }

        for (int r = 0; r < c.RadiusCm.Count; r++)
        {
            int index = r;
            string cm = c.RadiusCm[r].ToString(CrowdHudFormat.Inv);
            _inspectRadiusChips.Add(Chip(inspectRadii, $"""<button class="nf-chip">{cm}</button>""", "nf-chip-on", () => PickInspectRadius(index)));
            _deployRadiusChips.Add(Chip(deployRadii, $"""<button class="nf-chip nf-chip-sm nf-chip-num">{cm}</button>""", "nf-chip-on", () => PickDeployRadius(index)));
            _spawnRadiusChips.Add(Chip(spawnRadii, $"""<button class="nf-chip nf-chip-sm nf-chip-num">{cm}</button>""", "nf-chip-on", () => PickSpawnRadius(index)));
        }

        for (int i = 0; i < c.Templates.Count; i++)
        {
            int index = i;
            _structureChips.Add(Chip(structures, $"""<button class="nf-chip nf-chip-text">{Html(c.Templates[i])}</button>""", "nf-chip-on", () => PickTemplate(index)));
        }
    }

    private UiElement Clear(string id, List<CrowdHudClassSwitch> switches)
    {
        UiElement host = Find(id);
        host.ClearChildren();
        switches.Clear();
        return host;
    }

    private CrowdHudClassSwitch Chip(UiElement host, string html, string onToken, Action click)
    {
        UiElement chip = Fragment(html);
        chip.ClickHandler = _ => click();
        host.AddChild(chip);
        return CrowdHudClassSwitch.Toggle(chip, onToken);
    }

    /// <summary>部署兵种格：开是 nf-chip-on，关是原型的 60% 不透明。</summary>
    private CrowdHudClassSwitch OptionChip(UiElement host, string html, Action click)
    {
        UiElement chip = Fragment(html);
        chip.ClickHandler = _ => click();
        host.AddChild(chip);
        return CrowdHudClassSwitch.Options(chip, "nf-chip-dim", "nf-chip-on");
    }

    private UiElement Fragment(string html) => _fragments.LoadDocument(html).Root;

    private static string ShapeIcon(int index) =>
        $"""<svg width="14" height="14" viewBox="0 0 14 14" fill="{IconFill}">{ShapePaths[index % ShapePaths.Length]}</svg>""";

    private static string Html(string text) => WebUtility.HtmlEncode(text);
}
