using System.Text.Json;
using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibInputPlaybackTests
{
    [Test]
    public void Playback_UsesOnlyAuthoredButtonsAndRequiresEveryEventToRun()
    {
        string path = WritePlayback(
            """
            {
              "version": 1,
              "events": [
                { "frame": 2, "kind": "Button", "path": "<Keyboard>/w", "pressed": true },
                { "frame": 4, "kind": "UiClick", "elementId": "station-wheel" },
                { "frame": 6, "kind": "Button", "path": "<Keyboard>/w", "pressed": false }
              ]
            }
            """);

        try
        {
            RaylibInputPlayback playback = RaylibInputPlayback.Load(path);
            var clicked = new List<string>();
            for (int frame = 0; frame <= 6; frame++)
            {
                playback.AdvanceFrame(frame, elementId =>
                {
                    clicked.Add(elementId);
                    return true;
                });

                if (frame == 2)
                {
                    Assert.That(playback.GetButton("<Keyboard>/w"), Is.True);
                    Assert.That(playback.GetButton("<Keyboard>/s"), Is.False);
                }
            }

            playback.EnsureComplete(6);
            Assert.Multiple(() =>
            {
                Assert.That(playback.GetButton("<Keyboard>/w"), Is.False);
                Assert.That(clicked, Is.EqualTo(new[] { "station-wheel" }));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Playback_RejectsUnknownFieldsAndOutOfOrderFrames()
    {
        string unknownPath = WritePlayback(
            """
            {
              "version": 1,
              "events": [
                { "frame": 1, "kind": "Marker", "label": "start", "silentFallback": true }
              ]
            }
            """);
        string unorderedPath = WritePlayback(
            """
            {
              "version": 1,
              "events": [
                { "frame": 3, "kind": "Marker", "label": "later" },
                { "frame": 2, "kind": "Marker", "label": "earlier" }
              ]
            }
            """);

        try
        {
            Assert.That(() => RaylibInputPlayback.Load(unknownPath), Throws.TypeOf<JsonException>());
            Assert.That(
                () => RaylibInputPlayback.Load(unorderedPath),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("ordered"));
        }
        finally
        {
            File.Delete(unknownPath);
            File.Delete(unorderedPath);
        }
    }

    [Test]
    public void Playback_FailsWhenTheHostSkipsAnAuthoredFrameOrClickIsUnhandled()
    {
        string path = WritePlayback(
            """
            {
              "version": 1,
              "events": [
                { "frame": 2, "kind": "UiClick", "elementId": "missing" }
              ]
            }
            """);

        try
        {
            RaylibInputPlayback missed = RaylibInputPlayback.Load(path);
            missed.AdvanceFrame(0, _ => true);
            Assert.That(
                () => missed.AdvanceFrame(3, _ => true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("missed event"));

            RaylibInputPlayback unhandled = RaylibInputPlayback.Load(path);
            unhandled.AdvanceFrame(0, _ => true);
            unhandled.AdvanceFrame(1, _ => true);
            Assert.That(
                () => unhandled.AdvanceFrame(2, _ => false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("not handled"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePlayback(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ludots-raylib-input-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
