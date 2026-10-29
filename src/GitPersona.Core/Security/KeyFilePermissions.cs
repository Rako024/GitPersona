using System.Security.AccessControl;
using System.Security.Principal;

namespace GitPersona.Core.Security;

/// <summary>
/// Windows OpenSSH refuses a private key that anyone but its owner, SYSTEM and Administrators can read
/// ("UNPROTECTED PRIVATE KEY FILE"). That happens after copying keys from elsewhere.
/// </summary>
public static class KeyFilePermissions
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>Accounts other than the current user, SYSTEM and Administrators that may read the file.</summary>
    public static IReadOnlyList<string> LooseAccess(string path)
    {
        var me = WindowsIdentity.GetCurrent().User;
        var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & FileSystemRights.ReadData) != 0
                && rule.IdentityReference is SecurityIdentifier sid
                && sid != me && sid != System && sid != Administrators)
            .Select(rule => Describe((SecurityIdentifier)rule.IdentityReference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Leaves exactly: current user, SYSTEM and Administrators with full control, nothing inherited.</summary>
    public static void Restrict(string path)
    {
        var me = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current user has no SID.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { me, System, Administrators })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        new FileInfo(path).SetAccessControl(security);
    }

    private static string Describe(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }
}
