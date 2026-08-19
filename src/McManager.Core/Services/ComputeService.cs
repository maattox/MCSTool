using McManager.Core.Oci;
using Oci.CoreService.Models;
using Oci.CoreService.Requests;

namespace McManager.Core.Services;

public sealed record InstanceShapeSnapshot(
    string LifecycleState,
    string Shape,
    double Ocpus,
    double MemoryGb);

public interface IComputeService
{
    Task<ServiceResult<string>> GetLifecycleStateAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InstanceShapeSnapshot>> GetInstanceShapeAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> StartInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SoftStopInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> UpdateInstanceShapeConfigAsync(
        string instanceId,
        float ocpus,
        float memoryGb,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<string>> WaitForLifecycleAsync(
        string instanceId,
        string desiredState,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InstanceShapeSnapshot>> WaitForShapeConfigAsync(
        string instanceId,
        double ocpus,
        double memoryGb,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public sealed class ComputeService : IComputeService
{
    private readonly OciSession _session;

    public ComputeService(OciSession session) => _session = session;

    public async Task<ServiceResult<string>> GetLifecycleStateAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult<string>.Fail("instance_id is empty.");

        try
        {
            var response = await _session.Compute.GetInstance(
                new GetInstanceRequest { InstanceId = instanceId },
                retryConfiguration: _session.RetryConfiguration,
                cancellationToken: cancellationToken);

            var state = response.Instance.LifecycleState?.ToString() ?? "UNKNOWN";
            return ServiceResult<string>.Ok(state);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(FormatOciError("GetInstance", ex));
        }
    }

    public async Task<ServiceResult<InstanceShapeSnapshot>> GetInstanceShapeAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult<InstanceShapeSnapshot>.Fail("instance_id is empty.");

        try
        {
            var response = await _session.Compute.GetInstance(
                new GetInstanceRequest { InstanceId = instanceId },
                retryConfiguration: _session.RetryConfiguration,
                cancellationToken: cancellationToken);

            return ServiceResult<InstanceShapeSnapshot>.Ok(ReadSnapshot(response.Instance));
        }
        catch (Exception ex)
        {
            return ServiceResult<InstanceShapeSnapshot>.Fail(FormatOciError("GetInstance", ex));
        }
    }

    public async Task<ServiceResult> StartInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult.Fail("instance_id is empty.");

        try
        {
            await _session.Compute.InstanceAction(
                new InstanceActionRequest
                {
                    InstanceId = instanceId,
                    Action = "START",
                },
                retryConfiguration: _session.RetryConfiguration,
                cancellationToken: cancellationToken);

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(FormatOciError("InstanceAction START", ex));
        }
    }

    public async Task<ServiceResult> SoftStopInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult.Fail("instance_id is empty.");

        try
        {
            await _session.Compute.InstanceAction(
                new InstanceActionRequest
                {
                    InstanceId = instanceId,
                    Action = "SOFTSTOP",
                },
                retryConfiguration: _session.RetryConfiguration,
                cancellationToken: cancellationToken);

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(FormatOciError("InstanceAction SOFTSTOP", ex));
        }
    }

    public async Task<ServiceResult> UpdateInstanceShapeConfigAsync(
        string instanceId,
        float ocpus,
        float memoryGb,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult.Fail("instance_id is empty.");
        if (ocpus <= 0 || memoryGb <= 0)
            return ServiceResult.Fail("Shape OCPUs and memory GB must be > 0.");

        try
        {
            await _session.Compute.UpdateInstance(
                new UpdateInstanceRequest
                {
                    InstanceId = instanceId,
                    UpdateInstanceDetails = new UpdateInstanceDetails
                    {
                        ShapeConfig = new UpdateInstanceShapeConfigDetails
                        {
                            Ocpus = ocpus,
                            MemoryInGBs = memoryGb,
                        },
                    },
                },
                retryConfiguration: _session.RetryConfiguration,
                cancellationToken: cancellationToken);

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(FormatOciError("UpdateInstance shapeConfig", ex));
        }
    }

