using System.Collections.Generic;
using Basis.BasisUI;
using BasisPermissions;
using NUnit.Framework;

public sealed class BasisIndividualPlayerActionPermissionsTests
{
    [TestCase(PermNodes.ModerationKick, IndividualPlayerAdminAction.Kick)]
    [TestCase(PermNodes.ModerationBan, IndividualPlayerAdminAction.Ban)]
    [TestCase(PermNodes.ModerationIpBan, IndividualPlayerAdminAction.IpBan)]
    [TestCase(PermNodes.ModerationTeleport, IndividualPlayerAdminAction.Teleport)]
    [TestCase(PermNodes.ModerationShout, IndividualPlayerAdminAction.Shout)]
    [TestCase(PermNodes.ModerationMessage, IndividualPlayerAdminAction.Message)]
    [TestCase(PermNodes.PermissionsEdit, IndividualPlayerAdminAction.EditPermissions)]
    public void AllowsOnlyActionMatchingEffectivePermission(
        string permission,
        IndividualPlayerAdminAction expectedAction)
    {
        HashSet<string> permissions = new() { permission };

        foreach (IndividualPlayerAdminAction action in System.Enum.GetValues(typeof(IndividualPlayerAdminAction)))
        {
            Assert.AreEqual(
                action == expectedAction,
                IndividualPlayerActionPermissions.CanUse(permissions, action),
                action.ToString());
        }
    }

    [Test]
    public void PermissionsViewDoesNotAuthorizeMutatingActions()
    {
        HashSet<string> permissions = new() { PermNodes.PermissionsView };

        foreach (IndividualPlayerAdminAction action in System.Enum.GetValues(typeof(IndividualPlayerAdminAction)))
        {
            Assert.IsFalse(IndividualPlayerActionPermissions.CanUse(permissions, action), action.ToString());
        }
    }

    [Test]
    public void AnySpecificActionOrPermissionsViewMakesAdminSectionVisible()
    {
        Assert.IsTrue(IndividualPlayerActionPermissions.CanViewSection(
            new HashSet<string> { PermNodes.PermissionsView }));
        Assert.IsTrue(IndividualPlayerActionPermissions.CanViewSection(
            new HashSet<string> { PermNodes.ModerationKick }));
        Assert.IsFalse(IndividualPlayerActionPermissions.CanViewSection(new HashSet<string>()));
    }
}
