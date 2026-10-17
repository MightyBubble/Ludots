using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 逐单位严格对拍判据的负向用例：撤掉任何一道门（身份集合 / 结构字段 / 导航版本 / 领队路径 /
/// 逐单位位置 / 位置均值 / op 序与参数合同 / 夹具解析严格性）都必须在这里变红。
/// 抵消与错配两组用例同时算出旧口径 Σ(round(x×100)+round(y×100)) 两端相等——证明旧统计对这两类错误全盲，
/// 新判据必须红。全部用例只操作合成数据，不建会话。
/// </summary>
public sealed class CrowdFuzzTraceNegativeTests
{
    [Test]
    public void IdenticalInputs_AllGatesGreen()
    {
        var report = CompareSteps(Steps(), Steps());
        Assert.That(report.Green, Is.True, report.Describe());
        Assert.That(report.ComparedOps, Is.EqualTo(2));
        Assert.That(report.ComparedTicks, Is.EqualTo(4));
    }

    [Test]
    public void OppositePositionErrors_OldSignedStatBlind_NewGatesRed()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = WithUnit(actual[1], Unit(actual[1], 5, xm: 101));
        actual[1] = WithUnit(actual[1], Unit(actual[1], 7, xm: 199));

        Assert.That(SignedStatCm(expected), Is.EqualTo(SignedStatCm(actual)), "旧口径两端和相等（本用例的前提）");

