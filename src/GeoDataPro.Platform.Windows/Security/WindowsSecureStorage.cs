using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using GeoDataPro.Core.Security;

namespace GeoDataPro.Platform.Windows.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsSecureStorage : FileSecureStorageBase
{
    static readonly byte[] Entropy = "6f2d1c94-8a3e-4b71-9c05-2ad7e3f18b60"u8.ToArray();

    public WindowsSecureStorage(string directory) : base(directory) { }

    public override string Kind => "dpapi";

    protected override byte[] Seal(string name, ReadOnlySpan<byte> value) =>
        ProtectedData.Protect(value.ToArray(), Combine(name), DataProtectionScope.CurrentUser);

    protected override bool TryUnseal(string name, byte[] sealedValue, out byte[] value)
    {
        try
        {
            value = ProtectedData.Unprotect(sealedValue, Combine(name), DataProtectionScope.CurrentUser);
            return true;
        }
        catch (CryptographicException)
        {
            value = Array.Empty<byte>();
            return false;
        }
    }

    protected override void Harden(string path) => FileAcl.RestrictToCurrentUser(path);

    static byte[] Combine(string name)
    {
        var suffix = System.Text.Encoding.UTF8.GetBytes(name);
        var combined = new byte[Entropy.Length + suffix.Length];
        Buffer.BlockCopy(Entropy, 0, combined, 0, Entropy.Length);
        Buffer.BlockCopy(suffix, 0, combined, Entropy.Length, suffix.Length);
        return combined;
    }
}

[SupportedOSPlatform("windows")]
public static class FileAcl
{
    public static void RestrictToCurrentUser(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;

            var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User;
            if (owner == null) return;

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                RemoveAll(security);
                security.AddAccessRule(new FileSystemAccessRule(
                    owner,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                info.SetAccessControl(security);
                return;
            }

            if (!File.Exists(path)) return;

            var file = new FileInfo(path);
            var fileSecurity = file.GetAccessControl();
            fileSecurity.SetAccessRuleProtection(true, false);
            RemoveAll(fileSecurity);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                owner,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            file.SetAccessControl(fileSecurity);
        }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
        catch (IdentityNotMappedException) { }
        catch (IOException) { }
    }

    static void RemoveAll(FileSystemSecurity security)
    {
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules) security.RemoveAccessRuleAll(rule);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformPaths : GeoDataPro.Core.Data.IPlatformPaths
{
    readonly GeoDataPro.Core.Data.DefaultPlatformPaths _inner;

    public WindowsPlatformPaths(string? root = null)
    {
        _inner = new GeoDataPro.Core.Data.DefaultPlatformPaths(root);
        Harden(_inner.Root);
        Harden(DataDirectory);
        Harden(KeyDirectory);
        Harden(BackupDirectory);
        Harden(LogDirectory);
    }

    public string DataDirectory => _inner.DataDirectory;
    public string KeyDirectory => _inner.KeyDirectory;
    public string BackupDirectory => _inner.BackupDirectory;
    public string LogDirectory => _inner.LogDirectory;

    public void Harden(string path) => FileAcl.RestrictToCurrentUser(path);
}
