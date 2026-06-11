using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Nop.Services.Configuration;

namespace EFiling.Nop.Services;

/// <summary>
/// Azure Blob Storage implementation for e-filing document files.
/// Reads connection settings from the installed Nop.Plugin.Misc.AzureBlob plugin settings.
/// Stores files under: {container}/efiling-drafts/{customerId}/{draftId}/{filename}
/// </summary>
public class EFilingBlobService : IEFilingBlobService
{
    private readonly ISettingService _settingService;
    private BlobContainerClient? _containerClient;
    private string _endpoint = string.Empty;
    private string _containerName = string.Empty;
    private bool _appendContainer;
    private bool _initialized;
    private readonly object _lock = new();

    private const string PREFIX = "efiling-drafts";

    public EFilingBlobService(ISettingService settingService)
    {
        _settingService = settingService;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        // Read AzureBlobSettings from nopCommerce settings table (same settings the plugin uses)
        var connectionString = await _settingService.GetSettingByKeyAsync<string>("azureblobsettings.connectionstring");
        var containerName = await _settingService.GetSettingByKeyAsync<string>("azureblobsettings.containername");
        var endpoint = await _settingService.GetSettingByKeyAsync<string>("azureblobsettings.endpoint");
        var appendContainer = await _settingService.GetSettingByKeyAsync<bool>("azureblobsettings.appendcontainername");

        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Azure Blob Storage connection string is not configured. Go to Admin > Azure Blob > Configure.");
        if (string.IsNullOrEmpty(containerName))
            throw new InvalidOperationException("Azure Blob Storage container name is not configured.");

        lock (_lock)
        {
            if (_initialized) return;

            _containerName = containerName.Trim().ToLowerInvariant();
            _endpoint = (endpoint ?? "").Trim().TrimEnd('/');
            _appendContainer = appendContainer;

            var serviceClient = new BlobServiceClient(connectionString);
            _containerClient = serviceClient.GetBlobContainerClient(_containerName);
            try
            {
                _containerClient.CreateIfNotExists(PublicAccessType.None);
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "PublicAccessNotPermitted")
            {
                // Storage account has public access disabled at account level — container already exists
            }

            _initialized = true;
        }
    }

    public async Task<BlobFileInfo> UploadAsync(int customerId, string draftId, string fileName, string contentType, Stream stream, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();

        // Sanitize filename — keep only safe chars
        var safeName = SanitizeFileName(fileName);
        var blobPath = $"{PREFIX}/{customerId}/{draftId}/{safeName}";

        var blobClient = _containerClient!.GetBlobClient(blobPath);

        var headers = new BlobHttpHeaders { ContentType = contentType };
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, ct);

        return new BlobFileInfo
        {
            BlobPath = blobPath,
            BlobUrl = BuildUrl(blobPath),
            FileName = fileName,
            FileSize = stream.Length,
            ContentType = contentType
        };
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var blobClient = _containerClient!.GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
    }

    public async Task DeleteDraftFolderAsync(int customerId, string draftId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var prefix = $"{PREFIX}/{customerId}/{draftId}/";

        await foreach (var blob in _containerClient!.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
        {
            await _containerClient.DeleteBlobIfExistsAsync(blob.Name, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
    }

    public string GetBlobUrl(string blobPath)
    {
        if (!_initialized)
            return string.Empty;
        return BuildUrl(blobPath);
    }

    public async Task<Stream?> DownloadAsync(string blobPath, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        var blobClient = _containerClient!.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync(ct))
            return null;

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    private string BuildUrl(string blobPath)
    {
        var containerPart = _appendContainer ? $"{_containerName}/" : "";
        return $"{_endpoint}/{containerPart}{blobPath}";
    }

    private static string SanitizeFileName(string fileName)
    {
        // Keep alphanumeric, dots, dashes, underscores; replace spaces with underscores
        var safe = fileName.Replace(' ', '_');
        var chars = safe.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_').ToArray();
        var result = new string(chars);
        return string.IsNullOrEmpty(result) ? "file" : result;
    }
}
