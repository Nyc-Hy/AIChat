using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AIChat.Storage.Json;

internal readonly record struct ProtectedSecret(string Value, string Protection);

internal interface ISecretProtector
{
    ProtectedSecret Protect(string secret, string purpose);
    string Unprotect(string protectedValue, string protection, string purpose);
    void Delete(string purpose);
}

// Used only by an explicit isolated runtime profile. Secrets remain usable in
// memory for that process, but no plaintext or credential-vault entry is
// written. A copied production settings file therefore cannot trigger a vault
// read while a UX test is running.
internal sealed class SessionOnlySecretProtector : ISecretProtector
{
    public ProtectedSecret Protect(string secret, string purpose)
        => new("", PlatformSecretProtector.SessionOnly);

    public string Unprotect(string protectedValue, string protection, string purpose) => "";

    public void Delete(string purpose)
    {
    }
}

// Uses the operating system's credential vault without adding a third-party
// dependency. Unsupported Linux desktops fail closed: the key remains usable
// for the current process but no plaintext value is written to settings.json.
internal sealed class PlatformSecretProtector : ISecretProtector
{
    internal const string MacOsKeychain = "macos-keychain";
    internal const string LinuxSecretService = "linux-secret-service";
    internal const string SessionOnly = "session-only";

    public ProtectedSecret Protect(string secret, string purpose)
    {
        if (OperatingSystem.IsMacOS() && MacKeychain.TryWrite(purpose, secret))
        {
            return new ProtectedSecret(purpose, MacOsKeychain);
        }

        if (OperatingSystem.IsLinux() && SecretTool.TryStore(purpose, secret))
        {
            return new ProtectedSecret(purpose, LinuxSecretService);
        }

        return new ProtectedSecret("", SessionOnly);
    }

    public string Unprotect(string protectedValue, string protection, string purpose)
    {
        if (OperatingSystem.IsMacOS() &&
            string.Equals(protection, MacOsKeychain, StringComparison.OrdinalIgnoreCase))
        {
            return MacKeychain.TryRead(purpose, out var secret) ? secret : "";
        }

        if (OperatingSystem.IsLinux() &&
            string.Equals(protection, LinuxSecretService, StringComparison.OrdinalIgnoreCase))
        {
            return SecretTool.TryLookup(purpose, out var secret) ? secret : "";
        }

        return "";
    }

    public void Delete(string purpose)
    {
        if (OperatingSystem.IsMacOS())
        {
            MacKeychain.TryDelete(purpose);
        }
        else if (OperatingSystem.IsLinux())
        {
            SecretTool.TryDelete(purpose);
        }
    }

    private static class MacKeychain
    {
        private const string Service = "AIChat";
        private const int Success = 0;
        private const int DuplicateItem = -25299;

        public static bool TryWrite(string account, string secret)
        {
            var serviceBytes = Encoding.UTF8.GetBytes(Service);
            var accountBytes = Encoding.UTF8.GetBytes(account);
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            IntPtr item = IntPtr.Zero;
            IntPtr existingData = IntPtr.Zero;
            try
            {
                var status = SecKeychainAddGenericPassword(
                    IntPtr.Zero,
                    (uint)serviceBytes.Length,
                    serviceBytes,
                    (uint)accountBytes.Length,
                    accountBytes,
                    (uint)secretBytes.Length,
                    secretBytes,
                    out item);
                if (status == Success)
                {
                    return true;
                }
                if (status != DuplicateItem || !TryFind(account, out _, out existingData, out item))
                {
                    return false;
                }

                return SecKeychainItemModifyAttributesAndData(
                    item,
                    IntPtr.Zero,
                    (uint)secretBytes.Length,
                    secretBytes) == Success;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            finally
            {
                if (existingData != IntPtr.Zero)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, existingData);
                }
                if (item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }
        }

        public static bool TryRead(string account, out string secret)
        {
            secret = "";
            IntPtr data = IntPtr.Zero;
            IntPtr item = IntPtr.Zero;
            try
            {
                if (!TryFind(account, out var length, out data, out item))
                {
                    return false;
                }

                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                secret = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            finally
            {
                if (data != IntPtr.Zero)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                }
                if (item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }
        }

        public static void TryDelete(string account)
        {
            IntPtr data = IntPtr.Zero;
            IntPtr item = IntPtr.Zero;
            try
            {
                if (TryFind(account, out _, out data, out item))
                {
                    _ = SecKeychainItemDelete(item);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            finally
            {
                if (data != IntPtr.Zero)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                }
                if (item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }
        }

        private static bool TryFind(
            string account,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef)
        {
            var serviceBytes = Encoding.UTF8.GetBytes(Service);
            var accountBytes = Encoding.UTF8.GetBytes(account);
            return SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                serviceBytes,
                (uint)accountBytes.Length,
                accountBytes,
                out passwordLength,
                out passwordData,
                out itemRef) == Success;
        }

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            uint passwordLength,
            byte[] passwordData,
            out IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemModifyAttributesAndData(
            IntPtr itemRef,
            IntPtr attrList,
            uint length,
            byte[] data);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);
    }

    private static class SecretTool
    {
        private const int TimeoutMilliseconds = 5000;

        public static bool TryStore(string account, string secret)
            => TryRun(
                ["store", "--label=AIChat", "service", "AIChat", "account", account],
                secret,
                out _);

        public static bool TryLookup(string account, out string secret)
        {
            var success = TryRun(
                ["lookup", "service", "AIChat", "account", account],
                null,
                out var output);
            secret = success ? output.TrimEnd('\r', '\n') : "";
            return success;
        }

        public static void TryDelete(string account)
            => _ = TryRun(
                ["clear", "service", "AIChat", "account", account],
                null,
                out _);

        private static bool TryRun(
            IReadOnlyList<string> arguments,
            string? standardInput,
            out string standardOutput)
        {
            standardOutput = "";
            var executable = FindExecutable("secret-tool");
            if (executable is null)
            {
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardInput = standardInput is not null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return false;
                }
                if (standardInput is not null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    return false;
                }

                standardOutput = process.StandardOutput.ReadToEnd();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindExecutable(string name)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists);
        }
    }
}
