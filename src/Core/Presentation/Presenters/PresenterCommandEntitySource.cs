namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandEntitySource : byte
    {
        EventSource = 0,
        EventTarget = 1,

        /// <summary>
        /// Indexed member of a collection owned by the event source.
        /// Resolving the drawn owner fails closed when the store, collection,
        /// index, or member is missing. DestroyScopedPresenter does not resolve
        /// it again; the scoped key is the event subject captured at create.
        /// </summary>
        EventSourceCollectionMember = 2,
    }
}
