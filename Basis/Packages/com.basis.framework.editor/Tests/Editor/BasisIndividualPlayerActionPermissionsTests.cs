using System.Collections.Generic;
using Basis.BasisUI;
using NUnit.Framework;

public sealed class BasisIndividualPlayerActionPermissionsTests
{
    [TestCase("basis.moderation.kick", IndividualPlayerAdminAction.Kick)]
    [TestCase("basis.moderation.ban", IndividualPlayerAdminAction.Ban)]
    [TestCase("basis.moderation.ipban", IndividualPlayerAdminAction.IpBan)]
    [TestCase("basis.moderation.teleport", IndividualPlayerAdminAction.Teleport)]
    [TestCase("basis.moderation.shout", IndividualPlayerAdminAction.Shout)]
    [TestCase("basis.moderation.message", IndividualPlayerAdminAction.Message)]
    [TestCase("basis.permissions.edit", IndividualPlayerAdminAction.EditPermissions)]
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
        HashSet<string> permissions = new() { "basis.permissions.view" };

        foreach (IndividualPlayerAdminAction action in System.Enum.GetValues(typeof(IndividualPlayerAdminAction)))
        {
            Assert.IsFalse(IndividualPlayerActionPermissions.CanUse(permissions, action), action.ToString());
        }
    }

    [Test]
    public void AnySpecificActionOrPermissionsViewMakesAdminSectionVisible()
    {
        Assert.IsTrue(IndividualPlayerActionPermissions.CanViewSection(
            new HashSet<string> { "basis.permissions.view" }));
        Assert.IsTrue(IndividualPlayerActionPermissions.CanViewSection(
            new HashSet<string> { "basis.moderation.kick" }));
        Assert.IsFalse(IndividualPlayerActionPermissions.CanViewSection(new HashSet<string>()));
    }
}
