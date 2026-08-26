using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Sequencer
{
    public static class SequencerEventKeys
    {
        public static readonly EventKey SectionEntered = new("Sequencer.SectionEntered");
        public static readonly EventKey SectionExited = new("Sequencer.SectionExited");
        public static readonly EventKey SignalFired = new("Sequencer.SignalFired");
        public static readonly EventKey Completed = new("Sequencer.Completed");
    }

    public static class SequencerServiceKeys
    {
        public static readonly ServiceKey<string> SequenceId = new("Sequencer.SequenceId");
        public static readonly ServiceKey<string> TrackType = new("Sequencer.TrackType");
        public static readonly ServiceKey<string> EventId = new("Sequencer.EventId");
        public static readonly ServiceKey<string> LineId = new("Sequencer.LineId");
        public static readonly ServiceKey<string> BodyText = new("Sequencer.BodyText");
        public static readonly ServiceKey<float> Time = new("Sequencer.Time");
    }
}
