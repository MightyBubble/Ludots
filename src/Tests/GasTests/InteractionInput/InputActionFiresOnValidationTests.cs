using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Input.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class InputActionFiresOnValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Test]
    public void ChangeOnButton_FailsFastNamingTheAction()
    {
        var config = Config(new InputActionDef
        {
            Id = "Jump",
            Type = InputActionType.Button,
            FiresOn = InputActionDef.FiresOnChange,
        });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => InputConfigPipelineLoader.Validate(config, "Input/default_input.json"))!;
        Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_ACTION_FIRES_ON_INVALID"));
        Assert.That(ex.Message, Does.Contain("Jump"));
        Assert.That(ex.Message, Does.Contain("Input/default_input.json"));
    }

    [Test]
    public void ReleaseOnAxis2D_FailsFastNamingTheAction()
    {
        var config = Config(new InputActionDef
        {
            Id = "Look",
            Type = InputActionType.Axis2D,
            FiresOn = InputActionDef.FiresOnRelease,
        });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => InputConfigPipelineLoader.Validate(config, "mods/Aim/Input/default_input.json"))!;
        Assert.That(ex.Message, Does.Contain("Look"));
        Assert.That(ex.Message, Does.Contain("release"));
        Assert.That(ex.Message, Does.Contain("Button"));
    }

    [Test]
    public void PressOnAxis1D_FailsFastNamingTheAction()
    {
        var config = Config(new InputActionDef
        {
            Id = "Zoom",
            Type = InputActionType.Axis1D,
            FiresOn = InputActionDef.FiresOnPress,
        });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => InputConfigPipelineLoader.Validate(config, "Input/default_input.json"))!;
        Assert.That(ex.Message, Does.Contain("Zoom"));
    }

    [Test]
    public void ChangeOnAxis2D_AndReleaseOnButton_Load()
    {
        var config = Config(
            new InputActionDef
            {
                Id = "Aim.Stick",
                Type = InputActionType.Axis2D,
                FiresOn = InputActionDef.FiresOnChange,
            },
            new InputActionDef
            {
                Id = "Aim.Confirm",
                Type = InputActionType.Button,
                FiresOn = InputActionDef.FiresOnRelease,
            });

        InputConfigPipelineLoader.Validate(config, "Input/default_input.json");

        Assert.That(config.Actions[0].FiresOn, Is.EqualTo(InputActionDef.FiresOnChange));
        Assert.That(config.Actions[1].FiresOn, Is.EqualTo(InputActionDef.FiresOnRelease));
    }

    [Test]
    public void OmittedFiresOn_ButtonBecomesPress_OtherTypesStayBlank()
    {
        const string json = """
            {
              "actions": [
                { "id": "Confirm", "type": "Button" },
                { "id": "Move", "type": "Axis2D" },
                { "id": "Zoom", "type": "Axis1D" },
                { "id": "Look3", "type": "Axis3D" }
              ]
            }
            """;
        InputConfigRoot config = JsonSerializer.Deserialize<InputConfigRoot>(json, JsonOptions)!;

        InputConfigPipelineLoader.Validate(config, "Input/default_input.json");

        Assert.That(config.Actions[0].FiresOn, Is.EqualTo(InputActionDef.FiresOnPress));
        Assert.That(config.Actions[1].FiresOn, Is.EqualTo(string.Empty));
        Assert.That(config.Actions[2].FiresOn, Is.EqualTo(string.Empty));
        Assert.That(config.Actions[3].FiresOn, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ExistingInputConfigs_StillLoad()
    {
        string repoRoot = FindRepoRoot();
        string[] files = Directory.GetFiles(repoRoot, "default_input.json", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty);
        var failures = new List<string>();
        foreach (string file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(file);
                InputConfigRoot? config = JsonSerializer.Deserialize<InputConfigRoot>(json, JsonOptions);
                if (config == null)
                {
                    failures.Add($"{file}: deserialized null");
                    continue;
                }

                InputConfigPipelineLoader.Validate(config, file);
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    private static InputConfigRoot Config(params InputActionDef[] actions)
    {
        return new InputConfigRoot { Actions = new List<InputActionDef>(actions) };
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
