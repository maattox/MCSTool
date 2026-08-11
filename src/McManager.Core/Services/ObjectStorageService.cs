using McManager.Core.Config;
using McManager.Core.Oci;
using Oci.ObjectstorageService.Requests;

namespace McManager.Core.Services;

public sealed class ObjectStorageService : IObjectStorageService
{
    private readonly OciSession _session;
    private readonly string _namespace;
    private readonly string _bucket;

    public ObjectStorageService(OciSession session, ObjectStorageSettings settings)
    {
        _session = session;
        _namespace = settings.Namespace;
        _bucket = settings.Bucket;
    }

    public async Task<ServiceResult<byte[]>> GetBytesAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObjectName(objectName);
        if (validation is not null)
            return ServiceResult<byte[]>.Fail(validation);

        try
        {
            var response = await _session.ObjectStorage.GetObject(
                new GetObjectRequest
                {
                    NamespaceName = _namespace,
                    BucketName = _bucket,
                    ObjectName = objectName,
                },
                cancellationToken: cancellationToken);

            await using var stream = response.InputStream;
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            return ServiceResult<byte[]>.Ok(memory.ToArray());
        }
        catch (Exception ex)
        {
            return ServiceResult<byte[]>.Fail(ComputeService.FormatOciError("GetObject", ex));
        }
    }

    public async Task<ServiceResult> PutBytesAsync(
        string objectName,
        byte[] content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObjectName(objectName);
        if (validation is not null)
            return ServiceResult.Fail(validation);

        try
        {
            await using var stream = new MemoryStream(content);
            await _session.ObjectStorage.PutObject(
                new PutObjectRequest
                {
                    NamespaceName = _namespace,
                    BucketName = _bucket,
                    ObjectName = objectName,
                    PutObjectBody = stream,
                    ContentLength = content.Length,
                    ContentType = contentType,
                },
                cancellationToken: cancellationToken);

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ComputeService.FormatOciError("PutObject", ex));
        }
    }

    public async Task<ServiceResult<IReadOnlyList<string>>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_namespace))
            return ServiceResult<IReadOnlyList<string>>.Fail("object_storage.namespace is empty.");
        if (string.IsNullOrWhiteSpace(_bucket))
            return ServiceResult<IReadOnlyList<string>>.Fail("object_storage.bucket is empty.");

        try
        {
            var names = new List<string>();
            string? start = null;

            do
            {
                var response = await _session.ObjectStorage.ListObjects(
                    new ListObjectsRequest
                    {
                        NamespaceName = _namespace,
                        BucketName = _bucket,
                        Prefix = prefix,
                        Start = start,
                    },
                    cancellationToken: cancellationToken);

                if (response.ListObjects.Objects is not null)
                {
                    foreach (var obj in response.ListObjects.Objects)
                    {
                        if (!string.IsNullOrWhiteSpace(obj.Name))
                            names.Add(obj.Name);
                    }
                }

                start = response.ListObjects.NextStartWith;
            }
            while (!string.IsNullOrWhiteSpace(start));

            return ServiceResult<IReadOnlyList<string>>.Ok(names);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<string>>.Fail(ComputeService.FormatOciError("ListObjects", ex));
        }
    }

    private string? ValidateObjectName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(_namespace))
            return "object_storage.namespace is empty.";
        if (string.IsNullOrWhiteSpace(_bucket))
            return "object_storage.bucket is empty.";
        if (string.IsNullOrWhiteSpace(objectName))
            return "Object name is empty.";
        return null;
    }
}
