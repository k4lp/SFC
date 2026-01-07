using SalesforceCore.Services.Data;

namespace SalesforceCore.Services.Files;

/// <summary>
/// High-level service for file operations in Salesforce.
/// Simplifies uploading, downloading, and managing ContentVersion/ContentDocumentLink records.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Uploads a file and links it to a Salesforce record.
    /// Handles ContentVersion creation and automatic ContentDocumentLink creation.
    /// </summary>
    /// <param name="parentId">The Salesforce record ID to link the file to (e.g., Account, Contact, Case).</param>
    /// <param name="fileName">The name of the file including extension (e.g., "document.pdf").</param>
    /// <param name="content">The file content as a byte array.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ContentVersion ID.</returns>
    Task<string> UploadAsync(
        string parentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file from a stream and links it to a Salesforce record.
    /// </summary>
    /// <param name="parentId">The Salesforce record ID to link the file to.</param>
    /// <param name="fileName">The name of the file including extension.</param>
    /// <param name="stream">The file content as a stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ContentVersion ID.</returns>
    Task<string> UploadAsync(
        string parentId,
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file's content by ContentVersion ID.
    /// </summary>
    /// <param name="contentVersionId">The ContentVersion ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content as a byte array.</returns>
    Task<byte[]> DownloadAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file to a stream by ContentVersion ID.
    /// </summary>
    /// <param name="contentVersionId">The ContentVersion ID.</param>
    /// <param name="destinationStream">The stream to write to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DownloadToStreamAsync(
        string contentVersionId,
        Stream destinationStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all files attached to a Salesforce record.
    /// </summary>
    /// <param name="parentId">The Salesforce record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of attached files with metadata.</returns>
    Task<List<AttachedFile>> GetFilesAsync(
        string parentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets file metadata by ContentVersion ID.
    /// </summary>
    /// <param name="contentVersionId">The ContentVersion ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File metadata.</returns>
    Task<FileMetadata?> GetFileMetadataAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file by ContentDocument ID.
    /// This removes the file from all linked records.
    /// </summary>
    /// <param name="contentDocumentId">The ContentDocument ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string contentDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an existing ContentDocument to a Salesforce record.
    /// </summary>
    /// <param name="contentDocumentId">The ContentDocument ID to link.</param>
    /// <param name="linkedEntityId">The Salesforce record ID to link to.</param>
    /// <param name="shareType">The sharing type (V=Viewer, C=Collaborator, I=Inferred).</param>
    /// <param name="visibility">The visibility (AllUsers, InternalUsers, SharedUsers).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ContentDocumentLink ID.</returns>
    Task<string> LinkFileAsync(
        string contentDocumentId,
        string linkedEntityId,
        string shareType = "V",
        string visibility = "AllUsers",
        CancellationToken cancellationToken = default);
}

/// <summary>
/// File metadata returned from Salesforce.
/// </summary>
public class FileMetadata
{
    /// <summary>
    /// The ContentVersion ID.
    /// </summary>
    public string ContentVersionId { get; set; } = string.Empty;

    /// <summary>
    /// The ContentDocument ID.
    /// </summary>
    public string ContentDocumentId { get; set; } = string.Empty;

    /// <summary>
    /// The file title (without extension).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The file extension (without dot).
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// The full file name (title + extension).
    /// </summary>
    public string FileName => string.IsNullOrEmpty(FileExtension)
        ? Title
        : $"{Title}.{FileExtension}";

    /// <summary>
    /// The file size in bytes.
    /// </summary>
    public long ContentSize { get; set; }

    /// <summary>
    /// The MIME type of the file.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// When the file was created.
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// When the file was last modified.
    /// </summary>
    public DateTime LastModifiedDate { get; set; }

    /// <summary>
    /// The ID of the user who created the file.
    /// </summary>
    public string? CreatedById { get; set; }

    /// <summary>
    /// The version number.
    /// </summary>
    public int VersionNumber { get; set; }
}
