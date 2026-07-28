using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public interface IReferenceAudioStore
{
    Task<byte[]?> TryReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string objectKey,
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken = default);
}

public sealed class FileReferenceAudioStore(
    IWebHostEnvironment environment,
    IOptions<AudioStorageOptions> options) : IReferenceAudioStore
{
    private readonly string _root = Path.GetFullPath(Path.Combine(
        environment.ContentRootPath,
        options.Value.CachePath));

    public async Task<byte[]?> TryReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(objectKey);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    public async Task WriteAsync(
        string objectKey,
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(
            GetPath(objectKey),
            audio.ToArray(),
            cancellationToken);
    }

    private string GetPath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)
            || objectKey.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "Audio object keys must contain hexadecimal characters only.",
                nameof(objectKey));
        }

        var path = Path.GetFullPath(Path.Combine(_root, $"{objectKey}.mp3"));
        if (!path.StartsWith(
                _root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The audio object key resolved outside the configured cache.");
        }

        return path;
    }
}

public sealed class AzureBlobReferenceAudioStore : IReferenceAudioStore
{
    private readonly BlobContainerClient _container;
    private readonly AudioStorageOptions _options;

    public AzureBlobReferenceAudioStore(
        IConfiguration configuration,
        IOptions<AudioStorageOptions> options)
    {
        _options = options.Value;
        var connectionString = configuration.GetConnectionString(
            _options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{_options.ConnectionStringName}' is required "
                + "when AudioStorage:Provider is AzureBlob.");
        }

        _container = new BlobContainerClient(
            connectionString,
            _options.ContainerName);
    }

    public async Task<byte[]?> TryReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(GetBlobName(objectKey));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task WriteAsync(
        string objectKey,
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken = default)
    {
        if (_options.CreateContainerIfMissing)
        {
            await _container.CreateIfNotExistsAsync(
                PublicAccessType.None,
                cancellationToken: cancellationToken);
        }

        var blob = _container.GetBlobClient(GetBlobName(objectKey));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromBytes(audio),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "audio/mpeg",
                        CacheControl = "public, max-age=31536000, immutable"
                    },
                    Conditions = new BlobRequestConditions
                    {
                        IfNoneMatch = ETag.All
                    }
                },
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            // Another instance won the idempotent cache write.
        }
    }

    private string GetBlobName(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)
            || objectKey.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "Audio object keys must contain hexadecimal characters only.",
                nameof(objectKey));
        }

        return $"{_options.ObjectPrefix.Trim('/')}/{objectKey}.mp3";
    }
}
