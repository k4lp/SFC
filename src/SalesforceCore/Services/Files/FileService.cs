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
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(
        string parentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        RequireId(parentId, nameof(parentId), "Parent ID");
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
        // Strip any client-side directory part; fall back to the full name for dot-files like ".env".
        var clientName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(clientName)) clientName = fileName.Trim();
        var title = Path.GetFileNameWithoutExtension(clientName);
        if (string.IsNullOrWhiteSpace(title)) title = clientName;

        // Create ContentVersion with FirstPublishLocationId to auto-create ContentDocumentLink
        var payload = new
        {
            Title = title,
            PathOnClient = clientName,
            VersionData = base64Content,
            FirstPublishLocationId = parentId
        };

        var result = await _client.PostAsync<CreateResult>("/sobjects/ContentVersion/", payload, cancellationToken);
        EnsureCreated(result, "ContentVersion upload");


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

        if (stream.CanSeek && stream.Length - stream.Position > MaxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed ({MaxFileSize / (1024 * 1024)}MB)", nameof(stream));

        // Copy with a hard cap so a non-seekable (network) stream cannot be buffered without bound.
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memoryStream.Length + read > MaxFileSize)
                throw new ArgumentException($"File size exceeds maximum allowed ({MaxFileSize / (1024 * 1024)}MB)", nameof(stream));
            memoryStream.Write(buffer, 0, read);
        }

        return await UploadAsync(parentId, fileName, memoryStream.ToArray(), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default)
    {
        RequireId(contentVersionId, nameof(contentVersionId), "ContentVersion ID");

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
        await destinationStream.WriteAsync(content.AsMemory(), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<AttachedFile>> GetFilesAsync(
        string parentId,
        CancellationToken cancellationToken = default)
    {
        RequireId(parentId, nameof(parentId), "Parent ID");

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

        if (result?.Records is not { Count: > 0 } || result.Records[0] is null)
            return null;

        var record = result.Records[0];
        return new FileMetadata
        {
            ContentVersionId = record["Id"]?.ToString() ?? string.Empty,
            ContentDocumentId = record["ContentDocumentId"]?.ToString() ?? string.Empty,
            Title = record["Title"]?.ToString() ?? string.Empty,
            FileExtension = record["FileExtension"]?.ToString() ?? string.Empty,
            ContentSize = ReadLong(record["ContentSize"]) ?? 0,
            ContentType = record["FileType"]?.ToString(),
            CreatedDate = record["CreatedDate"].ParseDateTimeOrDefault(DateTime.MinValue),
            LastModifiedDate = record["LastModifiedDate"].ParseDateTimeOrDefault(DateTime.MinValue),
            CreatedById = record["CreatedById"]?.ToString(),
            // VersionNumber is a string field in Salesforce ("1"); GetValue<int>() threw on real responses.
            VersionNumber = (int?)ReadLong(record["VersionNumber"]) ?? 1
        };
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string contentDocumentId,
        CancellationToken cancellationToken = default)
    {
        RequireId(contentDocumentId, nameof(contentDocumentId), "ContentDocument ID");

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
        RequireId(contentDocumentId, nameof(contentDocumentId), "ContentDocument ID");
        RequireId(linkedEntityId, nameof(linkedEntityId), "Linked Entity ID");

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
        EnsureCreated(result, "ContentDocumentLink create");


        _logger.LogInformation("Successfully linked ContentDocument {ContentDocumentId} to {LinkedEntityId}",
            contentDocumentId, linkedEntityId);

        return result.Id;
    }

    /// <summary>Ids are interpolated into REST paths, so anything but a 15/18-char Salesforce id is rejected.</summary>
    private static void RequireId(string? id, string paramName, string label)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException($"{label} is required", paramName);
        if (!SecurityUtils.IsValidSalesforceId(id))
            throw new ArgumentException($"Invalid {label} format: {id}", paramName);
    }

    private void EnsureCreated(CreateResult? result, string operation)
    {
        if (result is null)
            throw new Models.Errors.SalesforceException($"{operation} returned an empty response.");
        if (!result.Success || string.IsNullOrEmpty(result.Id))
        {
            _logger.LogError("{Operation} failed: {Errors}", operation,
                string.Join(", ", (result.Errors ?? new()).Select(e => e.Message)));
            if (result.Errors is { Count: > 0 })
                throw Models.Errors.SalesforceException.FromErrors(result.Errors);
            throw new Models.Errors.SalesforceException($"{operation} did not succeed and returned no error details.");
        }
    }

    internal static long? ReadLong(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<string>(out var str) &&
            long.TryParse(str, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }
}
