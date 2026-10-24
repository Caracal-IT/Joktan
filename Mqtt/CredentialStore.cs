using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Joktan.Mqtt;

/// <summary>
/// Keeps broker and private key passwords in the operating system's credential store: Windows Credential
/// Manager, the macOS Keychain, or the Secret Service on Linux (through secret-tool). Where no
/// store is available nothing is saved.
/// </summary>
public abstract class CredentialStore
{
    protected const string Service = "Joktan MQTT";

    public static CredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsStore();
        if (OperatingSystem.IsMacOS())
            return new MacStore();
        if (OperatingSystem.IsLinux())
            return new LinuxStore();
        return new NoStore();
    }

    /// <summary>The account a password is filed under: one per user and broker.</summary>
    public static string AccountFor(string username, string host, string port) => $"{username}@{host}:{port}";

    /// <summary>The account the password of a certificate or private key file is filed under.</summary>
    public static string AccountForKey(string path) => $"key:{System.IO.Path.GetFullPath(path.Trim())}";

    public string? Read(string account) => Guard(() => ReadCore(account));

    public bool Write(string account, string password) => Guard(() => WriteCore(account, password));

    public void Delete(string account) => Guard(() =>
    {
        DeleteCore(account);
        return true;
    });

    protected abstract string? ReadCore(string account);

    protected abstract bool WriteCore(string account, string password);

    protected abstract void DeleteCore(string account);

    // A credential store that is missing or locked must never break the page.
    private static T? Guard<T>(Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or Win32Exception
                                      or InvalidOperationException or System.IO.IOException)
        {
            return default;
        }
    }

    private sealed class NoStore : CredentialStore
    {
        protected override string? ReadCore(string account) => null;

        protected override bool WriteCore(string account, string password) => false;

        protected override void DeleteCore(string account)
        {
        }
    }

    private sealed class WindowsStore : CredentialStore
    {
        private const uint TypeGeneric = 1;
        private const uint PersistLocalMachine = 2;

        private static string Target(string account) => $"{Service}:{account}";

        protected override string? ReadCore(string account)
        {
            if (!CredReadW(Target(account), TypeGeneric, 0, out var pointer))
                return null;

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                return credential.CredentialBlob == IntPtr.Zero
                    ? ""
                    : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        protected override bool WriteCore(string account, string password)
        {
            var blob = Encoding.Unicode.GetBytes(password);
            var buffer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
            try
            {
                Marshal.Copy(blob, 0, buffer, blob.Length);
                var credential = new Credential
                {
                    Type = TypeGeneric,
                    TargetName = Target(account),
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = buffer,
                    Persist = PersistLocalMachine,
                    UserName = account,
                };
                return CredWriteW(ref credential, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        protected override void DeleteCore(string account) => CredDeleteW(Target(account), TypeGeneric, 0);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public uint LastWrittenLow;
            public uint LastWrittenHigh;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWriteW(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDeleteW(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }

    private sealed class MacStore : CredentialStore
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        protected override string? ReadCore(string account)
        {
            if (!Find(account, out var password, out var item))
                return null;

            CFRelease(item);
            return password;
        }

        protected override bool WriteCore(string account, string password)
        {
            var data = Encoding.UTF8.GetBytes(password);
            if (Find(account, out _, out var item))
            {
                var status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data);
                CFRelease(item);
                return status == 0;
            }

            var service = Encoding.UTF8.GetBytes(Service);
            var name = Encoding.UTF8.GetBytes(account);
            return SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service,
                (uint)name.Length, name, (uint)data.Length, data, IntPtr.Zero) == 0;
        }

        protected override void DeleteCore(string account)
        {
            if (!Find(account, out _, out var item))
                return;

            SecKeychainItemDelete(item);
            CFRelease(item);
        }

        private static bool Find(string account, out string? password, out IntPtr item)
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var name = Encoding.UTF8.GetBytes(account);
            password = null;

            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service,
                (uint)name.Length, name, out var length, out var data, out item);
            if (status != 0)
                return false;

            password = length == 0 ? "" : Marshal.PtrToStringUTF8(data, (int)length);
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            return true;
        }

        [DllImport(Security)]
        private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
            uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service,
            uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes,
            uint length, byte[] data);

        [DllImport(Security)]
        private static extern int SecKeychainItemDelete(IntPtr item);

        [DllImport(Security)]
        private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr item);
    }

    private sealed class LinuxStore : CredentialStore
    {
        private const int TimeoutMilliseconds = 5000;

        protected override string? ReadCore(string account)
        {
            var (exitCode, output) = Run(null, "lookup", "service", Service, "account", account);
            return exitCode == 0 ? output : null;
        }

        protected override bool WriteCore(string account, string password)
            => Run(password, "store", $"--label={Service}", "service", Service, "account", account).ExitCode == 0;

        protected override void DeleteCore(string account)
            => Run(null, "clear", "service", Service, "account", account);

        // The password goes through standard input so it never appears in the process list.
        private static (int ExitCode, string Output) Run(string? input, params string[] arguments)
        {
            var start = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("secret-tool did not start");
            if (input is not null)
                process.StandardInput.Write(input);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                return (-1, "");
            }

            return (process.ExitCode, output);
        }
    }
}
