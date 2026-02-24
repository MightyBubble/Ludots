using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;

namespace ModdingTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Modding System Verification Test (Launcher Architecture) ===");

            // 1. Prepare Environment (Simulate Launcher)
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            string assetsDir = FindAssetsDir(rootDir);
            
            if (assetsDir == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Could not find 'assets' directory.");
                return;
            }

            string modsPath = Path.Combine(assetsDir, "Mods");
            string testModPath = Path.Combine(modsPath, "PipelineTestMod");
            string inputPatchModPath = Path.Combine(modsPath, "InputPatchTestMod");

            // Ensure Test Mod Exists (Force Recreate)
            if (Directory.Exists(testModPath))
            {
                Directory.Delete(testModPath, true);
            }
            CreateDummyMod(testModPath);
            if (Directory.Exists(inputPatchModPath))
            {
                Directory.Delete(inputPatchModPath, true);
            }
            CreateInputPatchMod(inputPatchModPath);

            // 2. Create game.json (Launcher Responsibility)
            var gameConfig = new GameConfig
            {
                ModPaths = new List<string> { testModPath, inputPatchModPath }
            };

            string gameJsonPath = Path.Combine(rootDir, "game.json");
            File.WriteAllText(gameJsonPath, JsonSerializer.Serialize(gameConfig));
            Console.WriteLine($"[Launcher] Created game.json at {gameJsonPath}");

            // 3. Boot Game (Desktop Entry Point Responsibility)
            GameEngine engine = null;
            try
            {
                // Note: GameBootstrapper expects "assets" folder to be discoverable from baseDirectory or parents
                // But our assetsDir might be far away if we are in bin/Debug...
                // GameBootstrapper.FindAssetsRootStrict looks up parent directories.
                // If we are in bin/Debug/net8.0, and assets is in project root, it should find it.
                // Let's verify.
                
                Console.WriteLine("[Bootstrapper] Initializing...");
                var result = GameBootstrapper.InitializeFromBaseDirectory(rootDir);
                engine = result.Engine;
                
                Console.WriteLine("[Bootstrapper] Engine Initialized.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Boot Failed: {ex}");
                return;
            }

            Console.WriteLine("\n=== Verifying Input Config Pipeline ===");
            try
            {
                var merged = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
                bool hasPrimary = merged.Actions.Exists(a => a.Id == "PrimaryClick");
                bool hasCtx = merged.Contexts.Exists(c => c.Id == "Physics2D_Playground");
                Console.WriteLine($"PrimaryClick Action: {hasPrimary} (Expected True)");
                Console.WriteLine($"Physics2D_Playground Context: {hasCtx} (Expected True)");
                if (hasPrimary && hasCtx)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("SUCCESS: Input config fragments merged via ConfigPipeline.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAILURE: Input config merge did not include mod patch.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILURE: Input config verification threw: {ex}");
            }

            // 4. Verify Pipeline (Test Logic)
            Console.WriteLine("\n=== Verifying Config Pipeline ===");
            
            // Access Registry via Engine -> MapLoader
            if (engine.MapLoader != null && engine.MapLoader.TemplateRegistry != null)
            {
                var registry = engine.MapLoader.TemplateRegistry;
                var unit = registry.Get("TestUnit");
                
                if (unit != null)
                {
                    Console.WriteLine($"Unit Found: {unit.Id}");
                    
                    string GetVal(string comp, string field) 
                    {
                        if (unit.Components.TryGetValue(comp, out var node))
                            return node[field]?.ToString() ?? "null";
                        return "missing";
                    }

                    var health = GetVal("Health", "Value");
                    var mana = GetVal("Mana", "Value");
                    var name = GetVal("Name", "Value");

                    Console.WriteLine($"Health: {health} (Expected 999)");
                    Console.WriteLine($"Mana:   {mana} (Expected 50)");
                    Console.WriteLine($"Name:   {name} (Expected Base Unit)");
                    
                    if (health == "999" && mana == "50" && name == "Base Unit")
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("SUCCESS: ModLauncher Architecture & Pipeline Verified!");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("FAILURE: Data mismatch.");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAILURE: TestUnit not found in Registry.");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILURE: Engine.MapLoader or TemplateRegistry is null.");
            }

            Console.WriteLine("\n=== Verifying Camera Request Pipeline ===");
            try
            {
                var inputConfigPath = Path.Combine(assetsDir, "Configs", "Input", "default_input.json");
                var inputConfig = InputConfigLoader.LoadFromFile(inputConfigPath);
                var backend = new DummyInputBackend();
                var inputHandler = new PlayerInputHandler(backend, inputConfig);
                inputHandler.PushContext("Default_Gameplay");
                engine.GlobalContext[ContextKeys.InputHandler] = inputHandler;

                engine.GameSession.Camera.State.Yaw = 0f;
                engine.GameSession.Camera.State.Pitch = 60f;
                engine.GameSession.Camera.State.DistanceCm = 60000f;

                engine.GlobalContext[ContextKeys.CameraControllerRequest] = new CameraControllerRequest
                {
                    Id = CameraControllerIds.Orbit3C,
                    Config = new Orbit3CCameraConfig { EnablePan = true }
                };

                engine.Start();

                backend.MousePos = new Vector2(100, 100);
                backend.MiddleDown = true;
                inputHandler.Update();
                engine.Tick(0.016f);

                backend.MousePos = new Vector2(140, 100);
                inputHandler.Update();
                engine.Tick(0.016f);

                var yaw = engine.GameSession.Camera.State.Yaw;
                Console.WriteLine($"Camera Yaw After Drag: {yaw:F2} (Expected > 0)");
                if (yaw > 0.1f)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("SUCCESS: CameraControllerRequest applied and Orbit3C responds to raw input.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAILURE: Camera yaw did not change.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILURE: Camera verification threw: {ex}");
            }

            // Cleanup
            engine.Dispose();
            if (File.Exists(gameJsonPath)) File.Delete(gameJsonPath);
            // Don't delete mod files yet, might need them for debugging or subsequent runs
            
            Console.ResetColor();
            Console.WriteLine("\n=== Test Finished ===");
        }

        static string FindAssetsDir(string rootDir)
        {
            var currentDir = new DirectoryInfo(rootDir);
            while (currentDir != null)
            {
                var checkPath = Path.Combine(currentDir.FullName, "assets");
                if (Directory.Exists(checkPath))
                {
                    return checkPath;
                }
                currentDir = currentDir.Parent;
            }
            return null;
        }

        static void CreateDummyMod(string modPath)
        {
            Directory.CreateDirectory(modPath);
            File.WriteAllText(Path.Combine(modPath, "mod.json"), 
                @"{ ""name"": ""PipelineTestMod"", ""version"": ""1.0.0"" }");
            
            string entitiesDir = Path.Combine(modPath, "assets", "Entities");
            Directory.CreateDirectory(entitiesDir);
            
            File.WriteAllText(Path.Combine(entitiesDir, "templates.json"), 
                @"[
                  {
                    ""Id"": ""TestUnit"",
                    ""Components"": {
                      ""Health"": { ""Value"": 999 },
                      ""Mana"": { ""Value"": 50 },
                      ""Name"": { ""Value"": ""Base Unit"" }
                    }
                  }
                ]");
            
            Console.WriteLine($"[ModdingTest] Created template at: {Path.Combine(entitiesDir, "templates.json")}");
            Console.WriteLine($"[ModdingTest] File Exists: {File.Exists(Path.Combine(entitiesDir, "templates.json"))}");
        }

        static void CreateInputPatchMod(string modPath)
        {
            Directory.CreateDirectory(modPath);
            File.WriteAllText(Path.Combine(modPath, "mod.json"),
                @"{ ""name"": ""InputPatchTestMod"", ""version"": ""1.0.0"" }");

            string inputDir = Path.Combine(modPath, "assets", "Input");
            Directory.CreateDirectory(inputDir);

            File.WriteAllText(Path.Combine(inputDir, "default_input.json"),
                @"{
  ""actions"": [
    { ""id"": ""PrimaryClick"", ""name"": ""Gameplay_PrimaryClick"", ""type"": ""Button"" }
  ],
  ""contexts"": [
    {
      ""id"": ""Physics2D_Playground"",
      ""name"": ""Physics2D Playground"",
      ""priority"": 10,
      ""bindings"": [
        { ""actionId"": ""PrimaryClick"", ""path"": ""<Mouse>/LeftButton"" }
      ]
    }
  ]
}");
        }

        sealed class DummyInputBackend : IInputBackend
        {
            public Vector2 MousePos;
            public float Wheel;
            public bool MiddleDown;

            public float GetAxis(string devicePath) => 0f;

            public bool GetButton(string devicePath)
            {
                if (devicePath.Equals("<Mouse>/MiddleButton", StringComparison.OrdinalIgnoreCase)) return MiddleDown;
                return false;
            }

            public Vector2 GetMousePosition() => MousePos;

            public float GetMouseWheel() => Wheel;

            public void EnableIME(bool enable) { }

            public void SetIMECandidatePosition(int x, int y) { }

            public string GetCharBuffer() => "";
        }
    }
}
