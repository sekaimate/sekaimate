using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class WebMeetingConnectionReadinessTests
    {
        [TestCase(false, true, true, true, true)]
        [TestCase(true, false, true, true, true)]
        [TestCase(true, true, false, true, true)]
        [TestCase(true, true, true, false, true)]
        [TestCase(true, true, true, true, false)]
        public void IsReady_RejectsIncompleteStartup(
            bool hasPendingRequest,
            bool networkInitialized,
            bool connectionPermitted,
            bool localPlayerInitialized,
            bool localPlayerSetupCompleted)
        {
            Assert.That(WebMeetingConnectionReadiness.IsReady(
                hasPendingRequest,
                networkInitialized,
                connectionPermitted,
                localPlayerInitialized,
                localPlayerSetupCompleted), Is.False);
        }

        [Test]
        public void IsReady_AllowsConnectionAfterStartupCompletes()
        {
            Assert.That(WebMeetingConnectionReadiness.IsReady(true, true, true, true, true), Is.True);
        }
    }
}
