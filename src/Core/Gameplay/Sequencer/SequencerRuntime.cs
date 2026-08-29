using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Sequencer
{
    public sealed record SequencerSessionSnapshot(
        string SequenceId,
        float Time,
        float Rate,
        bool Paused,
        IReadOnlyList<int> FiredSignalTrackIndices);

    public sealed class SequencerRuntime
    {
        private readonly GameEngine _engine;
        private readonly SequenceDefinitionRegistry _sequences;
        private readonly StoryDefinitionRegistry _story;
        private readonly StoryGraphInvoker _graphs;
        private readonly TaskRuntimeService _tasks;
        private readonly PresentationTextCatalog? _textCatalog;
        private readonly Ludots.Core.Presentation.PresentationDisplayResolver? _display;
        private ActiveSequenceSession? _active;

        public SequencerRuntime(
            GameEngine engine,
            SequenceDefinitionRegistry sequences,
            StoryDefinitionRegistry story,
            StoryGraphInvoker graphs,
            TaskRuntimeService tasks,
            PresentationTextCatalog? textCatalog,
            Ludots.Core.Presentation.PresentationDisplayResolver? display = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
            _story = story ?? throw new ArgumentNullException(nameof(story));
            _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _textCatalog = textCatalog;
            _display = display;
            _tasks.TaskStateChanged += HandleTaskStateChanged;
        }

        public bool HasActiveSequence => _active != null;

        public bool IsPaused => _active?.Paused ?? false;

        public void ResetState() => _active = null;

        public void Start(string sequenceId)
        {
            SequenceDefinition definition = _sequences.Require(sequenceId);
            _active = new ActiveSequenceSession(definition)
            {
                Rate = Math.Max(0.0001f, definition.Clock.Rate)
            };
            ApplyTracks(previousTime: -1f, currentTime: 0f, forceEnter: true);
        }

        public void Pause()
        {
            if (_active != null)
            {
                _active.Paused = true;
            }
        }

        public void Resume()
        {
            if (_active != null)
            {
                _active.Paused = false;
            }
        }

        public void SetRate(float rate)
        {
            if (_active == null)
            {
                return;
            }

            _active.Rate = Math.Max(0.0001f, rate);
        }

        public void Seek(float timeSeconds)
        {
            if (_active == null)
            {
                return;
            }

            float clamped = Math.Max(0f, timeSeconds);
            float previous = _active.Time;
            _active.Time = clamped;
            _active.FiredSignals.Clear();
            ApplyTracks(previous, clamped, forceEnter: true);
            if (clamped >= _active.Duration)
            {
                Complete();
            }
        }

        public void Skip()
        {
            if (_active == null)
            {
                return;
            }

            Seek(_active.Duration);
        }

        public void Update(float dt)
        {
            if (_active == null || _active.Paused)
            {
                return;
            }

            float previous = _active.Time;
            float next = previous + Math.Max(0f, dt) * _active.Rate;
            _active.Time = next;
            ApplyTracks(previous, next, forceEnter: false);
            if (next >= _active.Duration)
            {
                Complete();
            }
        }

        public bool TryGetActiveView(out SequenceView view)
        {
            if (_active == null)
            {
                view = null!;
                return false;
            }

            var subtitles = new List<SequenceSubtitleView>();
            string camera = string.Empty;
            for (int i = 0; i < _active.Definition.Tracks.Count; i++)
            {
                SequenceTrackDefinition track = _active.Definition.Tracks[i];
                if (!IsActiveAt(_active.Time, track))
                {
                    continue;
                }

                if (track.Type == SequenceTrackType.Camera)
                {
                    camera = track.Profile;
                }
                else if (track.Type == SequenceTrackType.Subtitle)
                {
                    StoryLineDefinition line = _story.RequireLine(track.LineId);
                    subtitles.Add(new SequenceSubtitleView(
                        track.LineId,
                        track.PresentationProfile,
                        ResolveLineText(track.LineId),
                        line.SpeakerId,
                        StoryTextResolution.ResolveSpeakerDisplayName(_story, _textCatalog, _display, line.SpeakerId),
                        track.Start,
                        track.Duration,
                        Math.Max(0f, _active.Time - track.Start)));
                }
            }

            view = new SequenceView(
                _active.Definition.Id,
                StoryTextResolution.FormatToken(_textCatalog, _display, _active.Definition.DisplayNameToken),
                _active.Time,
                _active.Rate,
                _active.Paused,
                Playing: !_active.Paused,
                camera,
                subtitles);
            return true;
        }

        public SequencerSessionSnapshot? CaptureSnapshot()
        {
            if (_active == null)
            {
                return null;
            }

            return new SequencerSessionSnapshot(
                _active.Definition.Id,
                _active.Time,
                _active.Rate,
                _active.Paused,
                new List<int>(_active.FiredSignals));
        }

        public void RestoreSnapshot(SequencerSessionSnapshot? snapshot)
        {
            _active = null;
            if (snapshot == null)
            {
                return;
            }

            SequenceDefinition definition = _sequences.Require(snapshot.SequenceId);
            _active = new ActiveSequenceSession(definition)
            {
                Time = snapshot.Time,
                Rate = snapshot.Rate,
                Paused = snapshot.Paused
            };
            for (int i = 0; i < snapshot.FiredSignalTrackIndices.Count; i++)
            {
                _active.FiredSignals.Add(snapshot.FiredSignalTrackIndices[i]);
            }

            ApplyTracks(previousTime: -1f, currentTime: snapshot.Time, forceEnter: true);
        }

        private void ApplyTracks(float previousTime, float currentTime, bool forceEnter)
        {
            if (_active == null)
            {
                return;
            }

            for (int i = 0; i < _active.Definition.Tracks.Count; i++)
            {
                SequenceTrackDefinition track = _active.Definition.Tracks[i];
                bool wasActive = previousTime >= 0f && IsActiveAt(previousTime, track);
                bool isActive = IsActiveAt(currentTime, track);

                if ((!wasActive && isActive) || (forceEnter && isActive))
                {
                    EnterTrack(i, track);
                }
                else if (wasActive && !isActive)
                {
                    ExitTrack(track);
                }

                if (track.Type == SequenceTrackType.Signal &&
                    !_active.FiredSignals.Contains(i) &&
                    CrossedOrAt(previousTime, currentTime, track.Start))
                {
                    FireSignal(i, track);
                }
            }
        }

        private void EnterTrack(int index, SequenceTrackDefinition track)
        {
            if (_active == null)
            {
                return;
            }

            if (track.Type == SequenceTrackType.Camera)
            {
                _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Id = track.Profile });
            }

            FireEvent(SequencerEventKeys.SectionEntered, ctx =>
            {
                ctx.Set(SequencerServiceKeys.SequenceId, _active.Definition.Id);
                ctx.Set(SequencerServiceKeys.TrackType, track.Type.ToString());
                ctx.Set(SequencerServiceKeys.LineId, track.LineId);
                ctx.Set(SequencerServiceKeys.Time, _active.Time);
                if (track.Type == SequenceTrackType.Subtitle)
                {
                    ctx.Set(SequencerServiceKeys.BodyText, ResolveLineText(track.LineId));
                }
            });
        }

        private void ExitTrack(SequenceTrackDefinition track)
        {
            if (_active == null)
            {
                return;
            }

            FireEvent(SequencerEventKeys.SectionExited, ctx =>
            {
                ctx.Set(SequencerServiceKeys.SequenceId, _active.Definition.Id);
                ctx.Set(SequencerServiceKeys.TrackType, track.Type.ToString());
                ctx.Set(SequencerServiceKeys.LineId, track.LineId);
                ctx.Set(SequencerServiceKeys.Time, _active.Time);
            });
        }

        private void FireSignal(int index, SequenceTrackDefinition track)
        {
            if (_active == null)
            {
                return;
            }

            _active.FiredSignals.Add(index);
            _graphs.ExecuteAction(track.ActionGraphId, Entity.Null);
            FireEvent(SequencerEventKeys.SignalFired, ctx =>
            {
                ctx.Set(SequencerServiceKeys.SequenceId, _active.Definition.Id);
                ctx.Set(SequencerServiceKeys.EventId, track.EventId);
                ctx.Set(SequencerServiceKeys.Time, _active.Time);
            });
        }

        private void Complete()
        {
            if (_active == null)
            {
                return;
            }

            ActiveSequenceSession completed = _active;
            if (completed.Definition.ClearCameraOnComplete)
            {
                _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
            }

            _active = null;
            FireEvent(SequencerEventKeys.Completed, ctx =>
            {
                ctx.Set(SequencerServiceKeys.SequenceId, completed.Definition.Id);
                ctx.Set(SequencerServiceKeys.Time, completed.Time);
            });
        }


        private void HandleTaskStateChanged(TaskStateChangedInfo change)
        {
            if (change.State != TaskInstanceState.Active)
            {
                return;
            }

            if (!_tasks.TryGetDefinition(change.TaskId, out TaskDefinition definition))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(definition.OnEnterSequenceId))
            {
                Start(definition.OnEnterSequenceId);
            }
        }

        private static bool IsActiveAt(float time, SequenceTrackDefinition track)
        {
            if (track.Type == SequenceTrackType.Signal)
            {
                return false;
            }

            float end = track.Start + Math.Max(0f, track.Duration);
            return time >= track.Start && time < end;
        }

        private static bool CrossedOrAt(float previous, float current, float mark)
        {
            if (previous < 0f)
            {
                return current >= mark;
            }

            return previous < mark && current >= mark;
        }

        private string ResolveLineText(string lineId)
        {
            StoryLineDefinition line = _story.RequireLine(lineId);
            return StoryTextResolution.FormatToken(_textCatalog, _display, line.TextToken, line.Args);
        }

        private void FireEvent(EventKey eventKey, Action<ScriptContext> populate)
        {
            ScriptContext context = _engine.CreateContext();
            populate(context);
            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                _engine.TriggerManager.FireMapEvent(new Ludots.Core.Map.MapId(mapId), eventKey, context);
                return;
            }

            _engine.TriggerManager.FireEvent(eventKey, context);
        }

        private sealed class ActiveSequenceSession
        {
            public ActiveSequenceSession(SequenceDefinition definition)
            {
                Definition = definition;
                Duration = ComputeDuration(definition);
                FiredSignals = new HashSet<int>();
            }

            public SequenceDefinition Definition { get; }
            public float Time { get; set; }
            public float Rate { get; set; } = 1f;
            public bool Paused { get; set; }
            public float Duration { get; }
            public HashSet<int> FiredSignals { get; }

            private static float ComputeDuration(SequenceDefinition definition)
            {
                float max = 0f;
                for (int i = 0; i < definition.Tracks.Count; i++)
                {
                    SequenceTrackDefinition track = definition.Tracks[i];
                    float end = track.Type == SequenceTrackType.Signal
                        ? track.Start
                        : track.Start + Math.Max(0f, track.Duration);
                    if (end > max)
                    {
                        max = end;
                    }
                }

                return max;
            }
        }
    }
}
