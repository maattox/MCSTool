using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// OCIR docker-login user. Product default is
/// <c>{object-storage-namespace}/{ ~/.oci user= }</c>.
/// <see cref="EnvVar"/> remains an escape hatch (full <c>namespace/user</c> string).
/// </summary>
public static class OcirUsername
{
    public const string EnvVar = "MCMANAGER_OCIR_USERNAME";

    public static ServiceResult<string> Derive(string? objectStorageNamespace, string? ociConfigUser)
    {
        var ns = objectStorageNamespace?.Trim();
        var user = ociConfigUser?.Trim();
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(user))
        {
            return ServiceResult<string>.Fail(
                "Object Storage namespace and OCI config user are required to form the OCIR login. "
                + "Function/Events stay skipped.");
        }

        return ServiceResult<string>.Ok(ns + "/" + user);
    }

    public static ServiceResult<string> Resolve(
        string? objectStorageNamespace,
        string? ociConfigUser,
        string? envOverride = null)
    {
        var env = string.IsNullOrWhiteSpace(envOverride)
            ? Environment.GetEnvironmentVariable(EnvVar)
            : envOverride;
        if (!string.IsNullOrWhiteSpace(env))
            return ServiceResult<string>.Ok(env.Trim());

        return Derive(objectStorageNamespace, ociConfigUser);
    }
}
