using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AbilityExecLoaderFailFastTests
    {
        [SetUp]
        public void SetUp()
        {
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        [Test]
        public void Load_MissingExecBlock_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                WriteAbilities(root,
                    """
                    [
                      {
                        "id": "Ability.Test.MissingExec"
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<AggregateException>(() => loader.Load(CreateAbilitiesCatalog(), relativePath: "GAS/abilities.json"));

                That(ex!.Flatten().InnerExceptions[0].Message, Does.Contain("exec"));
                That(ex.Flatten().InnerExceptions[0].Message, Does.Contain("GAS/abilities.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_RemovedOnActivateEffectsField_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                WriteAbilities(root,
                    """
                    [
                      {
                        "id": "Ability.Test.UnknownEffect",
                        "onActivateEffects": ["Effect.Test.Missing"],
                        "exec": {
                          "clockId": "FixedFrame",
                          "items": [
                            { "kind": "End", "tick": 0 }
                          ]
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<AggregateException>(() => loader.Load(CreateAbilitiesCatalog(), relativePath: "GAS/abilities.json"));

                That(ex!.Flatten().InnerExceptions[0].Message, Does.Contain("onActivateEffects"));
                That(ex.Flatten().InnerExceptions[0].Message, Does.Contain("author effects once"));
                That(ex.Flatten().InnerExceptions[0].Message, Does.Contain("EffectSignal or EffectClip"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void CompileAbility_MissingClockId_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.clockId"));
        }

        [Test]
        public void CompileAbility_NonObjectTimelineItem_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [42]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0]"));
            That(ex.Message, Does.Contain("object"));
        }

        [Test]
        public void CompileAbility_TooManyTimelineItems_IsRejected()
        {
            string items = string.Join(",\n", Enumerable.Range(0, 17).Select(i => $@"{{ ""kind"": ""End"", ""tick"": {i} }}"));
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    $$"""
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          {{items}}
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items"));
            That(ex.Message, Does.Contain("max 16"));
        }

        [Test]
        public void CompileAbility_BlankBlockTag_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "blockTags": {
                        "requiredAll": [""]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("blockTags.requiredAll[0]"));
            That(ex.Message, Does.Contain("non-empty"));
        }

        [Test]
        public void CompileAbility_BlankInterruptTag_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "interruptAny": [""],
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.interruptAny[0]"));
            That(ex.Message, Does.Contain("non-empty"));
        }

        [Test]
        public void CompileAbility_TimelineItemMissingTick_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End" }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0].tick"));
        }

        [Test]
        public void CompileAbility_TimelineItemMissingKind_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "tick": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0].kind"));
            That(ex.Message, Does.Contain("required"));
        }

        [Test]
        public void CompileAbility_InputGateMissingPayload_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "InputGate", "tick": 0, "tag": "Input.Confirm" }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("payloadA"));
            That(ex.Message, Does.Contain("InputGate"));
        }

        [Test]
        public void CompileAbility_EventGateWithTimeoutPayload_IsAccepted()
        {
            var ability = Compile(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "EventGate", "tick": 0, "payloadA": 180 }
                    ]
                  }
                }
                """);

            That(ability.ExecSpec.GetPayloadA(0), Is.EqualTo(180));
        }

        [Test]
        public void CompileAbility_CallerParamsEntryMustBeObject()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "callerParams": [
                          { "entries": [12] }
                        ],
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.callerParams[0].entries[0]"));
            That(ex.Message, Does.Contain("object"));
        }

        [Test]
        public void CompileAbility_CallerParamsEntryCapacityOverflow_IsRejected()
        {
            string entries = string.Join(
                ",",
                Enumerable.Range(0, Ludots.Core.Gameplay.GAS.Components.EffectConfigParams.MAX_PARAMS + 1)
                    .Select(i => $$"""{"key":"param.{{i}}","value":{{i}}}"""));

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    $$"""
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "callerParams": [
                          { "entries": [{{entries}}] }
                        ],
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.callerParams[0].entries"));
            That(ex.Message, Does.Contain("exceeded max"));
        }

        [Test]
        public void CompileAbility_CallerParamsDuplicateKey_IsRejectedAtLoadTime()
        {
            EffectTemplateIdRegistry.Register("Effect.Tests.Instant");

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "callerParams": [
                          {
                            "entries": [
                              { "key": "param.damage", "type": "Int", "value": 10 },
                              { "key": "param.damage", "type": "Int", "value": 20 }
                            ]
                          }
                        ],
                        "items": [
                          { "kind": "EffectSignal", "tick": 0, "template": "Effect.Tests.Instant", "callerParamsIdx": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.callerParams[0].entries[1].key"));
            That(ex.Message, Does.Contain("duplicates"));
            That(ex.Message, Does.Contain("exec.callerParams[0].entries[0].key"));
        }

        [Test]
        public void CompileAbility_CallerParamsTypedEffectTemplate_IsResolved()
        {
            EffectParamKeys.Initialize();
            int payloadTemplateId = EffectTemplateIdRegistry.Register("Effect.Payload.Override");

            var ability = Compile(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "callerParams": [
                      {
                        "entries": [
                          { "key": "_ep.payloadEffectId", "type": "EffectTemplate", "value": "Effect.Payload.Override" },
                          { "key": "_ep.durationTicks", "type": "Int", "value": 45 }
                        ]
                      }
                    ],
                    "items": [
                      { "kind": "EffectSignal", "tick": 0, "template": "Effect.Payload.Override", "callerParamsIdx": 0 }
                    ]
                  }
                }
                """);

            That(ability.HasExecCallerParamsPool, Is.True);
            That(ability.ExecSpec.GetCallerParamsIdx(0), Is.EqualTo(0));

            ref readonly EffectConfigParams callerParams = ref ability.ExecCallerParamsPool.Get(0);
            That(callerParams.TryGetRawValue(EffectParamKeys.PayloadEffectId, out ConfigParamType payloadType, out int payloadValue), Is.True);
            That(payloadType, Is.EqualTo(ConfigParamType.EffectTemplateId));
            That(payloadValue, Is.EqualTo(payloadTemplateId));

            That(callerParams.TryGetRawValue(EffectParamKeys.DurationTicks, out ConfigParamType durationType, out int durationValue), Is.True);
            That(durationType, Is.EqualTo(ConfigParamType.Int));
            That(durationValue, Is.EqualTo(45));
        }

        [Test]
        public void CompileAbility_EffectClipRequiresDurationTicks()
        {
            EffectTemplateIdRegistry.Register("Effect.Tests.Clip");

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "Step",
                        "items": [
                          { "kind": "EffectClip", "tick": 0, "template": "Effect.Tests.Clip", "clockId": "Step" }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0].durationTicks"));
            That(ex.Message, Does.Contain("EffectClip"));
        }

        [Test]
        public void CompileAbility_EffectClipRejectsDuplicateDurationCallerParam()
        {
            EffectParamKeys.Initialize();
            EffectTemplateIdRegistry.Register("Effect.Tests.Clip");

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "Step",
                        "callerParams": [
                          {
                            "entries": [
                              { "key": "_ep.durationTicks", "type": "Int", "value": 5 }
                            ]
                          }
                        ],
                        "items": [
                          { "kind": "EffectClip", "tick": 0, "template": "Effect.Tests.Clip", "durationTicks": 3, "clockId": "Step", "callerParamsIdx": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0].durationTicks"));
            That(ex.Message, Does.Contain("exec.callerParams[0]"));
            That(ex.Message, Does.Contain("_ep.durationTicks"));
        }

        [Test]
        public void CompileAbility_EffectClipRejectsCallerParamsWithoutRoomForDuration()
        {
            EffectTemplateIdRegistry.Register("Effect.Tests.Clip");
            string entries = string.Join(
                ",",
                Enumerable.Range(0, EffectConfigParams.MAX_PARAMS)
                    .Select(i => $$"""{"key":"param.{{i}}","type":"Int","value":{{i}}}"""));

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    $$"""
                    {
                      "exec": {
                        "clockId": "Step",
                        "callerParams": [
                          {
                            "entries": [{{entries}}]
                          }
                        ],
                        "items": [
                          { "kind": "EffectClip", "tick": 0, "template": "Effect.Tests.Clip", "durationTicks": 3, "clockId": "Step", "callerParamsIdx": 0 }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("exec.items[0].durationTicks"));
            That(ex.Message, Does.Contain("exec.callerParams[0]"));
            That(ex.Message, Does.Contain("capacity"));
        }

        [Test]
        public void CompileAbility_GraphSignal_IsRejectedAsUnknownExecutionKind()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "GraphSignal", "tick": 0, "graph": "Graph.Missing" }
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("Unknown ExecItemKind 'GraphSignal'"));
            That(ex.Message, Does.Not.Contain("Graph.Missing"));
        }

        [Test]
        public void CompileAbility_LegacyIndicatorField_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Circle",
                        "range": 500,
                        "radius": 120,
                        "showRangeCircle": true,
                        "validColor": "#XXCC66",
                        "invalidColor": "#FF3333",
                        "rangeCircleColor": "#3366FF"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_CircleIndicatorWithoutRangeCircle_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Circle",
                        "radius": 120,
                        "showRangeCircle": false,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_IndicatorStateColorsAreRejectedWithLegacyField()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Single",
                        "range": 500,
                        "showRangeCircle": true,
                        "rangeCircleColor": "#3366FF"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_OptionalIndicatorRangeZero_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Circle",
                        "range": 0,
                        "radius": 120,
                        "showRangeCircle": false,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_SingleIndicatorRadius_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Single",
                        "range": 500,
                        "radius": 80,
                        "showRangeCircle": true,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333",
                        "rangeCircleColor": "#3366FF"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_RangeCircleRequiresColor()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Circle",
                        "range": 500,
                        "radius": 120,
                        "showRangeCircle": true,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_LineIndicatorRequiresPositiveRadiusAsWidth()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Line",
                        "range": 500,
                        "radius": 0,
                        "showRangeCircle": false,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_RangeCircleColorWithoutRangeCircle_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "indicator": {
                        "shape": "Circle",
                        "radius": 120,
                        "showRangeCircle": false,
                        "validColor": "#33CC66",
                        "invalidColor": "#FF3333",
                        "rangeCircleColor": "#3366FF"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("field 'indicator': declare gameplay targeting"));
        }

        [Test]
        public void CompileAbility_TooManyToggleActiveEffects_IsRejected()
        {
            for (int i = 0; i < 5; i++)
            {
                EffectTemplateIdRegistry.Register($"Effect.Toggle.{i}");
            }

            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "toggleSpec": {
                        "toggleTag": "State.Toggle",
                        "activeEffects": [
                          "Effect.Toggle.0",
                          "Effect.Toggle.1",
                          "Effect.Toggle.2",
                          "Effect.Toggle.3",
                          "Effect.Toggle.4"
                        ]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("toggleSpec.activeEffects"));
            That(ex.Message, Does.Contain("max 4"));
        }

        [Test]
        public void CompileAbility_BlankToggleActiveEffect_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "toggleSpec": {
                        "toggleTag": "State.Toggle",
                        "activeEffects": [""]
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("toggleSpec.activeEffects[0]"));
            That(ex.Message, Does.Contain("non-empty"));
        }

        [Test]
        public void CompileAbility_LegacyToggleTagField_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "toggleSpec": {
                        "tag": "State.Toggle"
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("toggleSpec"));
            That(ex.Message, Does.Contain("tag"));
            That(ex.Message, Does.Contain("toggleTag"));
        }

        [Test]
        public void CompileAbility_UnknownPresentationModeHint_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "presentation": {
                        "modeHints": {
                          "SmartCastTypo": "bad mode"
                        }
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("presentation.modeHints.SmartCastTypo"));
            That(ex.Message, Does.Contain("unknown interaction mode"));
        }

        [Test]
        public void CompileAbility_BlankPresentationModeGlyph_IsRejected()
        {
            var ex = Throws<InvalidOperationException>(() =>
                Compile(
                    """
                    {
                      "exec": {
                        "clockId": "FixedFrame",
                        "items": [
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "presentation": {
                        "modeIconGlyphs": {
                          "SmartCast": ""
                        }
                      }
                    }
                    """));

            That(ex!.Message, Does.Contain("presentation.modeIconGlyphs.SmartCast"));
            That(ex.Message, Does.Contain("non-empty string"));
        }

        private static AbilityExecLoader CreateLoader(string root, out AbilityDefinitionRegistry registry)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);

            registry = new AbilityDefinitionRegistry();
            return new AbilityExecLoader(pipeline, registry);
        }

        private static ConfigCatalog CreateAbilitiesCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/abilities.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private static AbilityDefinition Compile(string json)
        {
            var obj = JsonNode.Parse(json)!.AsObject();
            return AbilityExecLoader.CompileAbility(obj, "Ability.Test.Strict", "GAS/abilities.json");
        }

        private static void WriteAbilities(string root, string json)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
            File.WriteAllText(Path.Combine(root, "Configs", "GAS", "abilities.json"), json);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AbilityExecLoaderFailFastTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
