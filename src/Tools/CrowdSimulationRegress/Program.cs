using System.Globalization;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Units;
using Schedulers;

namespace CrowdSimulationRegress;

/// <summary>
/// 无头回归。场景、重复次数和模糊操作表在 S4 演示 Mod 的 regress/catalog.json。
/// 地图探针仍是 CrowdSimulationMapProbe：它画地形和生成抽查，不跑这套终点比较。
/// </summary>
public static class Program
{
    private const string DefaultCatalog = "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/catalog.json";

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        bool full = false;
        string? scenariosArg = null;
        int? repeat = null;
        int? fuzzOps = null;
        long? seed = null;
        double? budget = null;
        string? catalogPath = null;
        int? fog = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--full":
                    full = true;
                    break;
                case "--scenarios":
                    scenariosArg = Need(args, ref i, "--scenarios");
                    break;
                case "--repeat":
                    repeat = int.Parse(Need(args, ref i, "--repeat"), CultureInfo.InvariantCulture);
                    break;
                case "--fuzz":
                    fuzzOps = int.Parse(Need(args, ref i, "--fuzz"), CultureInfo.InvariantCulture);
                    break;
                case "--seed":
                    seed = long.Parse(Need(args, ref i, "--seed"), CultureInfo.InvariantCulture);
                    break;
                case "--budget":
                    budget = double.Parse(Need(args, ref i, "--budget"), CultureInfo.InvariantCulture);
                    break;
                case "--catalog":
                    catalogPath = Need(args, ref i, "--catalog");
                    break;
                case "--fog":
                    fog = int.Parse(Need(args, ref i, "--fog"), CultureInfo.InvariantCulture);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("CrowdSimulationRegress [--scenarios a,b] [--full] [--repeat N] [--fuzz N] [--seed N] [--budget ms] [--catalog path] [--fog 0]");
                    return 0;
                default:
                    throw new InvalidOperationException($"未知参数 {args[i]}。");
            }
        }

        if (repeat is < 1) throw new InvalidOperationException("--repeat 需 ≥ 1。");
        if (fuzzOps is < 0) throw new InvalidOperationException("--fuzz 需 ≥ 0。");
        if (budget is < 0) throw new InvalidOperationException("--budget 需 ≥ 0。");

        string catalogFile = FindCatalog(catalogPath);
        var catalog = CrowdSimRegressCatalog.Parse(JsonNode.Parse(File.ReadAllText(catalogFile))!.AsObject());
        string repo = FindRepoRoot(catalogFile, catalog.StandardAssets);
        var ids = Select(catalog, scenariosArg, full);
        var scenarios = new List<CrowdSimRegressScenario>(ids.Count);
        foreach (var id in ids) scenarios.Add(LoadScenario(repo, catalog, id));

        int ops = fuzzOps ?? catalog.FuzzOps;
        long fuzzSeed = seed ?? catalog.Seed;
        var request = new CrowdSimRegressRequest
        {
            Scenarios = scenarios,
            Repeat = repeat ?? catalog.Repeat,
            BudgetMs = budget,
            Fuzz = ops > 0 ? catalog.CreateFuzzPlan(fuzzSeed, ops) : null,
            OpenScenario = scenario => SessionHost.Open(
                Path.Combine(repo, catalog.Scenarios[scenario.Id].SeedAssets!),
                Path.Combine(repo, catalog.StandardAssets),
                Path.Combine(repo, catalog.CapabilityAssets),
                scenario.Movement),
            OpenFuzz = ops > 0
                ? () =>
                {
                    var host = SessionHost.Open(
                        Path.Combine(repo, catalog.FuzzSeedAssets),
                        Path.Combine(repo, catalog.StandardAssets),
                        Path.Combine(repo, catalog.CapabilityAssets),
                        catalog.FuzzMovement);
                    host.Session.TruthNavFrozen = catalog.FuzzTruthNavFrozen;
                    return host;
                }
                : null,
        };

        JobScheduler? scheduler = null;
        CrowdSimRegressReport report;
        try
        {
            if (NeedsMovement(scenarios, ops > 0 && catalog.FuzzMovement))
            {
                scheduler = InstallScheduler();
            }

            report = CrowdSimRegress.Run(request);
        }
        finally
        {
            if (scheduler != null)
            {
                scheduler.Dispose();
                World.SharedJobScheduler = null;
            }
        }
        var culture = CultureInfo.InvariantCulture;
        bool failed = !report.Passed;
        if (report.Error != null) Console.WriteLine(report.Error);
        foreach (var scenario in report.Scenarios)
        {
            if (scenario.Unavailable)
            {
                Console.WriteLine($"{scenario.Id} UNAVAILABLE {scenario.Error}");
                continue;
            }

            string partitions = scenario.Differing.Count > 0
                ? string.Join(",", scenario.Differing)
                : scenario.Passed ? "match" : "-";
            Console.WriteLine(
                $"{scenario.Id} bakeMs={scenario.BakeMs.ToString("F1", culture)} avg={scenario.AvgTickMs.ToString("F3", culture)} p95={scenario.P95TickMs.ToString("F3", culture)} max={scenario.MaxTickMs.ToString("F3", culture)} checksum={CrowdSimChecksum.Format(scenario.Checksum)} partitions={partitions}");
            if (!scenario.Passed) Console.WriteLine($"{scenario.Id} FAIL {scenario.Error}");
        }

        if (ops == 0)
        {
            Console.WriteLine("fuzz off");
        }
        else if (report.Fuzz != null)
        {
            string reserved = report.Fuzz.Reserved.Count == 0 ? "-" : string.Join(",", report.Fuzz.Reserved);
            Console.WriteLine(
                $"fuzz seed={report.Fuzz.Seed} ops={ops} passed={report.Fuzz.Passed.ToString().ToLowerInvariant()} step={report.Fuzz.Step} reserved={reserved}");
            if (!report.Fuzz.Passed) Console.WriteLine($"fuzz FAIL {report.Fuzz.Error}");
        }
        else if (report.FuzzError != null)
        {
            Console.WriteLine($"fuzz FAIL {report.FuzzError}");
        }

        if (fog is > 0)
        {
            Console.WriteLine("fog FAIL UAT-FOG 要核对渲染层是否隐藏单位，这个无头入口没有这份检查。");
            failed = true;
        }
        else
        {
            Console.WriteLine("fog FOLLOW-UP UAT-FOG 要核对渲染层是否隐藏单位，这个入口没有跑。");
        }

        Console.WriteLine(failed ? "result fail" : "result pass");
        return failed ? 1 : 0;
    }

    /// <summary>
    /// 无头进程没有引擎世界。运动并行用的调度器和 GameEngine.InitializeWorld 同一份配置。
    /// 调用必须留在创建它的线程上，跑完再拆掉，否则工人线程把进程留住。
    /// </summary>
    private static JobScheduler? InstallScheduler()
    {
        if (World.SharedJobScheduler != null)
        {
            if (!World.SharedJobScheduler.IsMainThread)
            {
                throw new InvalidOperationException("World.SharedJobScheduler 属于别的线程，无法在本线程并行步进。");
            }

            return null;
        }

        var scheduler = new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = "LudotsWorker",
            ThreadCount = 0,
            MaxExpectedConcurrentJobs = 64,
            StrictAllocationMode = false,
        });
        World.SharedJobScheduler = scheduler;
        return scheduler;
    }

    private static bool NeedsMovement(IReadOnlyList<CrowdSimRegressScenario> scenarios, bool fuzzMoves)
    {
        if (fuzzMoves) return true;
        for (int i = 0; i < scenarios.Count; i++)
        {
            if (scenarios[i].Movement) return true;
        }

        return false;
    }

    private static List<string> Select(CrowdSimRegressCatalog catalog, string? scenariosArg, bool full)
    {
        var ids = new List<string>();
        if (scenariosArg != null)
        {
            foreach (var part in scenariosArg.Split(','))
            {
                string id = part.Trim();
                if (id.Length == 0) throw new InvalidOperationException("--scenarios 含空名字。");
                if (!catalog.Scenarios.ContainsKey(id)) throw new InvalidOperationException($"未知场景 {id}。");
                ids.Add(id);
            }
        }
        else
        {
            ids.AddRange(catalog.Defaults);
        }

        if (full)
        {
            for (int i = 0; i < catalog.Full.Count; i++)
            {
                if (!ids.Contains(catalog.Full[i])) ids.Add(catalog.Full[i]);
            }
        }

        if (ids.Count == 0) throw new InvalidOperationException("没有选中场景。");
        return ids;
    }

    private static CrowdSimRegressScenario LoadScenario(string repo, CrowdSimRegressCatalog catalog, string id)
    {
        var spec = catalog.Scenarios[id];
        if (spec.UnavailableReason != null)
        {
            return new CrowdSimRegressScenario { Id = id, UnavailableReason = spec.UnavailableReason };
        }

        string path = Path.Combine(repo, spec.SeedAssets!, spec.ScriptAsset!);
        var document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"{path} 根必须是对象。");
        return new CrowdSimRegressScenario
        {
            Id = id,
            Script = CrowdSimRegressCatalog.Script(document),
            Ticks = CrowdSimRegressCatalog.TickCount(document, spec.TicksFrom!),
            Movement = spec.Movement,
            TruthNavFrozen = spec.TruthNavFrozen,
        };
    }

    private static string Need(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length) throw new InvalidOperationException($"{flag} 缺少取值。");
        return args[++index];
    }

    private static string FindCatalog(string? explicitPath)
    {
        if (explicitPath != null)
        {
            if (!File.Exists(explicitPath)) throw new InvalidOperationException($"找不到场景表 {explicitPath}。");
            return Path.GetFullPath(explicitPath);
        }

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, DefaultCatalog);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"从工作目录往上找不到 {DefaultCatalog}。");
    }

    private static string FindRepoRoot(string catalogFile, string standardAssets)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(catalogFile))!);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, standardAssets))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"从 {catalogFile} 往上找不到 {standardAssets}。");
    }
}
