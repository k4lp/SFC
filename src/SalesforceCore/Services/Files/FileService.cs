using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Files;

/// <summary>
/// Implementation of high-level file operations for Salesforce.
/// Simplifies working with ContentVersion, ContentDocument, and ContentDocumentLink.
/// </summary>
public class FileService : IFileService
{
    private readonly ISalesforceClient _client;
    private readonly IDataService _dataService;
    private readonly ILogger<FileService> _logger;

    /// <summary>
    /// Maximum file size for upload (25MB Salesforce limit).
    /// </summary>
    private const long MaxFileSize = 25 * 1024 * 1024;

    /// <summary>
    /// Creates a new FileService.
    /// </summary>
    public FileService(
        ISalesforceClient client,
        IDataService dataService,
        ILogger<FileService> logger)
    {
        _client = client;
        _dataService = dataService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string parentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            throw new ArgumentException("Parent ID is required", nameof(parentId));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required", nameof(fileName));
        if (content == null || content.Length == 0)
            throw new ArgumentException("File content is required", nameof(content));
        if (content.Length > MaxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed ({MaxFileSize / (1024 * 1024)}MB)", nameof(content));

        _logger.LogDebug("Uploading file {FileName} ({Size} bytes) to record {ParentId}",
            fileName, content.Length, parentId);

        // Convert to Base64
        var base64Content = Convert.ToBase64String(content);
        var title = Path.GetFileNameWithoutExtension(fileName);

        // Create ContentVersion with FirstPublishLocationId to auto-create ContentDocumentLink
        var payload = new
        {
            Title = title,
            PathOnClient = fileName,
            VersionData = base64Content,
            FirstPublishLocationId = parentId
        };

        var result = await _client.PostAsync<CreateResult>("/sobjects/ContentVersion/", payload, cancellationToken);

        if (!result.Success && result.Errors.Count > 0)
        {
            _logger.LogError("Failed to upload file {FileName}: {Errors}",
                fileName, string.Join(", ", result.Errors.Select(e => e.Message)));
            throw Models.Errors.SalesforceException.FromErrors(result.Errors);
        }

        _logger.LogInformation("Successfully uploaded file {FileName} as ContentVersion {ContentVersionId}",
            fileName, result.Id);

        return result.Id;
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string parentId,
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return await UploadAsync(parentId, fileName, memoryStream.ToArray(), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentVersionId))
            throw new ArgumentException("ContentVersion ID is required", nameof(contentVersionId));

        _logger.LogDebug("Downloading file content for ContentVersion {ContentVersionId}", contentVersionId);

        return await _client.GetBytesAsync(
            $"/sobjects/ContentVersion/{contentVersionId}/VersionData",
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DownloadToStreamAsync(
        string contentVersionId,
        Stream destinationStream,
        CancellationToken cancellationToken = default)
    {
        if (destinationStream == null)
            throw new ArgumentNullException(nameof(destinationStream));

        var content = await DownloadAsync(contentVersionId, cancellationToken);
        await destinationStream.WriteAsync(content, 0, content.Length, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<AttachedFile>> GetFilesAsync(
        string parentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            throw new ArgumentException("Parent ID is required", nameof(parentId));

        return await _dataService.GetAttachedFilesAsync(parentId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<FileMetadata?> GetFileMetadataAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentVersionId))
            throw new ArgumentException("ContentVersion ID is required", nameof(contentVersionId));

        // Validate ID format BEFORE building query to prevent any injection attempts
        if (!SecurityUtils.IsValidSalesforceId(contentVersionId))
            throw new ArgumentException($"Invalid ContentVersion ID format: {contentVersionId}", nameof(contentVersionId));

        var soql = SoqlBuilder.From("ContentVersion")
            .Select(
                "Id",
                "ContentDocumentId",
                "Title",
                "FileExtension",
                "ContentSize",
                "FileType",
                "CreatedDate",
                "LastModifiedDate",
                "CreatedById",
                "VersionNumber")
            .WhereEquals("Id", contentVersionId)
            .Build();

        var result = await _dataService.QueryAsync(soql, cancellationToken);

        if (result.Records.Count == 0)
            return null;

        var record = result.Records[0];
        return new FileMetadata
        {
            ContentVersionId = record["Id"]?.ToString() ?? string.Empty,
            ContentDocumentId = record["ContentDocumentId"]?.ToString() ?? string.Empty,
            Title = record["Title"]?.ToString() ?? string.Empty,
            FileExtension = record["FileExtension"]?.ToString() ?? string.Empty,
            ContentSize = record["ContentSize"]?.GetValue<long>() ?? 0,
            ContentType = record["FileType"]?.ToString(),
            CreatedDate = record["CreatedDate"].ParseDateTimeOrDefault(DateTime.MinValue),
            LastModifiedDate = record["LastModifiedDate"].ParseDateTimeOrDefault(DateTime.MinValue),
            CreatedById = record["CreatedById"]?.ToString(),
            VersionNumber = record["VersionNumber"]?.GetValue<int>() ?? 1
        };
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string contentDocumentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentDocumentId))
            throw new ArgumentException("ContentDocument ID is required", nameof(contentDocumentId));

        _logger.LogDebug("Deleting ContentDocument {ContentDocumentId}", contentDocumentId);

        await _client.DeleteAsync($"/sobjects/ContentDocument/{contentDocumentId}", cancellationToken);

        _logger.LogInformation("Successfully deleted ContentDocument {ContentDocumentId}", contentDocumentId);
    }

    /// <inheritdoc/>
    public async Task<string> LinkFileAsync(
        string contentDocumentId,
        string linkedEntityId,
        string shareType = "V",
        string visibility = "AllUsers",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentDocumentId))
            throw new ArgumentException("ContentDocument ID is required", nameof(contentDocumentId));
        if (string.IsNullOrWhiteSpace(linkedEntityId))
            throw new ArgumentException("Linked Entity ID is required", nameof(linkedEntityId));

        _logger.LogDebug("Linking ContentDocument {ContentDocumentId} to record {LinkedEntityId}",
            contentDocumentId, linkedEntityId);

        var payload = new
        {
            ContentDocumentId = contentDocumentId,
            LinkedEntityId = linkedEntityId,
            ShareType = shareType,
            Visibility = visibility
        };

        var result = await _client.PostAsync<CreateResult>("/sobjects/ContentDocumentLink/", payload, cancellationToken);

        if (!result.Success && result.Errors.Count > 0)
        {
            _logger.LogError("Failed to link ContentDocument {ContentDocumentId} to {LinkedEntityId}: {Errors}",
                contentDocumentId, linkedEntityId, string.Join(", ", result.Errors.Select(e => e.Message)));
            throw Models.Errors.SalesforceException.FromErrors(result.Errors);
        }

        _logger.LogInformation("Successfully linked ContentDocument {ContentDocumentId} to {LinkedEntityId}",
            contentDocumentId, linkedEntityId);

        return result.Id;
    }
}
