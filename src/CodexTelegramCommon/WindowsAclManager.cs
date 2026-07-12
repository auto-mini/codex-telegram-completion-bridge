using System.Security.AccessControl;
using System.Security.Principal;

namespace CodexTelegramCommon;

public sealed record AclVerificationResult(bool IsValid, string OperationCode);

public static class WindowsAclManager
{
    private const FileSystemRights ExpectedRights = FileSystemRights.FullControl;
    private const InheritanceFlags ExpectedInheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);

    public static void CreateProtectedRoot(string path, string userSid)
    {
        CurrentUserContext.EnsureSupportedHost();
        var user = ParseSid(userSid);
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(CreateDirectoryRule(user));
        security.AddAccessRule(CreateDirectoryRule(SystemSid));
        new DirectoryInfo(path).SetAccessControl(security);
        var result = VerifyRoot(path, userSid);
        if (!result.IsValid)
        {
            throw new UnauthorizedAccessException(result.OperationCode);
        }
    }

    public static void ProtectFile(string path, string userSid)
    {
        CurrentUserContext.EnsureSupportedHost();
        var user = ParseSid(userSid);
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, ExpectedRights, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(SystemSid, ExpectedRights, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    internal static void NormalizeFileOwnership(string path, string userSid)
    {
        CurrentUserContext.EnsureSupportedHost();
        var expectedUser = ParseSid(userSid);
        var file = new FileInfo(path);
        var security = file.GetAccessControl(AccessControlSections.Owner);
        if (expectedUser.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return;
        }

        security.SetOwner(expectedUser);
        file.SetAccessControl(security);
    }

    public static void NormalizeTreeOwnership(string root, string userSid)
    {
        CurrentUserContext.EnsureSupportedHost();
        var rootResult = VerifyRoot(root, userSid);
        if (!rootResult.IsValid)
        {
            throw new UnauthorizedAccessException(rootResult.OperationCode);
        }

        var expectedUser = ParseSid(userSid);
        var entries = EnumerateTreeWithoutFollowingReparsePoints(root);
        if (entries.Any(entry => entry.IsReparsePoint))
        {
            throw new UnauthorizedAccessException("INSTALL_REPARSE_POINT_BLOCKED");
        }

        foreach (var entry in entries)
        {
            FileSystemSecurity security = entry.IsDirectory
                ? new DirectoryInfo(entry.Path).GetAccessControl(AccessControlSections.Owner)
                : new FileInfo(entry.Path).GetAccessControl(AccessControlSections.Owner);
            if (expectedUser.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            {
                continue;
            }

            security.SetOwner(expectedUser);
            if (entry.IsDirectory)
            {
                new DirectoryInfo(entry.Path).SetAccessControl((DirectorySecurity)security);
            }
            else
            {
                new FileInfo(entry.Path).SetAccessControl((FileSecurity)security);
            }
        }
    }

    public static AclVerificationResult VerifyRoot(string path, string userSid)
    {
        if (!Directory.Exists(path))
        {
            return new AclVerificationResult(false, "INSTALL_ROOT_MISSING");
        }

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return new AclVerificationResult(false, "INSTALL_REPARSE_POINT_BLOCKED");
            }

            var expectedUser = ParseSid(userSid);
            var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!expectedUser.Equals(security.GetOwner(typeof(SecurityIdentifier))) || !security.AreAccessRulesProtected)
            {
                return new AclVerificationResult(false, "INSTALL_ACL_OWNER_OR_PROTECTION_INVALID");
            }

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();
            if (rules.Length != 2 ||
                !HasExactDirectoryRule(rules, expectedUser) ||
                !HasExactDirectoryRule(rules, SystemSid))
            {
                return new AclVerificationResult(false, "INSTALL_ACL_RULES_INVALID");
            }

            return new AclVerificationResult(true, "INSTALL_ACL_OK");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or IdentityNotMappedException)
        {
            return new AclVerificationResult(false, "INSTALL_ACL_UNREADABLE");
        }
    }

    public static AclVerificationResult VerifyTree(string root, string userSid)
    {
        var rootResult = VerifyRoot(root, userSid);
        if (!rootResult.IsValid)
        {
            return rootResult;
        }

        try
        {
            var expectedUser = ParseSid(userSid);
            foreach (var entry in EnumerateTreeWithoutFollowingReparsePoints(root))
            {
                if (entry.IsReparsePoint)
                {
                    return new AclVerificationResult(false, "INSTALL_REPARSE_POINT_BLOCKED");
                }

                FileSystemSecurity security = entry.IsDirectory
                    ? new DirectoryInfo(entry.Path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                    : new FileInfo(entry.Path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                if (!expectedUser.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                {
                    return new AclVerificationResult(false, "INSTALL_DESCENDANT_OWNER_INVALID");
                }

                var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .ToArray();
                if (rules.Any(rule => rule.AccessControlType != AccessControlType.Allow ||
                                      !IsExpectedSid(rule.IdentityReference, expectedUser) ||
                                      (rule.FileSystemRights & ExpectedRights) != ExpectedRights) ||
                    !rules.Any(rule => expectedUser.Equals(rule.IdentityReference) && (rule.FileSystemRights & ExpectedRights) == ExpectedRights) ||
                    !rules.Any(rule => SystemSid.Equals(rule.IdentityReference) && (rule.FileSystemRights & ExpectedRights) == ExpectedRights))
                {
                    return new AclVerificationResult(false, "INSTALL_DESCENDANT_ACL_INVALID");
                }
            }

            return new AclVerificationResult(true, "INSTALL_ACL_OK");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or IdentityNotMappedException)
        {
            return new AclVerificationResult(false, "INSTALL_ACL_UNREADABLE");
        }
    }

    private static FileSystemAccessRule CreateDirectoryRule(SecurityIdentifier sid) => new(
        sid,
        ExpectedRights,
        ExpectedInheritance,
        PropagationFlags.None,
        AccessControlType.Allow);

    private static bool HasExactDirectoryRule(IEnumerable<FileSystemAccessRule> rules, SecurityIdentifier sid) =>
        rules.Any(rule =>
            sid.Equals(rule.IdentityReference) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & ExpectedRights) == ExpectedRights &&
            rule.InheritanceFlags == ExpectedInheritance &&
            rule.PropagationFlags == PropagationFlags.None &&
            !rule.IsInherited);

    private static bool IsExpectedSid(IdentityReference identity, SecurityIdentifier user) =>
        user.Equals(identity) || SystemSid.Equals(identity);

    private static IReadOnlyList<TreeEntry> EnumerateTreeWithoutFollowingReparsePoints(string root)
    {
        var entries = new List<TreeEntry>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                entries.Add(new TreeEntry(path, isDirectory, isReparsePoint));
                if (isDirectory && !isReparsePoint)
                {
                    pending.Push(path);
                }
            }
        }

        return entries;
    }

    private static SecurityIdentifier ParseSid(string value)
    {
        try
        {
            return new SecurityIdentifier(value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Windows user SID is invalid.", exception);
        }
    }

    private sealed record TreeEntry(string Path, bool IsDirectory, bool IsReparsePoint);
}
