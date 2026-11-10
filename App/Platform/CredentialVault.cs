using System.Runtime.InteropServices;

namespace GaugeOverlay.App.Platform;

/// <summary>The Lost Ark API key in Windows Credential Manager (lostark-api.plan.md, user 2026-11-08): encrypted to the Windows
/// login by Windows itself, in no app folder, so no copy of settings, logs or a bug-report zip can carry it. A generic
/// credential "GaugeOverlay/LostArkApi" (user "api", the key as UTF-16), the same entry `cmdkey /generic:` makes.</summary>
public static partial class CredentialVault
{
    public const string Target = "GaugeOverlay/LostArkApi";
    const string User = "api";
    const int GenericType = 1, PersistLocalMachine = 2, NotFound = 1168;

    /// <summary>Whether a key is saved, without copying it out of the vault.</summary>
    public static bool Has()
    {
        if (!CredReadW(Target, GenericType, 0, out var ptr)) return false;
        try { return Marshal.PtrToStructure<Credential>(ptr).BlobSize > 0; }
        finally { Free(ptr); }
    }

    /// <summary>The saved key, or null when there is none (or the vault can't be read). The returned string can't be wiped;
    /// only the vault's own buffer is.</summary>
    public static string? Read()
    {
        if (!CredReadW(Target, GenericType, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            return cred.BlobSize == 0 ? null : Marshal.PtrToStringUni(cred.Blob, (int)cred.BlobSize / sizeof(char));
        }
        finally { Free(ptr); }
    }

    /// <summary>Wipes the blob Windows handed back (CredFree doesn't), then frees it.</summary>
    static unsafe void Free(IntPtr ptr)
    {
        var cred = Marshal.PtrToStructure<Credential>(ptr);
        if (cred.Blob != IntPtr.Zero) System.Security.Cryptography.CryptographicOperations.ZeroMemory(new Span<byte>((void*)cred.Blob, (int)cred.BlobSize));
        CredFree(ptr);
    }

    /// <summary>Saves (or replaces) the key; false when Windows refused.</summary>
    public static bool Save(string key)
    {
        IntPtr blob = Marshal.StringToHGlobalUni(key), target = Marshal.StringToHGlobalUni(Target), user = Marshal.StringToHGlobalUni(User);
        try
        {
            var cred = new Credential
            {
                Type = GenericType, TargetName = target, UserName = user, Persist = PersistLocalMachine,
                Blob = blob, BlobSize = (uint)(key.Length * sizeof(char)),
            };
            return CredWriteW(cred, 0);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(blob);   // this copy wiped, not only freed
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
        }
    }

    /// <summary>Removes the key; true when it is gone (also when there was none).</summary>
    public static bool Delete() => CredDeleteW(Target, GenericType, 0) || Marshal.GetLastPInvokeError() == NotFound;

    [StructLayout(LayoutKind.Sequential)]
    struct Credential
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(in Credential credential, int flags);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, int type, int flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);
}
