namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandEntitySource : byte
    {
        EventSource = 0,
        EventTarget = 1,

        /// <summary>
        /// Indexed member of a collection owned by the event source.
        /// A missing store, collection, index, or a dead member fails closed.
        /// </summary>
        EventSourceCollectionMember = 2,
    }
}
