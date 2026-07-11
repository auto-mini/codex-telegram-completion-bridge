using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class WindowsAclManagerIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WindowsAclManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Protected_root_and_inherited_descendants_verify()
    {
        var sid = CurrentUserContext.Sid;

        WindowsAclManager.CreateProtectedRoot(root, sid);
        Directory.CreateDirectory(Path.Combine(root, "state"));
        File.WriteAllText(Path.Combine(root, "state", "sample.txt"), "sample");

        Assert.True(WindowsAclManager.VerifyRoot(root, sid).IsValid);
        Assert.True(WindowsAclManager.VerifyTree(root, sid).IsValid);
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

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
