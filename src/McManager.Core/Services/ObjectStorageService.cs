using System.Net.Http;
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
        var detailed = await ListDetailedAsync(prefix, cancellationToken);
        if (!detailed.Succeeded || detailed.Value is null)
            return ServiceResult<IReadOnlyList<string>>.Fail(detailed.Error ?? "ListObjects failed.");

        return ServiceResult<IReadOnlyList<string>>.Ok(
            detailed.Value.Select(o => o.Name).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<ObjectStorageObject>>> ListDetailedAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_namespace))
            return ServiceResult<IReadOnlyList<ObjectStorageObject>>.Fail("object_storage.namespace is empty.");
        if (string.IsNullOrWhiteSpace(_bucket))
            return ServiceResult<IReadOnlyList<ObjectStorageObject>>.Fail("object_storage.bucket is empty.");

        try
        {
            var items = new List<ObjectStorageObject>();
            string? start = null;

            do
            {
                var response = await _session.ObjectStorage.ListObjects(
                    new ListObjectsRequest
                    {
                        NamespaceName = _namespace,
                        BucketName = _bucket,
                        Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                        Start = start,
                        Fields = "name,size,timeCreated",
                    },
                    cancellationToken: cancellationToken);

                if (response.ListObjects.Objects is not null)
                {
                    foreach (var obj in response.ListObjects.Objects)
                    {
                        if (string.IsNullOrWhiteSpace(obj.Name))
                            continue;

                        items.Add(new ObjectStorageObject
                        {
                            Name = obj.Name,
                            SizeBytes = obj.Size ?? 0,
                            TimeCreated = obj.TimeCreated,
                        });
                    }
                }

                start = response.ListObjects.NextStartWith;
            }
            while (!string.IsNullOrWhiteSpace(start));

            return ServiceResult<IReadOnlyList<ObjectStorageObject>>.Ok(items);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ObjectStorageObject>>.Fail(
                ComputeService.FormatOciError("ListObjects", ex));
        }
    }

    public async Task<ServiceResult> DownloadToFileAsync(
        string objectName,
        string localPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObjectName(objectName);
        if (validation is not null)
            return ServiceResult.Fail(validation);
        if (string.IsNullOrWhiteSpace(localPath))
            return ServiceResult.Fail("Local path is empty.");

        try
        {
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            // ResponseHeadersRead avoids buffering the entire body (fails for objects > ~2 GiB).
            // See oracle/oci-dotnet-sdk#88 / HttpClient MaxResponseContentBufferSize.
            var response = await _session.ObjectStorage.GetObject(
                new GetObjectRequest
                {
                    NamespaceName = _namespace,
                    BucketName = _bucket,
                    ObjectName = objectName,
                },
                cancellationToken: cancellationToken,
                completionOption: HttpCompletionOption.ResponseHeadersRead);

            await using var remote = response.InputStream;
            await using var local = new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true);

            var buffer = new byte[1024 * 128];
            long written = 0;
            int read;
            while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                progress?.Report(written);
            }

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ComputeService.FormatOciError("GetObject(download)", ex));
        }
    }

    public async Task<ServiceResult> UploadFromFileAsync(
        string objectName,
        string localPath,
        string contentType = "application/octet-stream",
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObjectName(objectName);
        if (validation is not null)
            return ServiceResult.Fail(validation);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            return ServiceResult.Fail($"Local file not found: {localPath}");

        try
        {
            var fileInfo = new FileInfo(localPath);
            await using var local = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);

            // Progress wrapper reports bytes read from disk as the SDK consumes the stream.
            Stream body = progress is null
                ? local
                : new ProgressReadStream(local, progress);

            await _session.ObjectStorage.PutObject(
                new PutObjectRequest
                {
                    NamespaceName = _namespace,
                    BucketName = _bucket,
                    ObjectName = objectName,
                    PutObjectBody = body,
                    ContentLength = fileInfo.Length,
                    ContentType = contentType,
                },
                cancellationToken: cancellationToken);

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(ComputeService.FormatOciError("PutObject(upload)", ex));
        }
    }

    public async Task<ServiceResult> DeleteObjectAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateObjectName(objectName);
        if (validation is not null)
            return ServiceResult.Fail(validation);

        try
        {
            await _session.ObjectStorage.DeleteObject(
                new DeleteObjectRequest
                {
                    NamespaceName = _namespace,
                    BucketName = _bucket,
                    ObjectName = objectName,
                },
                cancellationToken: cancellationToken);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            if (OciErrorFormatter.IsNotFound(ex))
                return ServiceResult.Ok();
            return ServiceResult.Fail(ComputeService.FormatOciError("DeleteObject", ex));
        }
    }

    public async Task<ServiceResult<int>> DeleteAllObjectsAsync(CancellationToken cancellationToken = default)
    {
        var listed = await ListDetailedAsync("", cancellationToken).ConfigureAwait(false);
        if (!listed.Succeeded)
        {
            if (OciErrorFormatter.IsNotFoundMessage(listed.Error))
                return ServiceResult<int>.Ok(0);
            return ServiceResult<int>.Fail(listed.Error ?? "ListObjects failed.");
        }

        var names = listed.Value ?? [];
        var deleted = 0;
        foreach (var obj in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DeleteObjectAsync(obj.Name, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return ServiceResult<int>.Fail(result.Error ?? $"Failed to delete {obj.Name}.");
            deleted++;
        }

        return ServiceResult<int>.Ok(deleted);
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

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<long> _progress;
        private long _total;

        public ProgressReadStream(Stream inner, IProgress<long> progress)
        {
            _inner = inner;
            _progress = progress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            if (n > 0)
            {
                _total += n;
                _progress.Report(_total);
            }

            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (n > 0)
            {
                _total += n;
                _progress.Report(_total);
            }

            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await _inner.ReadAsync(buffer, cancellationToken);
            if (n > 0)
            {
                _total += n;
                _progress.Report(_total);
            }

            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Do not dispose inner — caller owns the FileStream.
            base.Dispose(disposing);
        }
    }
}
