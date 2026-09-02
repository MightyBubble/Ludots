using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// SangoSimMod 冒烟:反射扫移植内核程序集,断言核心玩法对象类型已入住,并构造
    /// 最少依赖的 Person 走通字段赋值(证明非纯接口搬运)。
    /// 测试不引用 SangoSimMod 工程(csproj 不动),按 mod 主程序集产物路径加载。
    /// </summary>
    [TestFixture]
    public sealed class SangoSimSmokeTests
    {
        private static string RepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate Ludots repo root (showcase.registry.json).");
        }

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
        }

        [Test]
        public void SimAssembly_ContainsCoreGameplayTypes()
        {
            Assembly assembly = LoadSangoSimMod();

            string[] expectedTypes =
            {
                "Sango.Core.Person",
                "Sango.Core.City",
                "Sango.Core.Force",
                "Sango.Core.Troop",
                "Sango.Core.Corps",
                "Sango.Core.ItemData",
                "Sango.Core.Skill",
                "Sango.Core.Technique",
            };

            foreach (string typeName in expectedTypes)
            {
                Type? resolved = assembly.GetType(typeName, throwOnError: false);
                Assert.That(resolved, Is.Not.Null, $"SangoSimMod should contain {typeName}");
            }
        }

        [Test]
        public void SimAssembly_ContainsFrameworkKernelAndJsonConverters()
        {
            Assembly assembly = LoadSangoSimMod();

            string[] expectedTypes =
            {
                "Sango.Object",
                "Sango.Singleton`1",
                "Sango.Core.GameRandom",
                "Sango.Core.GameFormula",
                "Sango.Hexagon.Hex",
                "Sango.Core.Id2ObjConverter`1",
                "Sango.Core.XY2CellConverter",
                "Sango.Core.Scenario",
            };

            foreach (string typeName in expectedTypes)
            {
                Type? resolved = assembly.GetType(typeName, throwOnError: false);
                Assert.That(resolved, Is.Not.Null, $"SangoSimMod should contain {typeName}");
            }
        }

        [Test]
        public void Person_Instance_SupportsBasicFieldAssignment()
        {
            Assembly assembly = LoadSangoSimMod();

            Type personType = assembly.GetType("Sango.Core.Person", throwOnError: true)!;
            object person = Activator.CreateInstance(personType)!;

            PropertyInfo idProperty = personType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!;
            PropertyInfo nameProperty = personType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)!;
            PropertyInfo aliveProperty = personType.GetProperty("IsAlive", BindingFlags.Public | BindingFlags.Instance)!;

            idProperty.SetValue(person, 42);
            nameProperty.SetValue(person, "关羽");
            aliveProperty.SetValue(person, true);

            Assert.That(idProperty.GetValue(person), Is.EqualTo(42));
            Assert.That(nameProperty.GetValue(person), Is.EqualTo("关羽"));
            Assert.That(aliveProperty.GetValue(person), Is.True);
        }

        [Test]
        public void SimAssembly_VendoredJsonFork_ExposesMemberContextConverterApi()
        {
            // sango 的延迟引用绑定依赖 fork 扩展的带成员上下文 ReadJson;
            // 该 API 不存在于官方 Newtonsoft,断言 vendor 副本随 mod 产物在场。
            Assembly assembly = LoadSangoSimMod();

            string? tkPath = Path.Combine(
                Path.GetDirectoryName(assembly.Location)!, "TKNewtonsoft.dll");
            Assert.That(File.Exists(tkPath), Is.True, "TKNewtonsoft.dll should sit next to SangoSimMod.dll");

            Assembly tk = Assembly.LoadFrom(tkPath);
            Type converterType = tk.GetType("TKNewtonsoft.Json.JsonConverter", throwOnError: true)!;
            MethodInfo? readJsonWithMemberContext = converterType.GetMethod(
                "ReadJson",
                new[] { tk.GetType("TKNewtonsoft.Json.JsonReader")!, typeof(Type), typeof(object),
                    tk.GetType("TKNewtonsoft.Json.JsonSerializer")!,
                    tk.GetType("TKNewtonsoft.Json.Serialization.JsonProperty")!, typeof(object) });
            Assert.That(readJsonWithMemberContext, Is.Not.Null,
                "Vendored TKNewtonsoft must keep the 6-arg ReadJson member-context overload");
        }
    }
}
