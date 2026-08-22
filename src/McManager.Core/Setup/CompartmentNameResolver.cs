using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;
using Oci.IdentityService.Models;
using Oci.IdentityService.Requests;

namespace McManager.Core.Setup;

/// <summary>
/// List-then-create naming for Setup. Never pastes an existing compartment OCID.
/// Connect-existing (Advanced Auto-detect) remains the escape hatch for an already-deployed stack.
/// </summary>
public static class CompartmentNameResolver
{
    public static async Task<ServiceResult<string>> AssignAsync(
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.CreateCompartment = true;
        state.ExistingCompartmentId = "";
        if (string.IsNullOrWhiteSpace(state.CompartmentName))
            state.CompartmentName = CompartmentNamer.BaseName;

        var hasTofu = TofuWorkspace.TryFindExisting(state.CompartmentName) is not null;
        if (CompartmentNamer.ShouldReuseAssignedName(state.CompartmentName, state.ApplyStage, hasTofu))
        {
            log?.Report($"Reusing compartment name `{state.CompartmentName}`.");
            return ServiceResult<string>.Ok(state.CompartmentName);
        }

        if (dryRun)
        {
            log?.Report($"[dry-run] Compartment name `{state.CompartmentName}` (no ListCompartments).");
            return ServiceResult<string>.Ok(state.CompartmentName);
        }

        var listed = await ListDisplayNamesAsync(state, log, cancellationToken).ConfigureAwait(false);
        if (!listed.Succeeded || listed.Value is null)
        {
            return ServiceResult<string>.Fail(
                listed.Error ?? "Could not list compartments to pick a free name.");
        }

        if (!CompartmentNamer.TryNextAvailable(listed.Value, out var next))
        {
            return ServiceResult<string>.Fail(
                $"Could not find a free compartment name ({CompartmentNamer.BaseName} through "
                + $"{CompartmentNamer.BaseName}-{CompartmentNamer.MaxNumericSuffix} are taken).");
        }

        state.CompartmentName = next;
        log?.Report(
            string.Equals(next, CompartmentNamer.BaseName, StringComparison.OrdinalIgnoreCase)
                ? $"Compartment name `{next}`."
                : $"Compartment `{CompartmentNamer.BaseName}` is taken; using `{next}`.");
        SetupWizardStore.Save(state);
        return ServiceResult<string>.Ok(next);
    }

    private static async Task<ServiceResult<IReadOnlyList<string>>> ListDisplayNamesAsync(
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var tenancy = OciConfigProfiles.TryGetValue(state.OciProfile, "tenancy");
        if (string.IsNullOrWhiteSpace(tenancy))
        {
            return ServiceResult<IReadOnlyList<string>>.Fail(
                "Could not read tenancy= from ~/.oci/config for the selected profile.");
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
            return ServiceResult<IReadOnlyList<string>>.Fail(
                session.Error ?? "OCI session failed while listing compartments.");
        }

        using var s = session.Value;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            log?.Report("Listing compartments to pick a free name…");
            try
            {
                var root = await s.Identity.GetCompartment(
                        new GetCompartmentRequest { CompartmentId = tenancy },
                        retryConfiguration: s.RetryConfiguration,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(root.Compartment?.Name))
                    names.Add(root.Compartment.Name.Trim());
            }
            catch (Exception ex)
            {
                log?.Report(OciErrorFormatter.Format("GetCompartment", ex));
            }

            foreach (var lifecycle in OccupyingStates)
            {
                string? page = null;
                do
                {
                    var response = await s.Identity.ListCompartments(
                            new ListCompartmentsRequest
                            {
                                CompartmentId = tenancy,
                                CompartmentIdInSubtree = true,
                                AccessLevel = ListCompartmentsRequest.AccessLevelEnum.Accessible,
                                LifecycleState = lifecycle,
                                Page = page,
                            },
                            retryConfiguration: s.RetryConfiguration,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (response.Items is not null)
                    {
                        foreach (var compartment in response.Items)
                        {
                            if (!string.IsNullOrWhiteSpace(compartment?.Name))
                                names.Add(compartment.Name.Trim());
                        }
                    }

                    page = response.OpcNextPage;
                }
                while (!string.IsNullOrWhiteSpace(page));
            }

            return ServiceResult<IReadOnlyList<string>>.Ok(names.ToList());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<string>>.Fail(
                OciErrorFormatter.Format("ListCompartments", ex));
        }
    }

    private static readonly Compartment.LifecycleStateEnum[] OccupyingStates =
    [
        Compartment.LifecycleStateEnum.Active,
        Compartment.LifecycleStateEnum.Creating,
        Compartment.LifecycleStateEnum.Deleting,
        Compartment.LifecycleStateEnum.Deleted,
    ];
}
