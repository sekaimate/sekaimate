namespace Basis.BasisUI
{
    public static class WebMeetingConnectionReadiness
    {
        public static bool IsReady(
            bool hasPendingRequest,
            bool networkInitialized,
            bool connectionPermitted,
            bool localPlayerInitialized)
        {
            return hasPendingRequest
                && networkInitialized
                && connectionPermitted
                && localPlayerInitialized;
        }
    }
}
