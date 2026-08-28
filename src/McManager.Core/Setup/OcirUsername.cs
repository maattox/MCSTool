using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// OCIR docker-login user. Product default on identity-domain tenancies is
/// <c>{object-storage-namespace}/{identity-domain}/{IAM user name}</c>.
/// Classic IAM (no domain listed) is <c>{namespace}/{IAM user name}</c>.
/// The IAM name is the Console username (often an email), never the
/// <c>~/.oci</c> <c>user=</c> OCID. <see cref="EnvVar"/> remains an escape hatch
/// (full login string).
/// </summary>
public static class OcirUsername
{
    public const string EnvVar = "MCMANAGER_OCIR_USERNAME";
    public const string DefaultIdentityDomain = "Default";

    public static ServiceResult<string> Derive(
        string? objectStorageNamespace,
        string? iamUserName,
        string? identityDomain = DefaultIdentityDomain)
    {
        var ns = objectStorageNamespace?.Trim();
        var user = iamUserName?.Trim();
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(user))
        {
            return ServiceResult<string>.Fail(
                "Object Storage namespace and IAM user name are required to form the OCIR login. "
                + "Function/Events stay skipped.");
        }

        if (user.StartsWith("ocid1.user.", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.Fail(
                "OCIR login needs the IAM / Console user name, not the ~/.oci user= OCID. "
                + "Function/Events stay skipped.");
        }

        var domain = identityDomain?.Trim();
        var login = string.IsNullOrWhiteSpace(domain)
            ? ns + "/" + user
            : ns + "/" + domain + "/" + user;
        return ServiceResult<string>.Ok(login);
    }

    public static ServiceResult<string> Resolve(
        string? objectStorageNamespace,
        string? iamUserName,
        string? identityDomain = DefaultIdentityDomain,
        string? envOverride = null)
    {
        var env = string.IsNullOrWhiteSpace(envOverride)
            ? Environment.GetEnvironmentVariable(EnvVar)
            : envOverride;
        if (!string.IsNullOrWhiteSpace(env))
            return ServiceResult<string>.Ok(env.Trim());

        return Derive(objectStorageNamespace, iamUserName, identityDomain);
    }
}
