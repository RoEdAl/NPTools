using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NamedPipeTools.Server
{
    internal enum SecurityMode
    {
        Default = 0,
        CurrentUser,
        CurrentSession,
        LocalUsers,
        Everyone
    }

    internal enum PseudoTokenId
    {
        CurrentProcess = -4,
        CurrentThread = -5,
        CurrentThreadEffective = -6
    }

    internal class LogonId
    {
        private const uint SE_GROUP_LOGON_ID = 0xC0000000; // from winnt.h
        private const int TokenGroups = 2; // from TOKEN_INFORMATION_CLASS

        private enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        private static readonly int SID_AND_ATTRIBUTES_SIZE = Marshal.SizeOf(typeof(SID_AND_ATTRIBUTES));

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_GROUPS
        {
            public int GroupCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public SID_AND_ATTRIBUTES[] Groups;
        };

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr TokenHandle,
            TOKEN_INFORMATION_CLASS TokenInformationClass,
            IntPtr TokenInformation,
            int TokenInformationLength,
            out int ReturnLength);

        private static IntPtr GetPseudoToken(PseudoTokenId pseudoTokenId) => new IntPtr((int)pseudoTokenId);

        public static SecurityIdentifier Get() => Get(WindowsIdentity.GetCurrent().Token);

        public static SecurityIdentifier Get(PseudoTokenId pseudoTokenId) => Get(GetPseudoToken(pseudoTokenId));

        private static SecurityIdentifier Get(IntPtr token)
        {
            int tokenInfLength = 0;
            // first call gets lenght of TokenInformation
            bool tokenInfoRes = GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenGroups, IntPtr.Zero, tokenInfLength, out tokenInfLength);
            IntPtr tokenInformation = Marshal.AllocHGlobal(tokenInfLength);

            try
            {
                tokenInfoRes = GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenGroups, tokenInformation, tokenInfLength, out tokenInfLength);

                if (!tokenInfoRes)
                {
                    return null;
                }

                TOKEN_GROUPS tokenGroups = (TOKEN_GROUPS)Marshal.PtrToStructure(tokenInformation, typeof(TOKEN_GROUPS));
                IntPtr tokenGroupsGroups = IntPtr.Add(tokenInformation, IntPtr.Size); // + sizeof(GroupCount)
                for (int i = 0; i < tokenGroups.GroupCount; i++)
                {
                    SID_AND_ATTRIBUTES sidAndAttributes = (SID_AND_ATTRIBUTES)Marshal.PtrToStructure(IntPtr.Add(tokenGroupsGroups, i * SID_AND_ATTRIBUTES_SIZE), typeof(SID_AND_ATTRIBUTES));
                    if ((sidAndAttributes.Attributes & SE_GROUP_LOGON_ID) == SE_GROUP_LOGON_ID)
                    {
                        return new SecurityIdentifier(sidAndAttributes.Sid);
                    }
                }

                return null; // not found
            }
            finally
            {
                Marshal.FreeHGlobal(tokenInformation);
            }
        }
    }

    internal class Security
    {
        private static SecurityIdentifier GetSecurityIdentifier(WellKnownSidType wellKnownSidType) => new SecurityIdentifier(wellKnownSidType, null);

        private static PipeAccessRights GetPipeRights(PipeDirection pipeDirection)
        {
            switch (pipeDirection)
            {
                case PipeDirection.In:
                    return PipeAccessRights.Write | PipeAccessRights.ReadPermissions | PipeAccessRights.ReadAttributes | PipeAccessRights.ReadExtendedAttributes | PipeAccessRights.AccessSystemSecurity;

                case PipeDirection.Out:
                    return PipeAccessRights.Read | PipeAccessRights.ReadAttributes | PipeAccessRights.ReadExtendedAttributes | PipeAccessRights.AccessSystemSecurity;

                default:
                    throw new NotSupportedException("Bidirectional pipe is not supported");
            }
        }

        public static PipeSecurity Get(SecurityMode securityMode, PipeDirection pipeDirection)
        {
            if (securityMode == SecurityMode.Default)
            {
                return null;
            }

            var res = new PipeSecurity();
            // res.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));

            var creatorOwnerSid = GetSecurityIdentifier(WellKnownSidType.CreatorOwnerSid);
            res.AddAccessRule(new PipeAccessRule(creatorOwnerSid, PipeAccessRights.FullControl, AccessControlType.Allow));

            var administratorsSid = GetSecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid);
            res.AddAccessRule(new PipeAccessRule(administratorsSid, PipeAccessRights.FullControl, AccessControlType.Allow));

            switch (securityMode)
            {
                case SecurityMode.CurrentSession:
                {
                    var logonSid = LogonId.Get(PseudoTokenId.CurrentThreadEffective);
                    if (logonSid == null) logonSid = LogonId.Get();
                    if (logonSid != null)
                    {
                        res.AddAccessRule(new PipeAccessRule(logonSid, GetPipeRights(pipeDirection), AccessControlType.Allow));
                    }
                    break;
                }

                case SecurityMode.LocalUsers:
                {
                    var localSid = GetSecurityIdentifier(WellKnownSidType.LocalSid);
                    res.AddAccessRule(new PipeAccessRule(localSid, GetPipeRights(pipeDirection), AccessControlType.Allow));
                    break;
                }

                case SecurityMode.Everyone:
                {
                    var worldSid = GetSecurityIdentifier(WellKnownSidType.WorldSid);
                    res.AddAccessRule(new PipeAccessRule(worldSid, GetPipeRights(pipeDirection), AccessControlType.Allow));
                    break;
                }
            }
            return res;
        }
    }
}
