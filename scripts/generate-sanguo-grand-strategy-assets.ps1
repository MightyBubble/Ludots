$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $repoRoot "mods/showcases/sanguo_grand_strategy/SanguoGrandStrategyMod"
$assetRoot = Join-Path $modRoot "assets"
$runtimeRoot = Join-Path $modRoot "Runtime"

function Save-Json($Path, $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 32
    Set-Content -Path $Path -Value ($json + "`n") -Encoding UTF8
}

function CsString([string]$Value) {
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

$factions = @(
    [ordered]@{ TeamId = 1; Id = "wei"; Name = "Wei"; Ruler = "Cao Council"; Color = "#4D8DFF"; Style = "Central plains administration, crossbow lines, heavy cavalry reserves." },
    [ordered]@{ TeamId = 2; Id = "shu"; Name = "Shu"; Ruler = "Liu Court"; Color = "#5CC86D"; Style = "Mountain defense, veteran spears, resilient governance." },
    [ordered]@{ TeamId = 3; Id = "wu"; Name = "Wu"; Ruler = "Sun Admiralty"; Color = "#F1B84B"; Style = "River logistics, naval reach, fast coastal raids." },
    [ordered]@{ TeamId = 4; Id = "han"; Name = "Han"; Ruler = "Imperial Secretariat"; Color = "#B989FF"; Style = "Legitimacy, diplomacy, mixed frontier garrisons." },
    [ordered]@{ TeamId = 5; Id = "yuan"; Name = "Yuan"; Ruler = "Northern Coalition"; Color = "#F06A6A"; Style = "Large levies, wealthy grain belts, brittle loyalty." },
    [ordered]@{ TeamId = 6; Id = "liang"; Name = "Liang"; Ruler = "Western Protectorate"; Color = "#9EC3D9"; Style = "Horse archers, passes, long supply lines." },
    [ordered]@{ TeamId = 7; Id = "nanman"; Name = "Nanman"; Ruler = "Southern League"; Color = "#72D1B8"; Style = "Jungle mobility, elephants, high morale shock troops." },
    [ordered]@{ TeamId = 8; Id = "gongsun"; Name = "Gongsun"; Ruler = "Liaodong Command"; Color = "#D7A06B"; Style = "Frontier cavalry, ports, compact defensive cities." }
)

$regions = @(
    @{ Name = "Youzhou"; Team = 8; CenterX = 210000; CenterY = 142000; RadiusX = 62000; RadiusY = 38000; Prefix = @("Bei","Ji","Zhuo","Yu","Shang","Dai","Fan","Yan","Ping","Gu"); Suffix = @("ping","cheng","guan","yuan","zhou","du","tai","ling","men","an") },
    @{ Name = "Jizhou"; Team = 5; CenterX = 112000; CenterY = 108000; RadiusX = 70000; RadiusY = 44000; Prefix = @("Ye","Han","Zhong","Chang","Bo","He","Qing","Ping","Wei","Zhao"); Suffix = @("du","dan","shan","ling","yuan","jian","he","tai","cheng","xiang") },
    @{ Name = "Qingzhou"; Team = 1; CenterX = 190000; CenterY = 62000; RadiusX = 62000; RadiusY = 36000; Prefix = @("Lin","Bei","Dong","Gao","Lai","Qi","Ju","Lang","Tai","Hai"); Suffix = @("zi","hai","lai","mi","yang","shan","xian","ya","an","kou") },
    @{ Name = "Yanzhou"; Team = 1; CenterX = 86000; CenterY = 36000; RadiusX = 69000; RadiusY = 42000; Prefix = @("Pu","Chen","Xu","Luo","Ying","Qiao","Run","Nan","Huo","Ding"); Suffix = @("yang","liu","chang","yang","chuan","xian","an","du","guan","tao") },
    @{ Name = "Sili"; Team = 4; CenterX = 12000; CenterY = 34000; RadiusX = 74000; RadiusY = 42000; Prefix = @("Chang","Mei","Wu","Chen","Hong","Hedong","Ping","Shang","Xin","Guo"); Suffix = @("an","xian","gong","cang","nong","guan","yang","luo","feng","jin") },
    @{ Name = "Liangzhou"; Team = 6; CenterX = -132000; CenterY = 96000; RadiusX = 98000; RadiusY = 56000; Prefix = @("Tian","Long","Jin","Wu","Zhang","Jiu","Dun","Xi","An","Bei"); Suffix = @("shui","xi","cheng","wei","ye","quan","huang","ping","ding","di") },
    @{ Name = "Yizhou"; Team = 2; CenterX = -98000; CenterY = -62000; RadiusX = 76000; RadiusY = 62000; Prefix = @("Cheng","Guang","Mian","Zi","Jian","Han","Ba","Jiang","Qian","Lang"); Suffix = @("du","han","zhu","tong","men","zhong","xi","zhou","wei","zhong") },
    @{ Name = "Jingzhou"; Team = 4; CenterX = 42000; CenterY = -52000; RadiusX = 80000; RadiusY = 62000; Prefix = @("Xiang","Fan","Xin","Yi","An","Sui","Ru","Cai","Qi","Huang"); Suffix = @("yang","cheng","ye","cheng","lu","zhou","nan","yang","chun","gang") },
    @{ Name = "Yangzhou"; Team = 3; CenterX = 152000; CenterY = -62000; RadiusX = 74000; RadiusY = 56000; Prefix = @("Jian","Wu","Kuai","Yu","Dan","Xuan","Guang","Hefei","Lu","Chai"); Suffix = @("ye","jun","ji","hang","yang","cheng","ling","kou","jiang","sang") },
    @{ Name = "Wuyue"; Team = 3; CenterX = 202000; CenterY = -128000; RadiusX = 58000; RadiusY = 52000; Prefix = @("Hang","Ning","Yong","Lin","Jian","Hou","Fu","Quan","Wen","Tai"); Suffix = @("zhou","bo","jia","hai","an","guan","zhou","zhou","ling","zhou") },
    @{ Name = "Nanzhong"; Team = 7; CenterX = -74000; CenterY = -154000; RadiusX = 92000; RadiusY = 68000; Prefix = @("Yun","Dian","Yong","Jian","Zang","Dali","Kun","Qu","Li","Bao"); Suffix = @("nan","chi","chang","ning","ke","fu","ming","jing","jiang","shan") },
    @{ Name = "Jiaozhou"; Team = 7; CenterX = 76000; CenterY = -182000; RadiusX = 98000; RadiusY = 58000; Prefix = @("Jiao","Nan","Cang","Gui","Hepu","Long","Yu","Xiang","Lin","Pan"); Suffix = @("zhi","hai","wu","yang","pu","bian","lin","jun","yi","zhou") }
)

$cities = New-Object System.Collections.Generic.List[object]
$cityIndex = 1
foreach ($region in $regions) {
    for ($i = 0; $i -lt 25; $i++) {
        $prefix = $region.Prefix[$i % $region.Prefix.Count]
        $suffix = $region.Suffix[(($i * 3) + [math]::Floor($i / 2)) % $region.Suffix.Count]
        $name = "$($region.Name) $prefix$suffix"
        $angle = (2.0 * [math]::PI * $i / 25.0) + ($region.Team * 0.19)
        $ring = 0.30 + (((($i * 37) + ($region.Team * 11)) % 70) / 100.0)
        $x = [int][math]::Round($region.CenterX + [math]::Cos($angle) * $region.RadiusX * $ring)
        $y = [int][math]::Round($region.CenterY + [math]::Sin($angle) * $region.RadiusY * $ring)
        $population = 18000 + (($cityIndex * 917) % 82000)
        $troops = 3200 + (($cityIndex * 431) % 18000)
        $food = 7200 + (($cityIndex * 773) % 42000)
        $gold = 2400 + (($cityIndex * 557) % 26000)
        $production = 12 + (($cityIndex * 13) % 88)
        $defense = 18 + (($cityIndex * 17) % 82)
        $morale = 45 + (($cityIndex * 19) % 55)
        $loyalty = 38 + (($cityIndex * 23) % 62)
        $training = 10 + (($cityIndex * 29) % 90)
        $cities.Add([ordered]@{
            Id = ("sanguo_city_{0:000}" -f $cityIndex)
            Name = $name
            Region = $region.Name
            TeamId = $region.Team
            X = $x
            Y = $y
            Population = $population
            Troops = $troops
            Food = $food
            Gold = $gold
            Production = $production
            Defense = $defense
            Morale = $morale
            Loyalty = $loyalty
            Training = $training
        })
        $cityIndex++
    }
}

$categories = @("Infantry","Spear","Cavalry","Archer","Crossbow","Siege","Naval","Scout","Guard","Elephant")
$doctrines = @("Qingzhou","Tiger","WhiteHorse","Feather","River","Mountain","Imperial","Frontier","Wuling","Nanzhong")
$unitTypes = New-Object System.Collections.Generic.List[object]
for ($i = 1; $i -le 100; $i++) {
    $category = $categories[($i - 1) % $categories.Count]
    $doctrine = $doctrines[[math]::Floor(($i - 1) / 10)]
    $tier = [int]([math]::Floor(($i - 1) / 20) + 1)
    $unitTypes.Add([ordered]@{
        Id = ("sanguo_unit_type_{0:000}" -f $i)
        Name = "$doctrine $category T$tier"
        Category = $category
        Tier = $tier
        Attack = 18 + ($tier * 6) + (($i * 5) % 19)
        Defense = 14 + ($tier * 5) + (($i * 7) % 17)
        CostGold = 70 + ($tier * 35) + (($i * 11) % 90)
        CostFood = 90 + ($tier * 40) + (($i * 13) % 120)
        Supply = 1 + (($i + $tier) % 5)
        Mobility = 4 + (($i * 3) % 8)
    })
}

$template = @(
    [ordered]@{
        id = "sanguo_city"
        components = [ordered]@{
            Name = [ordered]@{ Value = "Sanguo City" }
            Team = [ordered]@{ Id = 1 }
            PlayerOwner = [ordered]@{ PlayerId = 1 }
            SelectionSelectableTag = [ordered]@{}
            SelectionSelectableState = [ordered]@{ IsEnabled = $true }
            WorldPositionCm = [ordered]@{ Value = [ordered]@{ X = 0; Y = 0 } }
            AttributeBuffer = [ordered]@{ base = [ordered]@{
                Health = 1000; Population = 25000; Troops = 5000; Food = 10000; Gold = 3000
                Production = 25; Defense = 40; Morale = 70; Supply = 80; Training = 25
                Loyalty = 70; Command = 15; TechProgress = 0
            } }
            AbilityStateBuffer = [ordered]@{ abilityIds = @(
                "Ability.Sanguo.Conscript",
                "Ability.Sanguo.TrainElite",
                "Ability.Sanguo.Develop",
                "Ability.Sanguo.Tax",
                "Ability.Sanguo.Harvest",
                "Ability.Sanguo.March",
                "Ability.Sanguo.Research",
                "Ability.Sanguo.Diplomacy"
            ) }
            GameplayTagContainer = [ordered]@{}
            TagCountContainer = [ordered]@{}
            TimedTagBuffer = [ordered]@{}
            OrderBuffer = [ordered]@{}
            BlackboardEntityBuffer = [ordered]@{}
            BlackboardIntBuffer = [ordered]@{}
        }
    }
)
Save-Json (Join-Path $assetRoot "Entities/templates.json") $template

$mapEntities = foreach ($city in $cities) {
    [ordered]@{
        InstanceId = $city.Id
        Template = "sanguo_city"
        Overrides = [ordered]@{
            Name = [ordered]@{ Value = $city.Name }
            Team = [ordered]@{ Id = $city.TeamId }
            PlayerOwner = [ordered]@{ PlayerId = $city.TeamId }
            WorldPositionCm = [ordered]@{ Value = [ordered]@{ X = $city.X; Y = $city.Y } }
            AttributeBuffer = [ordered]@{ base = [ordered]@{
                Health = 1000 + $city.Defense
                Population = $city.Population
                Troops = $city.Troops
                Food = $city.Food
                Gold = $city.Gold
                Production = $city.Production
                Defense = $city.Defense
                Morale = $city.Morale
                Supply = 70 + (($city.Production + $city.Defense) % 31)
                Training = $city.Training
                Loyalty = $city.Loyalty
                Command = 12 + (($city.Training + $city.Defense) % 44)
                TechProgress = 0
            } }
        }
    }
}

$map = [ordered]@{
    Id = "sanguo_grand_strategy_china"
    Tags = @("sanguo_grand_strategy", "grand_strategy", "china", "gas", "items", "graph")
    Metadata = [ordered]@{
        CityCount = 300
        UnitTypeCount = 100
        FactionCount = $factions.Count
        License = "CC0 original generated content"
        DataPlaneTopic = "ludots.showcase.sanguo.world"
    }
    DefaultCamera = [ordered]@{
        VirtualCameraId = "Rts"
        TargetXCm = 40000
        TargetYCm = -30000
        Yaw = 0
        Pitch = 55
        DistanceCm = 850000
        FovYDeg = 48
    }
    Entities = @($mapEntities)
}
Save-Json (Join-Path $assetRoot "Maps/sanguo_grand_strategy_china.json") $map

function New-Effect([string]$id, [object[]]$mods, [string]$presetType = "Buff", [string]$lifetime = "After", [int]$durationTicks = 1, [string[]]$tags = @()) {
    $effect = [ordered]@{
        id = $id
        tags = @($tags + $id)
        presetType = $presetType
        lifetime = $lifetime
        participatesInResponse = $false
        modifiers = @($mods)
    }
    if ($lifetime -eq "After") {
        $effect.duration = [ordered]@{ durationTicks = $durationTicks; periodTicks = 0; clockId = "FixedFrame" }
    }
    return $effect
}

function Mod([string]$attribute, [string]$op, [double]$value) {
    return [ordered]@{ attribute = $attribute; op = $op; value = $value }
}

$effects = @(
    (New-Effect "Effect.Sanguo.Conscript" @((Mod "Troops" "Add" 800),(Mod "Population" "Add" -400),(Mod "Food" "Add" -600),(Mod "Gold" "Add" -160),(Mod "Morale" "Add" -2)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Recruit")),
    (New-Effect "Effect.Sanguo.TrainElite" @((Mod "Troops" "Add" 500),(Mod "Training" "Add" 5),(Mod "Gold" "Add" -320),(Mod "Food" "Add" -260)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Recruit")),
    (New-Effect "Effect.Sanguo.Develop" @((Mod "Production" "Add" 5),(Mod "Population" "Add" 900),(Mod "Loyalty" "Add" 2),(Mod "Gold" "Add" -220)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Economy")),
    (New-Effect "Effect.Sanguo.Tax" @((Mod "Gold" "Add" 900),(Mod "Loyalty" "Add" -3),(Mod "Morale" "Add" -1)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Economy")),
    (New-Effect "Effect.Sanguo.Harvest" @((Mod "Food" "Add" 1400),(Mod "Morale" "Add" 1),(Mod "Supply" "Add" 3)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Economy")),
    (New-Effect "Effect.Sanguo.March" @((Mod "Supply" "Add" -5),(Mod "Morale" "Add" -1),(Mod "Training" "Add" 1)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Military")),
    (New-Effect "Effect.Sanguo.Research" @((Mod "TechProgress" "Add" 6),(Mod "Gold" "Add" -240),(Mod "Training" "Add" 2)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Tech")),
    (New-Effect "Effect.Sanguo.Diplomacy" @((Mod "Loyalty" "Add" 2),(Mod "Morale" "Add" 2),(Mod "Gold" "Add" -120)) "InstantDamage" "Instant" 0 @("Effect.Sanguo.Diplomacy")),
    (New-Effect "Effect.Sanguo.Item.CommandSeal" @((Mod "Command" "Add" 8),(Mod "Loyalty" "Add" 2)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item")),
    (New-Effect "Effect.Sanguo.Item.Warhorse" @((Mod "Supply" "Add" 8),(Mod "Morale" "Add" 3)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item")),
    (New-Effect "Effect.Sanguo.Item.StrategyManual" @((Mod "TechProgress" "Add" 2),(Mod "Training" "Add" 4)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item")),
    (New-Effect "Effect.Sanguo.Item.HeavyArmor" @((Mod "Defense" "Add" 10),(Mod "Morale" "Add" 1)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item")),
    (New-Effect "Effect.Sanguo.Item.EliteWeapon" @((Mod "Command" "Add" 5),(Mod "Training" "Add" 6)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item")),
    (New-Effect "Effect.Sanguo.Item.GrainLedger" @((Mod "Food" "Add" 500),(Mod "Production" "Add" 3)) "Buff" "Infinite" 0 @("Effect.Sanguo.Item"))
)
Save-Json (Join-Path $assetRoot "GAS/effects.json") $effects

$abilities = foreach ($ability in @(
    @{ Id = "Ability.Sanguo.Conscript"; Effect = "Effect.Sanguo.Conscript"; Tag = "Cooldown.Sanguo.Recruit" },
    @{ Id = "Ability.Sanguo.TrainElite"; Effect = "Effect.Sanguo.TrainElite"; Tag = "Cooldown.Sanguo.Recruit" },
    @{ Id = "Ability.Sanguo.Develop"; Effect = "Effect.Sanguo.Develop"; Tag = "Cooldown.Sanguo.Economy" },
    @{ Id = "Ability.Sanguo.Tax"; Effect = "Effect.Sanguo.Tax"; Tag = "Cooldown.Sanguo.Economy" },
    @{ Id = "Ability.Sanguo.Harvest"; Effect = "Effect.Sanguo.Harvest"; Tag = "Cooldown.Sanguo.Economy" },
    @{ Id = "Ability.Sanguo.March"; Effect = "Effect.Sanguo.March"; Tag = "Cooldown.Sanguo.Military" },
    @{ Id = "Ability.Sanguo.Research"; Effect = "Effect.Sanguo.Research"; Tag = "Cooldown.Sanguo.Tech" },
    @{ Id = "Ability.Sanguo.Diplomacy"; Effect = "Effect.Sanguo.Diplomacy"; Tag = "Cooldown.Sanguo.Diplomacy" }
)) {
    [ordered]@{
        id = $ability.Id
        exec = [ordered]@{
            clockId = "FixedFrame"
            items = @(
                [ordered]@{ kind = "TagClip"; tick = 0; duration = 18; tag = $ability.Tag },
                [ordered]@{ kind = "EffectSignal"; tick = 0; template = $ability.Effect },
                [ordered]@{ kind = "End"; tick = 0 }
            )
        }
    }
}
Save-Json (Join-Path $assetRoot "GAS/abilities.json") $abilities

$graphs = @(
    [ordered]@{
        id = "sanguo.graph.cityEconomyQuery"
        kind = "Query"
        entry = "minPopulation"
        nodes = @(
            [ordered]@{ id = "minPopulation"; op = "ConstFloat"; floatValue = 0; next = "maxPopulation" },
            [ordered]@{ id = "maxPopulation"; op = "ConstFloat"; floatValue = 999999; next = "allMapEntities" },
            [ordered]@{ id = "allMapEntities"; op = "QueryAllMapEntities"; next = "cities" },
            [ordered]@{ id = "cities"; op = "QueryFilterTemplate"; template = "sanguo_city"; next = "populationRange" },
            [ordered]@{ id = "populationRange"; op = "QueryFilterAttributeRange"; attribute = "Population"; inputs = @("minPopulation","maxPopulation"); next = "sortProduction" },
            [ordered]@{ id = "sortProduction"; op = "QuerySortByAttribute"; attribute = "Production"; descending = $true; next = "cityCount" },
            [ordered]@{ id = "cityCount"; op = "AggCount"; next = "totalPopulation" },
            [ordered]@{ id = "totalPopulation"; op = "AggSumAttribute"; attribute = "Population"; next = "totalFood" },
            [ordered]@{ id = "totalFood"; op = "AggSumAttribute"; attribute = "Food"; next = "totalGold" },
            [ordered]@{ id = "totalGold"; op = "AggSumAttribute"; attribute = "Gold"; next = "bestProductionCity" },
            [ordered]@{ id = "bestProductionCity"; op = "AggMaxEntityByAttribute"; attribute = "Production" }
        )
        outputs = @(
            [ordered]@{ id = "cities"; destination = "EntityCollection"; type = "TargetList"; collectionKey = "sanguo.collection.cities.economy"; role = "Display"; title = "Sanguo Cities"; summary = "All generated China-map cities sorted by production." },
            [ordered]@{ id = "cityCount"; destination = "Summary"; type = "Int"; source = "cityCount"; key = "sanguo.summary.cityCount" },
            [ordered]@{ id = "totalPopulation"; destination = "Summary"; type = "Float"; source = "totalPopulation"; key = "sanguo.summary.population" },
            [ordered]@{ id = "totalFood"; destination = "Summary"; type = "Float"; source = "totalFood"; key = "sanguo.summary.food" },
            [ordered]@{ id = "totalGold"; destination = "Summary"; type = "Float"; source = "totalGold"; key = "sanguo.summary.gold" },
            [ordered]@{ id = "bestProductionCity"; destination = "Summary"; type = "Entity"; source = "bestProductionCity"; key = "sanguo.summary.bestProductionCity" }
        )
    },
    [ordered]@{
        id = "sanguo.graph.weiFrontierQuery"
        kind = "Query"
        entry = "minTroops"
        nodes = @(
            [ordered]@{ id = "minTroops"; op = "ConstFloat"; floatValue = 5000; next = "maxTroops" },
            [ordered]@{ id = "maxTroops"; op = "ConstFloat"; floatValue = 100000; next = "allMapEntities" },
            [ordered]@{ id = "allMapEntities"; op = "QueryAllMapEntities"; next = "teamCities" },
            [ordered]@{ id = "teamCities"; op = "QueryFilterTeam"; teamId = 1; next = "troopRange" },
            [ordered]@{ id = "troopRange"; op = "QueryFilterAttributeRange"; attribute = "Troops"; inputs = @("minTroops","maxTroops"); next = "sortTroops" },
            [ordered]@{ id = "sortTroops"; op = "QuerySortByAttribute"; attribute = "Troops"; descending = $true; next = "cityCount" },
            [ordered]@{ id = "cityCount"; op = "AggCount"; next = "totalTroops" },
            [ordered]@{ id = "totalTroops"; op = "AggSumAttribute"; attribute = "Troops"; next = "strongestCity" },
            [ordered]@{ id = "strongestCity"; op = "AggMaxEntityByAttribute"; attribute = "Troops" }
        )
        outputs = @(
            [ordered]@{ id = "weiCities"; destination = "EntityCollection"; type = "TargetList"; collectionKey = "sanguo.collection.wei.frontier"; role = "Display"; title = "Wei Frontier Cities"; summary = "Wei-held cities with at least 5000 troops." },
            [ordered]@{ id = "cityCount"; destination = "Summary"; type = "Int"; source = "cityCount"; key = "sanguo.summary.weiFrontierCount" },
            [ordered]@{ id = "totalTroops"; destination = "Summary"; type = "Float"; source = "totalTroops"; key = "sanguo.summary.weiTroops" },
            [ordered]@{ id = "strongestCity"; destination = "Summary"; type = "Entity"; source = "strongestCity"; key = "sanguo.summary.weiStrongestCity" }
        )
    }
)
Save-Json (Join-Path $assetRoot "GAS/graphs.json") $graphs

$shapes = @(
    [ordered]@{ id = "sanguo_shape_1x1"; rows = @("X"); rotatable = $false },
    [ordered]@{ id = "sanguo_shape_1x2"; rows = @("X","X"); rotatable = $true },
    [ordered]@{ id = "sanguo_shape_2x2"; rows = @("XX","XX"); rotatable = $true }
)
Save-Json (Join-Path $assetRoot "Items/shapes.json") $shapes

$layouts = @(
    [ordered]@{
        id = "sanguo_layout_commander"
        purpose = "Equipment"
        grantsEquipmentBonuses = $true
        namedSlots = @(
            [ordered]@{ id = "weapon"; label = "Weapon"; requiredAll = @("Sanguo.Slot.Weapon") },
            [ordered]@{ id = "armor"; label = "Armor"; requiredAll = @("Sanguo.Slot.Armor") },
            [ordered]@{ id = "mount"; label = "Mount"; requiredAll = @("Sanguo.Slot.Mount") },
            [ordered]@{ id = "seal"; label = "Seal"; requiredAll = @("Sanguo.Slot.Seal") },
            [ordered]@{ id = "manual"; label = "Manual"; requiredAll = @("Sanguo.Slot.Manual") }
        )
    },
    [ordered]@{ id = "sanguo_layout_treasury"; purpose = "Stash"; width = 8; height = 6 }
)
Save-Json (Join-Path $assetRoot "Items/layouts.json") $layouts

$items = @(
    @{ id="itm_sanguo_green_dragon_blade"; name="Green Dragon Blade"; slot="weapon"; tag="Sanguo.Slot.Weapon"; effect="Effect.Sanguo.Item.EliteWeapon"; shape="sanguo_shape_1x2" },
    @{ id="itm_sanguo_serpent_spear"; name="Serpent Spear"; slot="weapon"; tag="Sanguo.Slot.Weapon"; effect="Effect.Sanguo.Item.EliteWeapon"; shape="sanguo_shape_1x2" },
    @{ id="itm_sanguo_twin_swords"; name="Twin Swords"; slot="weapon"; tag="Sanguo.Slot.Weapon"; effect="Effect.Sanguo.Item.EliteWeapon"; shape="sanguo_shape_1x2" },
    @{ id="itm_sanguo_mingguang_armor"; name="Mingguang Armor"; slot="armor"; tag="Sanguo.Slot.Armor"; effect="Effect.Sanguo.Item.HeavyArmor"; shape="sanguo_shape_2x2" },
    @{ id="itm_sanguo_liangdang_armor"; name="Liangdang Armor"; slot="armor"; tag="Sanguo.Slot.Armor"; effect="Effect.Sanguo.Item.HeavyArmor"; shape="sanguo_shape_2x2" },
    @{ id="itm_sanguo_red_hare"; name="Red Hare"; slot="mount"; tag="Sanguo.Slot.Mount"; effect="Effect.Sanguo.Item.Warhorse"; shape="sanguo_shape_2x2" },
    @{ id="itm_sanguo_white_shadow"; name="White Shadow"; slot="mount"; tag="Sanguo.Slot.Mount"; effect="Effect.Sanguo.Item.Warhorse"; shape="sanguo_shape_2x2" },
    @{ id="itm_sanguo_prime_minister_seal"; name="Prime Minister Seal"; slot="seal"; tag="Sanguo.Slot.Seal"; effect="Effect.Sanguo.Item.CommandSeal"; shape="sanguo_shape_1x1" },
    @{ id="itm_sanguo_imperial_edict"; name="Imperial Edict"; slot="seal"; tag="Sanguo.Slot.Seal"; effect="Effect.Sanguo.Item.CommandSeal"; shape="sanguo_shape_1x1" },
    @{ id="itm_sanguo_six_secret_teachings"; name="Six Secret Teachings"; slot="manual"; tag="Sanguo.Slot.Manual"; effect="Effect.Sanguo.Item.StrategyManual"; shape="sanguo_shape_1x1" },
    @{ id="itm_sanguo_art_of_war"; name="Art of War"; slot="manual"; tag="Sanguo.Slot.Manual"; effect="Effect.Sanguo.Item.StrategyManual"; shape="sanguo_shape_1x1" },
    @{ id="itm_sanguo_grain_ledger"; name="Grain Ledger"; slot="manual"; tag="Sanguo.Slot.Manual"; effect="Effect.Sanguo.Item.GrainLedger"; shape="sanguo_shape_1x1" }
) | ForEach-Object {
    [ordered]@{
        id = $_.id
        displayName = $_.name
        shape = $_.shape
        tags = @($_.tag, "Sanguo.Item")
        allowedNamedSlots = @($_.slot)
        equipEffects = @($_.effect)
    }
}
Save-Json (Join-Path $assetRoot "Items/definitions.json") $items

$scenario = New-Object System.Text.StringBuilder
[void]$scenario.AppendLine("// <auto-generated />")
[void]$scenario.AppendLine("namespace SanguoGrandStrategyMod.Runtime;")
[void]$scenario.AppendLine()
[void]$scenario.AppendLine("internal static partial class SanguoScenarioData")
[void]$scenario.AppendLine("{")
[void]$scenario.AppendLine("    public static FactionDefinition[] CreateFactions()")
[void]$scenario.AppendLine("    {")
[void]$scenario.AppendLine("        return new[]")
[void]$scenario.AppendLine("        {")
foreach ($f in $factions) {
    [void]$scenario.AppendLine("            new FactionDefinition($($f.TeamId), $(CsString $f.Id), $(CsString $f.Name), $(CsString $f.Ruler), $(CsString $f.Color), $(CsString $f.Style)),")
}
[void]$scenario.AppendLine("        };")
[void]$scenario.AppendLine("    }")
[void]$scenario.AppendLine()
[void]$scenario.AppendLine("    public static UnitTypeDefinition[] CreateUnitTypes()")
[void]$scenario.AppendLine("    {")
[void]$scenario.AppendLine("        return new[]")
[void]$scenario.AppendLine("        {")
foreach ($u in $unitTypes) {
    [void]$scenario.AppendLine("            new UnitTypeDefinition($(CsString $u.Id), $(CsString $u.Name), $(CsString $u.Category), $($u.Tier), $($u.Attack), $($u.Defense), $($u.CostGold), $($u.CostFood), $($u.Supply), $($u.Mobility)),")
}
[void]$scenario.AppendLine("        };")
[void]$scenario.AppendLine("    }")
[void]$scenario.AppendLine()
[void]$scenario.AppendLine("    public static CityDefinition[] CreateCities()")
[void]$scenario.AppendLine("    {")
[void]$scenario.AppendLine("        return new[]")
[void]$scenario.AppendLine("        {")
foreach ($c in $cities) {
    [void]$scenario.AppendLine("            new CityDefinition($(CsString $c.Id), $(CsString $c.Name), $(CsString $c.Region), $($c.TeamId), $($c.X), $($c.Y), $($c.Population), $($c.Troops), $($c.Food), $($c.Gold), $($c.Production), $($c.Defense), $($c.Morale), $($c.Loyalty), $($c.Training)),")
}
[void]$scenario.AppendLine("        };")
[void]$scenario.AppendLine("    }")
[void]$scenario.AppendLine("}")
Set-Content -Path (Join-Path $runtimeRoot "SanguoScenarioData.Generated.cs") -Value $scenario.ToString() -Encoding UTF8

Write-Host "Generated $($cities.Count) cities, $($unitTypes.Count) unit types, and Sanguo mod JSON assets."
