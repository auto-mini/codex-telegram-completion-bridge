using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class WindowsAclManagerIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WindowsAclManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Protected_root_and_normalized_descendants_verify()
    {
        var sid = CurrentUserContext.Sid;

        WindowsAclManager.CreateProtectedRoot(root, sid);
        Directory.CreateDirectory(Path.Combine(root, "state"));
        File.WriteAllText(Path.Combine(root, "state", "sample.txt"), "sample");
        WindowsAclManager.NormalizeTreeOwnership(root, sid);

        Assert.True(WindowsAclManager.VerifyRoot(root, sid).IsValid);
        var result = WindowsAclManager.VerifyTree(root, sid);
        Assert.True(result.IsValid, result.OperationCode);
    }

    [Fact]
    public void Unexpected_explicit_rule_is_rejected()
    {
        var sid = CurrentUserContext.Sid;
        WindowsAclManager.CreateProtectedRoot(root, sid);
        var security = new DirectoryInfo(root).GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);

        Assert.False(WindowsAclManager.VerifyRoot(root, sid).IsValid);
    }

    [Fact]
    public void Ownership_normalization_does_not_hide_an_unexpected_descendant_rule()
    {
        var sid = CurrentUserContext.Sid;
        WindowsAclManager.CreateProtectedRoot(root, sid);
        var path = Path.Combine(root, "sample.txt");
        File.WriteAllText(path, "sample");
        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);

        WindowsAclManager.NormalizeTreeOwnership(root, sid);
        var result = WindowsAclManager.VerifyTree(root, sid);

        Assert.False(result.IsValid);
        Assert.Equal("INSTALL_DESCENDANT_ACL_INVALID", result.OperationCode);
    }

    [Fact]
    public void Atomic_create_and_replace_keep_the_current_user_as_owner()
    {
        var sid = CurrentUserContext.Sid;
        WindowsAclManager.CreateProtectedRoot(root, sid);
        var path = Path.Combine(root, "sample.json");

        AtomicFile.WriteUtf8(path, "first");
        AtomicFile.WriteUtf8(path, "second");
        var result = WindowsAclManager.VerifyTree(root, sid);

        Assert.Equal("second", AtomicFile.ReadUtf8(path));
        Assert.True(result.IsValid, result.OperationCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
