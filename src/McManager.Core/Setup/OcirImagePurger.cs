using McManager.Core.Oci;
using McManager.Core.Services;
using Oci.ArtifactsService.Requests;

namespace McManager.Core.Setup;

/// <summary>
/// Deletes images in the product OCIR repo so OpenTofu can destroy
/// <c>oci_artifacts_container_repository.softstop</c>. Best-effort.
/// </summary>
public static class OcirImagePurger
{
    public const string ProductRepositoryName = "mcmgr-fn/softstop";

    public static async Task<ServiceResult<int>> DeleteProductImagesAsync(
        OciSession session,
        string compartmentId,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(compartmentId))
            return ServiceResult<int>.Ok(0);

        try
        {
            var deleted = 0;
            string? page = null;
            do
            {
                var response = await session.Artifacts.ListContainerImages(
                    new ListContainerImagesRequest
                    {
                        CompartmentId = compartmentId,
                        RepositoryName = ProductRepositoryName,
                        Page = page,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var items = response.ContainerImageCollection?.Items;
                if (items is not null)
                {
                    foreach (var image in items)
                    {
                        if (string.IsNullOrWhiteSpace(image.Id))
                            continue;

                        cancellationToken.ThrowIfCancellationRequested();
                        log?.Report($"Deleting OCIR image {ShortId(image.Id)}…");
                        await session.Artifacts.DeleteContainerImage(
                            new DeleteContainerImageRequest { ImageId = image.Id },
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        deleted++;
                    }
                }

                page = response.OpcNextPage;
            }
            while (!string.IsNullOrWhiteSpace(page));

            return ServiceResult<int>.Ok(deleted);
        }
        catch (Exception ex)
        {
            if (OciErrorFormatter.IsNotFound(ex))
                return ServiceResult<int>.Ok(0);
            return ServiceResult<int>.Fail(ComputeService.FormatOciError("List/DeleteContainerImage", ex));
        }
    }

    private static string ShortId(string ocid)
    {
        if (ocid.Length <= 22)
            return ocid;
        return ocid[^12..];
    }
}
