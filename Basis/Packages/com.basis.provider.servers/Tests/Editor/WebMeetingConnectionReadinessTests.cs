using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class WebMeetingConnectionReadinessTests
    {
        [TestCase(false, true, true, true)]
        [TestCase(true, false, true, true)]
        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]
        public void IsReady_RejectsIncompleteStartup(
            bool hasPendingRequest,
            bool networkInitialized,
            bool connectionPermitted,
            bool localPlayerInitialized)
        {
            bool isReady = WebMeetingConnectionReadiness.IsReady(
                hasPendingRequest,
                networkInitialized,
                connectionPermitted,
                localPlayerInitialized);

            Assert.That(isReady, Is.False);
        }

        [Test]
        public void IsReady_AllowsConnectionAfterStartupCompletes()
        {
            bool isReady = WebMeetingConnectionReadiness.IsReady(
                hasPendingRequest: true,
                networkInitialized: true,
                connectionPermitted: true,
                localPlayerInitialized: true);

            Assert.That(isReady, Is.True);
        }
    }
}
