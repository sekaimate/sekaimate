using System.Collections.Generic;
using BasisNetworkCore.Security;

namespace Basis.BasisUI
{
    public enum IndividualPlayerAdminAction
    {
        Kick,
        Ban,
        IpBan,
        Teleport,
        Shout,
        Message,
        EditPermissions,
    }

    public static class IndividualPlayerActionPermissions
    {
        public static bool CanUse(ISet<string> permissions, IndividualPlayerAdminAction action)
        {
            if (permissions == null)
            {
                return false;
            }

            string requiredPermission = action switch
            {
                IndividualPlayerAdminAction.Kick => PermNodes.ModerationKick,
                IndividualPlayerAdminAction.Ban => PermNodes.ModerationBan,
                IndividualPlayerAdminAction.IpBan => PermNodes.ModerationIpBan,
                IndividualPlayerAdminAction.Teleport => PermNodes.ModerationTeleport,
                IndividualPlayerAdminAction.Shout => PermNodes.ModerationShout,
                IndividualPlayerAdminAction.Message => PermNodes.ModerationMessage,
                IndividualPlayerAdminAction.EditPermissions => PermNodes.PermissionsEdit,
                _ => string.Empty,
            };
            return requiredPermission.Length > 0 && permissions.Contains(requiredPermission);
        }

        public static bool CanViewSection(ISet<string> permissions)
        {
            if (permissions == null)
            {
                return false;
            }

            if (permissions.Contains(PermNodes.PermissionsView))
            {
                return true;
            }

            foreach (IndividualPlayerAdminAction action in System.Enum.GetValues(typeof(IndividualPlayerAdminAction)))
            {
                if (CanUse(permissions, action))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
