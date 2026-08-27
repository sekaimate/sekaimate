namespace Basis.BasisUI
{
    public static class WebMeetingConnectionReadiness
    {
        public static bool IsReady(
            bool hasPendingRequest,
            bool networkInitialized,
            bool connectionPermitted,
            bool localPlayerInitialized,
            bool localPlayerSetupCompleted)
        {
            return hasPendingRequest
                && networkInitialized
                && connectionPermitted
                && localPlayerInitialized
                && localPlayerSetupCompleted;
        }
    }
}