    public async Task<ServiceResult<string>> WaitForLifecycleAsync(
        string instanceId,
        string desiredState,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromMinutes(20);
        var deadline = DateTime.UtcNow + limit;
        var delaySeconds = 3.0;
        const double maxDelaySeconds = 30.0;
        var desired = desiredState.Trim().ToUpperInvariant();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await GetLifecycleStateAsync(instanceId, cancellationToken);
            if (!current.Succeeded)
                return current;

            var state = (current.Value ?? "").ToUpperInvariant();
            if (state == desired)
                return current;

            if (DateTime.UtcNow >= deadline)
            {
                return ServiceResult<string>.Fail(
                    $"Timed out waiting for lifecycle {desired} (last={state}) after {limit.TotalMinutes:0} minutes.");
            }

            var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, maxDelaySeconds));
            await Task.Delay(delay, cancellationToken);
            delaySeconds = Math.Min(delaySeconds * 2, maxDelaySeconds);
        }
    }

    public async Task<ServiceResult<InstanceShapeSnapshot>> WaitForShapeConfigAsync(
        string instanceId,
        double ocpus,
        double memoryGb,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromMinutes(20);
        var deadline = DateTime.UtcNow + limit;
        var delaySeconds = 3.0;
        const double maxDelaySeconds = 30.0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await GetInstanceShapeAsync(instanceId, cancellationToken);
            if (!current.Succeeded || current.Value is null)
                return current;

            var snap = current.Value;
            var state = (snap.LifecycleState ?? "").ToUpperInvariant();
            if (state is "FAILED" or "TERMINATED" or "TERMINATING")
            {
                return ServiceResult<InstanceShapeSnapshot>.Fail(
                    $"Instance entered {state} while waiting for shape "
                    + $"{Vm1ShapeScaleUx.FormatExact(ocpus, memoryGb)}.");
            }

            var stable = state is "STOPPED" or "RUNNING";
            if (stable && Vm1ShapeScaleUx.ShapeEquals(snap.Ocpus, snap.MemoryGb, ocpus, memoryGb))
                return current;

            if (DateTime.UtcNow >= deadline)
            {
                return ServiceResult<InstanceShapeSnapshot>.Fail(
                    $"Timed out waiting for shape {Vm1ShapeScaleUx.FormatExact(ocpus, memoryGb)} "
                    + $"(last={Vm1ShapeScaleUx.FormatExact(snap.Ocpus, snap.MemoryGb)}, lifecycle={state}) "
                    + $"after {limit.TotalMinutes:0} minutes.");
            }

            var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, maxDelaySeconds));
            await Task.Delay(delay, cancellationToken);
            delaySeconds = Math.Min(delaySeconds * 2, maxDelaySeconds);
        }
    }

    private static InstanceShapeSnapshot ReadSnapshot(Instance instance)
    {
        var state = instance.LifecycleState?.ToString() ?? "UNKNOWN";
        var shape = instance.Shape?.Trim() ?? "";
        var ocpus = (double)(instance.ShapeConfig?.Ocpus ?? 0);
        var memory = (double)(instance.ShapeConfig?.MemoryInGBs ?? 0);
        return new InstanceShapeSnapshot(state, shape, ocpus, memory);
    }

    /// <summary>
    /// Targeted public-IP lookup for one instance (ListVnicAttachments by instance OCID + GetVnic).
    /// Used when meta <c>ssh_host</c> is empty/stale. Not a tenancy-wide List.
    /// </summary>
    public async Task<ServiceResult<string?>> TryGetPrimaryPublicIpAsync(
        string compartmentId,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(compartmentId))
            return ServiceResult<string?>.Fail("compartment_id is empty.");
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult<string?>.Fail("instance_id is empty.");

        try
        {
            string? page = null;
            string? primary = null;
            string? any = null;
            do
            {
                var response = await _session.Compute.ListVnicAttachments(
                    new ListVnicAttachmentsRequest
                    {
                        CompartmentId = compartmentId,
                        InstanceId = instanceId,
                        Page = page,
                    },
                    retryConfiguration: _session.RetryConfiguration,
                    cancellationToken: cancellationToken);

                if (response.Items is not null)
                {
                    foreach (var attachment in response.Items)
                    {
                        if (string.IsNullOrWhiteSpace(attachment.VnicId))
                            continue;

                        var vnic = await _session.VirtualNetwork.GetVnic(
                            new GetVnicRequest { VnicId = attachment.VnicId },
                            retryConfiguration: _session.RetryConfiguration,
                            cancellationToken: cancellationToken);

                        var rawIp = vnic.Vnic?.PublicIp?.Trim();
                        if (string.IsNullOrWhiteSpace(rawIp))
                            continue;

                        var ip = rawIp;
                        any ??= ip;
                        if (vnic.Vnic?.IsPrimary == true)
                            primary = ip;
                    }
                }

                page = response.OpcNextPage;
            }
            while (!string.IsNullOrWhiteSpace(page));

            return ServiceResult<string?>.Ok(primary ?? any);
        }
        catch (Exception ex)
        {
            return ServiceResult<string?>.Fail(FormatOciError("ListVnicAttachments/GetVnic", ex));
        }
    }

    internal static string FormatOciError(string operation, Exception ex) =>
        OciErrorFormatter.Format(operation, ex);
}
