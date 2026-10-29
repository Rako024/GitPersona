using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GitPersona.Core.Security;

/// <summary>An entry in Windows Credential Manager (secret not included).</summary>
public sealed record StoredCredential(string TargetName, string? UserName, DateTimeOffset LastWritten);

/// <summary>
/// Generic credentials in Windows Credential Manager (the "Windows Credentials" list), protected by DPAPI
/// with the user's logon. This is the only place GitPersona keeps secrets.
/// </summary>
[SuppressMessage("Performance", "CA1822", Justification = "Injected service; callers depend on an instance.")]
public sealed partial class CredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxBlobBytes = 5 * 512;

    public void Save(string targetName, string userName, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(secret);
        var blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length > MaxBlobBytes)
        {
            throw new ArgumentException("The secret is too long for Windows Credential Manager.", nameof(secret));
        }

        var target = Marshal.StringToCoTaskMemUni(targetName);
        var user = Marshal.StringToCoTaskMemUni(userName);
        var data = Marshal.AllocCoTaskMem(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                UserName = user,
                CredentialBlob = data,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
            };
            if (!CredWrite(credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, data, blob.Length);
            CryptographicOperations.ZeroMemory(blob);
            Marshal.FreeCoTaskMem(data);
            Marshal.FreeCoTaskMem(user);
            Marshal.FreeCoTaskMem(target);
        }
    }

    /// <returns>The user name and secret, or <see langword="null"/> when there is no such entry.</returns>
    public (string? UserName, string Secret)? Read(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (!CredRead(targetName, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var secret = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return (Marshal.PtrToStringUni(credential.UserName), secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <returns><see langword="false"/> when the entry did not exist.</returns>
    public bool Delete(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (CredDelete(targetName, CredTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        return error == ErrorNotFound ? false : throw new Win32Exception(error);
    }

    /// <param name="filter">Target name prefix with a trailing <c>*</c>, e.g. <c>git:https://github.com*</c>.</param>
    public IReadOnlyList<StoredCredential> Enumerate(string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        if (!CredEnumerate(filter, 0, out var count, out var list))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? [] : throw new Win32Exception(error);
        }

        try
        {
            var result = new List<StoredCredential>((int)count);
            for (var i = 0; i < count; i++)
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                var fileTime = ((long)credential.LastWrittenHigh << 32) | credential.LastWrittenLow;
                result.Add(new StoredCredential(
                    Marshal.PtrToStringUni(credential.TargetName) ?? string.Empty,
                    Marshal.PtrToStringUni(credential.UserName),
                    DateTimeOffset.FromFileTime(fileTime)));
            }

            return result;
        }
        finally
        {
            CredFree(list);
        }
    }

    // CREDENTIALW; strings are left as pointers so the struct stays blittable.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in NativeCredential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string? filter, uint flags, out uint count, out IntPtr credentials);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}

/// <summary>GitHub tokens per account: <c>GitPersona:github.com:octocat</c>.</summary>
public sealed class GitHubTokenStore(CredentialStore store)
{
    public const string Prefix = "GitPersona:";

    public static string TargetFor(string host, string login) =>
        $"{Prefix}{host.Trim().ToLowerInvariant()}:{login.Trim().ToLowerInvariant()}";

    public void Save(string host, string login, string token) => store.Save(TargetFor(host, login), login, token);

    public string? Read(string host, string login) =>
        string.IsNullOrWhiteSpace(login) ? null : store.Read(TargetFor(host, login))?.Secret;

    public bool Delete(string host, string login) => store.Delete(TargetFor(host, login));
}
