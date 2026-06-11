namespace EFiling.Nop.Services;

/// <summary>
/// Service for uploading, downloading, and deleting e-filing document files
/// in Azure Blob Storage. Used by the draft save/restore system.
/// </summary>
public interface IEFilingBlobService
{
    /// <summary>
    /// Upload a file to blob storage under the drafts path.
    /// </summary>
    /// <param name="customerId">Customer who owns this draft.</param>
    /// <param name="draftId">Draft identifier (use "new" for unsaved drafts).</param>
    /// <param name="fileName">Original file name (e.g., "complaint.pdf").</param>
    /// <param name="contentType">MIME type (e.g., "application/pdf").</param>
    /// <param name="stream">File content stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Blob info containing the storage path and public URL.</returns>
    Task<BlobFileInfo> UploadAsync(int customerId, string draftId, string fileName, string contentType, Stream stream, CancellationToken ct = default);

    /// <summary>
    /// Delete a single blob by its storage path.
    /// </summary>
    Task DeleteAsync(string blobPath, CancellationToken ct = default);

    /// <summary>
    /// Delete all blobs under a draft's folder.
    /// </summary>
    Task DeleteDraftFolderAsync(int customerId, string draftId, CancellationToken ct = default);

    /// <summary>
    /// Get the public URL for a blob path.
    /// </summary>
    string GetBlobUrl(string blobPath);

    /// <summary>
    /// Download a blob's content as a stream.
    /// </summary>
    Task<Stream?> DownloadAsync(string blobPath, CancellationToken ct = default);
}

/// <summary>
/// Represents an uploaded blob file.
/// </summary>
public class BlobFileInfo
{
    /// <summary>Blob path within the container (e.g., "efiling-drafts/1/42/complaint.pdf").</summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Full public URL to the blob.</summary>
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>Original file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>MIME content type.</summary>
    public string ContentType { get; set; } = string.Empty;
}
