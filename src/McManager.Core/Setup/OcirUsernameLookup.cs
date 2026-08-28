using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;
using Oci.IdentityService.Models;
using Oci.IdentityService.Requests;

namespace McManager.Core.Setup;

public sealed record OcirIamLogin(string IamUserName, string? IdentityDomain);

/// <summary>
/// Resolves the OCIR docker-login identity (IAM user name + identity domain)
/// from the wizard OCI profile. Does not return the <c>user=</c> OCID.
/// </summary>
public static class OcirUsernameLookup
{
    public static async Task<ServiceResult<OcirIamLogin>> LookupAsync(
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var userOcid = OciConfigProfiles.TryGetValue(state.OciProfile, "user");
        var tenancy = OciConfigProfiles.TryGetValue(state.OciProfile, "tenancy");
        if (string.IsNullOrWhiteSpace(userOcid) || string.IsNullOrWhiteSpace(tenancy))
        {
            return ServiceResult<OcirIamLogin>.Fail(
                "Could not read user= / tenancy= from ~/.oci/config for the selected profile. "
                + "Function/Events stay skipped.");
        }

        var config = new ManagerLocalConfig
        {
            Oci = new OciSettings
            {
                ConfigFile = OciConfigProfiles.DefaultConfigPath(),
                Profile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile,
                Region = state.OciRegion,
                TenancyId = tenancy,
                CompartmentId = tenancy,
            },
        };

        var session = OciSession.TryCreate(config);
        if (!session.Succeeded || session.Value is null)
        {
            return ServiceResult<OcirIamLogin>.Fail(
                session.Error ?? "OCI session failed while resolving the OCIR login user.");
        }

        using var s = session.Value;
        try
        {
            log?.Report("Resolving OCIR login from IAM user name (not the ~/.oci user= OCID)…");
            var userResp = await s.Identity.GetUser(
                    new GetUserRequest { UserId = userOcid },
                    retryConfiguration: s.RetryConfiguration,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var iamName = userResp.User?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(iamName))
                iamName = userResp.User?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(iamName)
                || iamName.StartsWith("ocid1.user.", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<OcirIamLogin>.Fail(
                    "IAM GetUser did not return a Console user name for OCIR login. "
                    + "Function/Events stay skipped.");
            }

            var domain = await TryDefaultIdentityDomainAsync(s, tenancy, log, cancellationToken)
                .ConfigureAwait(false);
            return ServiceResult<OcirIamLogin>.Ok(new OcirIamLogin(iamName, domain));
        }
        catch (Exception ex)
        {
            return ServiceResult<OcirIamLogin>.Fail(
                OciErrorFormatter.Format("GetUser", ex)
                + " Function/Events stay skipped.");
        }
    }

    private static async Task<string?> TryDefaultIdentityDomainAsync(
        OciSession session,
        string tenancy,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        try
        {
            DomainSummary? chosen = null;
            string? page = null;
            do
            {
                var response = await session.Identity.ListDomains(
                        new ListDomainsRequest
                        {
                            CompartmentId = tenancy,
                            LifecycleState = Domain.LifecycleStateEnum.Active,
                            Page = page,
                        },
                        retryConfiguration: session.RetryConfiguration,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (response.Items is not null)
                {
                    foreach (var item in response.Items)
                    {
                        if (item is null)
                            continue;
                        if (item.Type == Domain.TypeEnum.Default)
                        {
                            chosen = item;
                            break;
                        }

                        chosen ??= item;
                    }
                }

                if (chosen?.Type == Domain.TypeEnum.Default)
                    break;
                page = response.OpcNextPage;
            }
            while (!string.IsNullOrWhiteSpace(page));

            var name = chosen?.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name) && chosen?.Type == Domain.TypeEnum.Default)
                name = OcirUsername.DefaultIdentityDomain;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex)
        {
            log?.Report(
                OciErrorFormatter.Format("ListDomains", ex)
                + " Using two-part OCIR login (no identity domain).");
            return null;
        }
    }
}
