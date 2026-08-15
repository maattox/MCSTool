using System.Runtime.InteropServices;
using System.Text;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Stores the optional OCIR Auth Token in Windows Credential Manager (target <c>McManager/ocir</c>).
/// Never writes the token to wizard JSON.
/// </summary>
public static class WindowsCredentialStore
{
    public const string OcirTarget = "McManager/ocir";

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool Exists(string target = OcirTarget) =>
        IsSupported && TryRead(target, out _);

    public static ServiceResult SaveOcirToken(string token)
    {
        if (!IsSupported)
            return ServiceResult.Fail("Windows Credential Manager is only available on Windows.");

        if (string.IsNullOrWhiteSpace(token))
            return ServiceResult.Fail("Auth Token is empty.");

        return Write(OcirTarget, "ocir", token.Trim());
    }

    public static ServiceResult DeleteOcirToken()
    {
        if (!IsSupported)
            return ServiceResult.Fail("Windows Credential Manager is only available on Windows.");

        if (!CredDelete(OcirTarget, CredTypeGeneric, 0))
        {
            var code = Marshal.GetLastWin32Error();
            if (code == 1168) // ERROR_NOT_FOUND
                return ServiceResult.Ok();
            return ServiceResult.Fail($"CredDelete failed (Win32 {code}).");
        }

        return ServiceResult.Ok();
    }

    public static bool TryRead(string target, out string secret)
    {
        secret = "";
        if (!IsSupported)
            return false;

        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
            return false;

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return false;

            secret = Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2) ?? "";
            return !string.IsNullOrEmpty(secret);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static ServiceResult Write(string target, string userName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new NativeCredential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = "OCI MC Server OCIR Auth Token",
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = userName,
            };

            if (!CredWrite(ref cred, 0))
            {
                var code = Marshal.GetLastWin32Error();
                return ServiceResult.Fail($"CredWrite failed (Win32 {code}).");
            }

            return ServiceResult.Ok();
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