        var report = CompareTicks(expected, actual);
        AssertGate(report, CrowdFuzzTraceGate.PositionUnit, 5, "units.x");
        AssertGate(report, CrowdFuzzTraceGate.PositionMean, null, "units.x.mean");
        Assert.That(
            report.Divergences.Single(d => d.Gate == CrowdFuzzTraceGate.PositionMean).Actual,
            Does.Contain("100"),
            "+100 与 −100 的均值必须显形");
    }

    [Test]
    public void SwappedUnitPositions_OldSignedStatBlind_NewGatesRed()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = WithUnit(actual[1], Unit(actual[1], 5, xm: 200, ym: 200));
        actual[1] = WithUnit(actual[1], Unit(actual[1], 7, xm: 100, ym: 100));

        Assert.That(SignedStatCm(expected), Is.EqualTo(SignedStatCm(actual)), "集合不变时旧口径对错配全盲（本用例的前提）");

        var report = CompareTicks(expected, actual);
        Assert.That(report.Divergences.Any(d => d.Gate == CrowdFuzzTraceGate.Identity), Is.False, "身份集合相同，身份门应绿");
        AssertGate(report, CrowdFuzzTraceGate.PositionUnit, 5, "units.x");
        AssertGate(report, CrowdFuzzTraceGate.PositionMean, null, "units.x.mean");
    }

    [Test]
    public void MissingUnit_IdentityGateRed()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = actual[1] with { Units = actual[1].Units.Where(u => u.Handle != 7).ToList() };

        var identity = AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.Identity, 7, "units.handle");
        Assert.That(identity.Expected, Is.EqualTo("7"), "Web 有、C# 缺失");
        Assert.That(identity.Actual, Is.EqualTo("缺失"));
    }

    [Test]
    public void ExtraUnit_IdentityGateRed()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = actual[1] with
        {
            Units = actual[1].Units.Append(new CrowdWebFuzzUnitTick(999, 150, 150, 0, 0, 0, 0, 0, 0)).ToList(),
        };

        var identity = AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.Identity, 999, "units.handle");
        Assert.That(identity.Expected, Is.EqualTo("缺失"));
        Assert.That(identity.Actual, Is.EqualTo("999"), "C# 多出的句柄");
    }

    [TestCase("state", 0, 1)]
    [TestCase("group", 0, 1)]
    [TestCase("player", 0, 1)]
    [TestCase("level", 0, 1)]
    [TestCase("order", 0, 1)]
    [TestCase("mode", 0, 1)]
    public void StructuralField_StrictEqual_NoTolerance(string field, int expectedValue, int mutatedValue)
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = WithUnit(actual[1], MutateStructural(Unit(actual[1], 5), field, mutatedValue));

        var divergence = AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.Structural, 5, $"units.{field}");
        Assert.That(divergence.Expected, Is.EqualTo(expectedValue.ToString()));
        Assert.That(divergence.Actual, Is.EqualTo(mutatedValue.ToString()));
    }

    [Test]
    public void NavVersionStrict_VersionBumpRed()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[0] = actual[0] with { Navs = new[] { new CrowdWebFuzzNavTick(0, 1), new CrowdWebFuzzNavTick(1, 0) } };

        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.NavVersion, null, "navs[0].version");
    }

    [TestCase(0, Description = "缺导航 id")]
    [TestCase(3, Description = "多导航 id")]
    public void NavIdSet_Red(int actualNavCount)
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        var navs = Enumerable.Range(0, actualNavCount).Select(id => new CrowdWebFuzzNavTick(id, 0)).ToList();
        actual[0] = actual[0] with { Navs = navs };

        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.NavVersion, null, "navs[");
    }

    [Test]
    public void LeaderSegStrict_Red()
    {
        var (expected, actual) = LeaderPair(l => l with { Seg = 3 });
        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "seg");
    }

    [Test]
    public void LeaderDoneStrict_Red()
    {
        var (expected, actual) = LeaderPair(l => l with { Done = true });
        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "done");
    }

    [Test]
    public void LeaderPathCountStrict_Red()
    {
        var (expected, actual) = LeaderPair(l => l with { Path = l.Path.Append(new CrowdWebFuzzPathPoint(30, 30)).ToList() });
        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "path.count");
    }

    [Test]
    public void LeaderPositionBeyondAxisBand_Red()
    {
        var (expected, actual) = LeaderPair(l => l with { Xm = l.Xm + 0.05 });
        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "leader.x");
    }

    [Test]
    public void LeaderMissingOnCsSide_Red()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = actual[1] with { Leaders = Array.Empty<CrowdWebFuzzLeaderTick>() };

        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "leaders[2#0]");
    }

    [Test]
    public void LeaderExtraOnCsSide_Red()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[1] = WithLeader(actual[1], new CrowdWebFuzzLeaderTick(9, 0, 1, 1, 0, false, TwoPoints()));

        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.LeaderPath, null, "leaders[9#0]");
    }

    [Test]
    public void PositionPerUnitBand_OneCmStrict()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        actual[0] = WithUnit(actual[0], Unit(actual[0], 5, ym: 100.008));
        Assert.That(CompareTicks(expected, actual).Green, Is.True, "0.8 cm 的差在带内");

        actual[0] = WithUnit(actual[0], Unit(actual[0], 5, ym: 100.02));
        AssertGate(CompareTicks(expected, actual), CrowdFuzzTraceGate.PositionUnit, 5, "units.y");
    }

    [Test]
    public void PositionMeanBand_EveryUnitWithinPerUnitBandButMeanStillChecked()
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        for (int i = 0; i < actual.Count; i++)
        {
            actual[i] = WithUnit(actual[i], Unit(actual[i], 5, ym: 100.006));
            actual[i] = WithUnit(actual[i], Unit(actual[i], 7, ym: 200.006));
        }

        Assert.That(CompareTicks(expected, actual).Green, Is.True, "每单位 0.6 cm 时均值 0.6 cm，两道门都绿");

        for (int i = 0; i < actual.Count; i++)
        {
            actual[i] = WithUnit(actual[i], Unit(actual[i], 5, ym: 100.05));
            actual[i] = WithUnit(actual[i], Unit(actual[i], 7, ym: 200.05));
        }

        var report = CompareTicks(expected, actual);
        var mean = report.Divergences.Single(d => d.Gate == CrowdFuzzTraceGate.PositionMean);
        Assert.That(mean.Field, Is.EqualTo("units.y.mean"));
        Assert.That(mean.Detail, Does.Contain("2 个单位"), "均值分母 = 双端都在的单位数");
    }

    [Test]
    public void OpSequence_KindMismatch_Red()
    {
        var expected = Steps(secondKind: "spawn");
        var actual = Steps(secondKind: "order");
        AssertGate(CompareSteps(expected, actual), CrowdFuzzTraceGate.OpSequence, null, "op.kind");
    }

    [Test]
    public void OpSequence_RngDrawMismatch_Red()
    {
        var expected = Steps();
        var actual = Steps();
        actual[0] = Step(rng: new uint[] { 1, 3 });
        AssertGate(CompareSteps(expected, actual), CrowdFuzzTraceGate.OpSequence, null, "op.rngDraws[1]");
    }

    [Test]
    public void OpSequence_UnitCountMismatch_Red()
    {
        var expected = Steps();
        var actual = Steps();
        actual[1] = Step(rng: new uint[] { 3 }, units: 9);
        AssertGate(CompareSteps(expected, actual), CrowdFuzzTraceGate.OpSequence, null, "units");
    }

    [Test]
    public void OpSequence_ValidateErrorsMismatch_Red()
    {
        var expected = Steps();
        expected[0] = Step(validateOk: false, errors: new[] { "甲错误" });
        var actual = Steps();
        actual[0] = Step(validateOk: false, errors: new[] { "乙错误" });
        AssertGate(CompareSteps(expected, actual), CrowdFuzzTraceGate.OpSequence, null, "validateErrors");
    }

    [Test]
    public void OpSequence_StepCountMismatch_Red()
    {
        var report = CrowdWebFuzzTraceCompare.Compare(
            Steps(), TwoUnitTicks(), Steps().Take(1).ToList(), TwoUnitTicks().Take(2).ToList());
        AssertGate(report, CrowdFuzzTraceGate.OpSequence, null, "steps.length");
    }

    [Test]
    public void OpParams_MeterFieldWithinBandGreen_BeyondBandRed()
    {
        var expected = Steps(opParams: SpawnParams(x: 100));
        var within = Steps(opParams: SpawnParams(x: 100.008));
        Assert.That(CompareSteps(expected, within).Green, Is.True, "0.8 cm 的差在带内");

        AssertGate(
            CompareSteps(expected, Steps(opParams: SpawnParams(x: 100.02))),
            CrowdFuzzTraceGate.OpParams, null, "op.params.x");
    }

    [TestCase("count", 300, 301)]
    [TestCase("player", 0, 1)]
    [TestCase("unitType", 0, 1)]
    [TestCase("rIdx", 0, 1)]
    public void OpParams_SpawnNonCoordinateFields_StrictRed(string field, int expectedValue, int mutatedValue)
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "spawn", Params = SpawnParams() };
        var actual = new CrowdWebFuzzOpRecord { Kind = "spawn", Params = SpawnParams() };
        actual.Params![field] = mutatedValue;
        Assert.That(expected.Params![field]!.GetValue<int>(), Is.EqualTo(expectedValue));

        var diffs = CrowdWebFuzzOpParamContract.Equal(expected, actual);
        Assert.That(diffs.Select(d => d.Path), Does.Contain(field), $"非坐标字段 {field} 必须严格相等");
    }

    [Test]
    public void OpParams_OrderDragBool_StrictRed()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "order", Params = OrderParams() };
        var actual = new CrowdWebFuzzOpRecord { Kind = "order", Params = OrderParams() };
        actual.Params!["drag"] = false;
        Assert.That(expected.Params!["drag"]!.GetValue<bool>(), Is.True);

        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, actual).Select(d => d.Path), Does.Contain("drag"));
    }

    [Test]
    public void OpParams_StructureTemplateString_StrictRed()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "structure", Params = PlaceParams() };
        var actual = new CrowdWebFuzzOpRecord { Kind = "structure", Params = PlaceParams() };
        actual.Params!["template"] = "other";
        Assert.That(expected.Params!["template"]!.GetValue<string>(), Is.EqualTo("building"));

        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, actual).Select(d => d.Path), Does.Contain("template"));
    }

    [Test]
    public void OpParams_StructureToArray_MeterBandedPerElement()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "structure", Params = PlaceParams() };
        var within = new CrowdWebFuzzOpRecord { Kind = "structure", Params = PlaceParams() };
        within.Params!["to"] = new JsonArray(110.008, 120.0);
        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, within), Is.Empty, "to 逐元素 1 cm 带内");

        var beyond = new CrowdWebFuzzOpRecord { Kind = "structure", Params = PlaceParams() };
        beyond.Params!["to"] = new JsonArray(111, 120.0);
        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, beyond).Select(d => d.Path), Does.Contain("to[0]"));
    }

    [Test]
    public void OpParams_ExplodeProbeRows_StrictRed()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "explode", Params = ExplodeParams() };

        var handleShifted = new CrowdWebFuzzOpRecord { Kind = "explode", Params = ExplodeParams() };
        handleShifted.Params!["probe"].AsArray()[0].AsArray()[0] = 99u;
        Assert.That(
            CrowdWebFuzzOpParamContract.Equal(expected, handleShifted).Select(d => d.Path),
            Does.Contain("probe[0][0]"));

        var hitFlipped = new CrowdWebFuzzOpRecord { Kind = "explode", Params = ExplodeParams() };
        hitFlipped.Params!["probe"].AsArray()[0].AsArray()[1] = false;
        Assert.That(
            CrowdWebFuzzOpParamContract.Equal(expected, hitFlipped).Select(d => d.Path),
            Does.Contain("probe[0][1]"));
    }

    [TestCase("expected", Description = "Web 侧带未知字段")]
    [TestCase("actual", Description = "C# 侧带未知字段")]
    public void OpParams_UnknownField_OutsideContract_Red(string side)
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "order", Params = OrderParams() };
        var actual = new CrowdWebFuzzOpRecord { Kind = "order", Params = OrderParams() };
        (side == "expected" ? expected : actual).Params!["width"] = 800;

        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, actual), Is.Not.Empty, "合同外字段不许静默通过");
    }

    [Test]
    public void OpParams_UnknownKind_Red()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "mystery", Params = new JsonObject() };
        var actual = new CrowdWebFuzzOpRecord { Kind = "mystery", Params = new JsonObject() };
        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, actual)[0].Reason, Does.Contain("未知 op 种类"));
    }

    [Test]
    public void OpParams_ErrorMessage_StringExact()
    {
        var expected = new CrowdWebFuzzOpRecord { Kind = "error", Params = new JsonObject { ["message"] = "失败" } };
        var actual = new CrowdWebFuzzOpRecord { Kind = "error", Params = new JsonObject { ["message"] = "失败 " } };
        Assert.That(CrowdWebFuzzOpParamContract.Equal(expected, actual).Select(d => d.Path), Does.Contain("message"));
    }

    [TestCaseSource(nameof(FixtureMutations))]
    public void FixtureLoad_StrictRejects(Action<JsonObject> mutate, string messageFragment)
    {
        string path = Path.Combine(Path.GetTempPath(), $"crowd-fuzz-trace-negative-{Guid.NewGuid():N}.json");
        try
        {
            var doc = MinimalFixture();
            mutate(doc);
            File.WriteAllText(path, doc.ToJsonString());
            var ex = Assert.Throws<InvalidOperationException>(() => CrowdWebFuzzTraceFixture.Load(path));
            Assert.That(ex!.Message, Does.Contain(messageFragment));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void FixtureLoad_MinimalDoc_ParsesWithFields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"crowd-fuzz-trace-negative-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, MinimalFixture().ToJsonString());
            var fixture = CrowdWebFuzzTraceFixture.Load(path);
            Assert.That(fixture.Seed, Is.EqualTo(42));
            Assert.That(fixture.Steps, Has.Count.EqualTo(1));
            Assert.That(fixture.Ticks, Has.Count.EqualTo(2));
            Assert.That(fixture.Ticks[0].Units.Select(u => u.Handle), Is.EqualTo(new uint[] { 5, 7 }).AsCollection);
            Assert.That(fixture.Ticks[0].Navs, Is.EqualTo(new[] { new CrowdWebFuzzNavTick(0, 0), new CrowdWebFuzzNavTick(1, 0) }));
            Assert.That(fixture.Ticks[0].Leaders[0].Path, Has.Count.EqualTo(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IEnumerable<TestCaseData> FixtureMutations()
    {
        yield return Case(o => o["extra"] = 1, "未知字段 extra", "Root_未知字段");
        yield return Case(o => o["paritySchema"] = "crowd-fuzz-trace-v2", "需为 crowd-fuzz-trace-v1", "Root_schema 演进必须升版");
        yield return Case(o => o["steps"].AsArray().Add(o["steps"].AsArray()[0]!.DeepClone()), "与 opCount", "Steps_长度与 opCount 不一致");
        yield return Case(o => o["ticks"].AsArray().Add(o["ticks"].AsArray()[0]!.DeepClone()), "与 opCount", "Ticks_长度与两倍 opCount 不一致");
        yield return Case(o => o["ticks"].AsArray()[1]["tick"] = 7, "连续编号", "Tick_编号不连续");
        yield return Case(o => o["ticks"].AsArray()[1]["op"] = 2, "每 op 两 tick", "Tick_op 配对错");
        yield return Case(o => o["ticks"].AsArray()[0]["extra"] = 1, "未知字段", "Tick_未知字段");
        yield return Case(o => o["steps"].AsArray()[0]["extra"] = 1, "未知字段", "Step_未知字段");
        yield return Case(o => o["blast"]["extra"] = 1, "未知字段", "Blast_未知字段");
        yield return Case(o => o["ticks"].AsArray()[0]["units"].AsArray()[0].AsArray().RemoveAt(8), "9 元数组", "UnitRow_少一元");
        yield return Case(o => o["ticks"].AsArray()[0]["units"].AsArray()[0].AsArray()[1] = null, "需为有限数值", "UnitRow_坐标为 null");
        yield return Case(o => o["ticks"].AsArray()[0]["units"].AsArray()[0].AsArray()[3] = 256, "0..255", "UnitRow_字节越界");
        yield return Case(
            o => o["ticks"].AsArray()[0]["units"].AsArray().Add(o["ticks"].AsArray()[0]["units"].AsArray()[0]!.DeepClone()),
            "句柄 5 重复", "UnitRow_句柄重复");
        yield return Case(
            o => o["ticks"].AsArray()[0]["navs"] = new JsonArray(new JsonArray(1, 0), new JsonArray(0, 0)),
            "升序且不重复", "Navs_乱序");
        yield return Case(
            o => o["ticks"].AsArray()[0]["leaders"] = new JsonArray(new JsonArray(2, 0, 1.0, 1.0, 0, false)),
            "7 元数组", "LeaderRow_少一元");
        yield return Case(o => o["contentSha256"] = new string('z', 64), "64 位十六进制", "Sha_非十六进制");
        yield return Case(o => o["contentSha256"] = "abcd", "64 位十六进制", "Sha_长度不足");
        yield return Case(o => o["templateOrder"] = new JsonArray(), "非空 id 数组", "TemplateOrder_空");
        yield return Case(o => o["initialStructures"] = new JsonArray(new JsonArray(1.0)), "两个有限数值", "InitialStructures_一元点");
        yield return Case(o => o["steps"].AsArray()[0].AsObject().Remove("validateOk"), "需为布尔值", "Step_缺 validateOk");
        yield return Case(
            o => { var s = o["steps"].AsArray()[0].AsObject(); s["validateOk"] = false; s["validateErrors"] = new JsonArray("甲", null); },
            "validateErrors[1] 需为非空字符串", "Step_null 错误项");
        yield return Case(
            o => { var s = o["steps"].AsArray()[0].AsObject(); s["validateOk"] = false; s["validateErrors"] = new JsonArray(42); },
            "validateErrors[0] 需为非空字符串", "Step_错误项为数字");
        yield return Case(
            o => { var s = o["steps"].AsArray()[0].AsObject(); s["validateOk"] = false; s["validateErrors"] = "甲错误"; },
            "需为非空字符串数组", "Step_错误项字段为字符串");
        yield return Case(
            o => { var s = o["steps"].AsArray()[0].AsObject(); s["validateOk"] = false; s["validateErrors"] = new JsonArray(); },
            "需为非空字符串数组", "Step_空错误数组");
        yield return Case(
            o => o["steps"].AsArray()[0].AsObject()["validateErrors"] = new JsonArray("甲"),
            "validateOk 为真时不得出现", "Step_成功步带错误");
        yield return Case(
            o => o["steps"].AsArray()[0].AsObject()["validateOk"] = false,
            "validateOk 为假时必须带", "Step_失败步缺错误");
        yield return Case(o => o["steps"].AsArray()[0].AsObject()["op"].AsObject()["extra"] = 1, "未知字段", "Op_未知字段");
        yield return Case(o => o["steps"].AsArray()[0].AsObject()["op"].AsObject().Remove("params"), "op.params 需为对象", "Op_缺 params");

        static TestCaseData Case(Action<JsonObject> mutate, string fragment, string name)
            => new TestCaseData(mutate, fragment).SetName(name);
    }

    private static JsonObject MinimalFixture() => new()
    {
        ["paritySchema"] = CrowdWebFuzzTraceFixture.SchemaV1,
        ["seed"] = 42,
        ["opCount"] = 1,
        ["worldSeed"] = 7,
        ["tickHz"] = 30,
        ["blast"] = new JsonObject { ["ringScale"] = 2, ["impulse"] = 4 },
        ["templateOrder"] = new JsonArray("building"),
        ["formationOrder"] = new JsonArray("line"),
        ["initialStructures"] = new JsonArray(new JsonArray(10.0, 20.0)),
        ["generatedBy"] = "negative-tests",
        ["generationCommand"] = "node scripts/exportFuzzTrace.mjs --web-src . --seed 42 --ops 1 --out x.json",
        ["contentSha256"] = new string('a', 64),
        ["steps"] = new JsonArray(new JsonObject
        {
            ["rngDraws"] = new JsonArray(1u, 2u),
            ["op"] = new JsonObject
            {
                ["kind"] = "spawn",
                ["params"] = SpawnParams(),
            },
            ["units"] = 2,
            ["validateOk"] = true,
            ["unitChecksumHex"] = "abcdef",
            ["positionStatL1Cm"] = 60000,
        }),
        ["ticks"] = new JsonArray(TickJson(1), TickJson(2)),
    };

    private static JsonNode TickJson(int tick) => new JsonObject
    {
        ["tick"] = tick,
        ["op"] = 1,
        ["units"] = new JsonArray(
            new JsonArray(5u, 100.0, 100.0, 0, 0, 0, 0, 0u, 0),
            new JsonArray(7u, 200.0, 200.0, 0, 0, 0, 0, 0u, 0)),
        ["navs"] = new JsonArray(new JsonArray(0, 0), new JsonArray(1, 0)),
        ["leaders"] = new JsonArray(new JsonArray(2, 0, 1.0, 1.0, 0, false, new JsonArray(new JsonArray(10.0, 10.0), new JsonArray(20.0, 20.0)))),
    };

    private static JsonObject SpawnParams(double x = 100) => new()
    {
        ["x"] = x,
        ["y"] = 2,
        ["count"] = 300,
        ["player"] = 0,
        ["unitType"] = 0,
        ["rIdx"] = 0,
    };

    private static JsonObject OrderParams() => new()
    {
        ["x"] = 1,
        ["y"] = 2,
        ["player"] = 0,
        ["select"] = true,
        ["ax"] = 3,
        ["ay"] = 4,
        ["drag"] = true,
    };

    private static JsonObject PlaceParams() => new()
    {
        ["action"] = "place",
        ["template"] = "building",
        ["x"] = 1,
        ["y"] = 2,
        ["size"] = 3,
        ["to"] = new JsonArray(110.0, 120.0),
    };

    private static JsonObject ExplodeParams() => new()
    {
        ["x"] = 1,
        ["y"] = 2,
        ["radius"] = 40,
        ["probe"] = new JsonArray(
            new JsonArray(5u, true),
            new JsonArray(7u, false)),
    };

    private static CrowdWebFuzzStepRecord Step(
        IReadOnlyList<uint>? rng = null, string kind = "spawn", JsonObject? opParams = null,
        int units = 2, bool validateOk = true, IReadOnlyList<string>? errors = null)
        => new()
        {
            RngDraws = rng ?? new uint[] { 1, 2 },
            Op = new CrowdWebFuzzOpRecord { Kind = kind, Params = opParams ?? SpawnParams() },
            Units = units,
            ValidateOk = validateOk,
            ValidateErrors = errors,
            UnitChecksumHex = "abcdef",
            PositionStatL1Cm = 60000,
        };

    private static List<CrowdWebFuzzStepRecord> Steps(
        JsonObject? opParams = null, string secondKind = "spawn")
        => new()
        {
            Step(opParams: opParams),
            Step(rng: new uint[] { 3 }, kind: secondKind, opParams: opParams),
        };

    private static List<CrowdWebFuzzTickTrace> TwoUnitTicks() => new()
    {
        new CrowdWebFuzzTickTrace(1, 1, TwoUnits(), TwoNavs(), OneLeader()),
        new CrowdWebFuzzTickTrace(2, 1, TwoUnits(), TwoNavs(), OneLeader()),
        new CrowdWebFuzzTickTrace(3, 2, TwoUnits(), TwoNavs(), OneLeader()),
        new CrowdWebFuzzTickTrace(4, 2, TwoUnits(), TwoNavs(), OneLeader()),
    };

    private static List<CrowdWebFuzzUnitTick> TwoUnits() => new()
    {
        new CrowdWebFuzzUnitTick(5, 100, 100, 0, 0, 0, 0, 0, 0),
        new CrowdWebFuzzUnitTick(7, 200, 200, 0, 0, 0, 0, 0, 0),
    };

    private static List<CrowdWebFuzzNavTick> TwoNavs() => new() { new(0, 0), new(1, 0) };

    private static List<CrowdWebFuzzPathPoint> TwoPoints() => new() { new(10, 10), new(20, 20) };

    private static List<CrowdWebFuzzLeaderTick> OneLeader() => new() { new(2, 0, 1, 1, 0, false, TwoPoints()) };

    private static CrowdWebFuzzUnitTick Unit(CrowdWebFuzzTickTrace tick, uint handle, double? xm = null, double? ym = null)
    {
        var unit = tick.Units.Single(u => u.Handle == handle);
        return unit with { Xm = xm ?? unit.Xm, Ym = ym ?? unit.Ym };
    }

    private static CrowdWebFuzzTickTrace WithUnit(CrowdWebFuzzTickTrace tick, CrowdWebFuzzUnitTick replacement)
        => tick with
        {
            Units = tick.Units.Select(u => u.Handle == replacement.Handle ? replacement : u).ToList(),
        };

    private static CrowdWebFuzzTickTrace WithLeader(CrowdWebFuzzTickTrace tick, CrowdWebFuzzLeaderTick leader)
        => tick with { Leaders = tick.Leaders.Append(leader).ToList() };

    private static CrowdWebFuzzUnitTick MutateStructural(CrowdWebFuzzUnitTick unit, string field, int value) => field switch
    {
        "state" => unit with { State = (byte)value },
        "group" => unit with { Group = value },
        "player" => unit with { Player = (byte)value },
        "level" => unit with { Level = (byte)value },
        "order" => unit with { Order = (uint)value },
        "mode" => unit with { Mode = (byte)value },
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static (List<CrowdWebFuzzTickTrace> Expected, List<CrowdWebFuzzTickTrace> Actual) LeaderPair(
        Func<CrowdWebFuzzLeaderTick, CrowdWebFuzzLeaderTick> mutate)
    {
        var expected = TwoUnitTicks();
        var actual = TwoUnitTicks();
        for (int i = 0; i < actual.Count; i++)
        {
            actual[i] = actual[i] with { Leaders = actual[i].Leaders.Select(mutate).ToList() };
        }

        return (expected, actual);
    }

    private static CrowdFuzzTraceReport CompareTicks(
        List<CrowdWebFuzzTickTrace> expectedTicks, List<CrowdWebFuzzTickTrace> actualTicks)
        => CrowdWebFuzzTraceCompare.Compare(Steps(), expectedTicks, Steps(), actualTicks);

    private static CrowdFuzzTraceReport CompareSteps(
        List<CrowdWebFuzzStepRecord> expectedSteps, List<CrowdWebFuzzStepRecord> actualSteps)
        => CrowdWebFuzzTraceCompare.Compare(expectedSteps, TwoUnitTicks(), actualSteps, TwoUnitTicks());

    private static CrowdFuzzTraceDivergence AssertGate(
        CrowdFuzzTraceReport report, CrowdFuzzTraceGate gate, uint? handle, string fieldFragment)
    {
        var divergence = report.Divergences.SingleOrDefault(d => d.Gate == gate);
        Assert.That(divergence, Is.Not.Null, $"{gate} 门必须红：{report.Describe()}");
        Assert.That(divergence!.Handle, Is.EqualTo(handle), $"{gate} 门报告的单位");
        Assert.That(divergence.Field, Does.Contain(fieldFragment), $"{gate} 门报告的字段");
        return divergence;
    }

    /// <summary>旧口径：Σ(round(x×100)+round(y×100))——有符号、按全体求和，抵消与错配都测不出来。</summary>
    private static long SignedStatCm(List<CrowdWebFuzzTickTrace> ticks)
        => ticks[0].Units.Sum(u => (long)Math.Round(u.Xm * 100) + (long)Math.Round(u.Ym * 100));
}
