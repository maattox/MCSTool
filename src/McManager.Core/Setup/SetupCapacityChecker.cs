using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;
using Oci.CoreService.Models;
using Oci.CoreService.Requests;
using Oci.IdentityService.Requests;

namespace McManager.Core.Setup;

/// <summary>
/// Probe A1 Flex host capacity via <c>CreateComputeCapacityReport</c> (no VCN or instance).
/// Snapshot only — apply can still lose a race. Matches tofu's first AD and VM1 shape defaults.
/// </summary>
public static class SetupCapacityChecker
{
    /// <summary>Must match <c>infra/variables.tf</c> VM1 defaults (TEMPORARY 2/12 test shape).</summary>
    public const string Vm1Shape = "VM.Standard.A1.Flex";

    public const float Vm1Ocpus = 2;

    public const float Vm1MemoryGb = 12;

    public sealed class Result
    {
        public bool OutOfCapacity { get; init; }
        public bool Unsupported { get; init; }
        public bool ProbeFailed { get; init; }
        public string Message { get; init; } = "";
        public string? AvailabilityDomain { get; init; }
        public string? AvailabilityStatus { get; init; }
        public string? OpcRequestId { get; init; }
    }

    public static async Task<Result> CheckVm1Async(
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var tenancy = OciConfigProfiles.TryGetValue(state.OciProfile, "tenancy");
        if (string.IsNullOrWhiteSpace(tenancy))
        {
            return new Result
            {
                ProbeFailed = true,
                Message = "Could not read tenancy= from ~/.oci/config; skipping capacity probe.",
            };
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
            return new Result
            {
                ProbeFailed = true,
                Message = session.Error ?? "OCI session failed for capacity probe.",
            };
        }

        using var s = session.Value;
        try
        {
            var ads = await s.Identity.ListAvailabilityDomains(
                    new ListAvailabilityDomainsRequest { CompartmentId = tenancy },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var ad = ads.Items?.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(ad))
            {
                return new Result
                {
                    ProbeFailed = true,
                    Message = "No availability domains returned; skipping capacity probe.",
                };
            }

            log?.Report($"Checking A1 Flex host capacity in {ad} ({Vm1Ocpus} OCPU / {Vm1MemoryGb} GB, no instance create)…");

            var request = new CreateComputeCapacityReportRequest
            {
                OpcRetryToken = Guid.NewGuid().ToString("N"),
                CreateComputeCapacityReportDetails = new CreateComputeCapacityReportDetails
                {
                    CompartmentId = tenancy,
                    AvailabilityDomain = ad,
                    ShapeAvailabilities =
                    [
                        new CreateCapacityReportShapeAvailabilityDetails
                        {
                            InstanceShape = Vm1Shape,
                            InstanceShapeConfig = new CapacityReportInstanceShapeConfig
                            {
                                Ocpus = Vm1Ocpus,
                                MemoryInGBs = Vm1MemoryGb,
                                BaselineOcpuUtilization = CapacityReportInstanceShapeConfig.BaselineOcpuUtilizationEnum.Baseline11,
                            },
                        },
                    ],
                },
            };

            var response = await s.Compute.CreateComputeCapacityReport(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var row = response.ComputeCapacityReport?.ShapeAvailabilities?.FirstOrDefault();
            var status = row?.AvailabilityStatus;
            var statusName = status?.ToString() ?? "unknown";
            var opc = response.OpcRequestId;

            if (status == CapacityReportShapeAvailability.AvailabilityStatusEnum.OutOfHostCapacity)
            {
                log?.Report($"A1 Flex is out of host capacity in {ad} (opc-request-id {opc}). Skipping tofu apply.");
                return new Result
                {
                    OutOfCapacity = true,
                    AvailabilityDomain = ad,
                    AvailabilityStatus = statusName,
                    OpcRequestId = opc,
                    Message =
                        "Always Free A1 Flex host capacity is unavailable in this region right now. "
                        + "VM1 was not created. Retry reuses any compartment/VCN/door already in OpenTofu state.",
                };
            }

            if (status == CapacityReportShapeAvailability.AvailabilityStatusEnum.HardwareNotSupported)
            {
                return new Result
                {
                    Unsupported = true,
                    AvailabilityDomain = ad,
                    AvailabilityStatus = statusName,
                    OpcRequestId = opc,
                    Message = $"VM.Standard.A1.Flex is not supported in {ad}. Pick the tenancy home region.",
                };
            }

            log?.Report($"A1 Flex capacity report: {statusName} in {ad} (opc-request-id {opc}). This is not a reservation.");
            return new Result
            {
                AvailabilityDomain = ad,
                AvailabilityStatus = statusName,
                OpcRequestId = opc,
                Message = statusName,
            };
        }
        catch (Exception ex)
        {
            log?.Report("Capacity probe failed (" + ex.Message + "). Continuing to tofu apply.");
            return new Result
            {
                ProbeFailed = true,
                Message = ex.Message,
            };
        }
    }
}
