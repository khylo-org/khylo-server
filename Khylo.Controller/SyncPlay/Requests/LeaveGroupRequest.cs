using Khylo.Model.SyncPlay;

namespace Khylo.Controller.SyncPlay.Requests
{
    /// <summary>
    /// Class LeaveGroupRequest.
    /// </summary>
    public class LeaveGroupRequest : ISyncPlayRequest
    {
        /// <inheritdoc />
        public RequestType Type { get; } = RequestType.LeaveGroup;
    }
}
