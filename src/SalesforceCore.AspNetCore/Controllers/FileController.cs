using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SalesforceCore.Services.Data;
using SalesforceCore.Utilities;

namespace SalesforceCore.AspNetCore.Controllers;

/// <summary>
/// Controller for file operations (download, preview).
/// </summary>
[Authorize]
[Route("[controller]")]
public class FileController : Controller
{
    private readonly IDataService _dataService;
    private readonly ILogger<FileController> _logger;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "jpg", "image/jpeg" },
        { "jpeg", "image/jpeg" },
        { "png", "image/png" },
        { "gif", "image/gif" },
        { "webp", "image/webp" },
        { "bmp", "image/bmp" },
        { "pdf", "application/pdf" },
        { "doc", "application/msword" },
        { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "xls", "application/vnd.ms-excel" },
        { "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "ppt", "application/vnd.ms-powerpoint" },
        { "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { "txt", "text/plain" },
        { "csv", "text/csv" },
        { "xml", "application/xml" },
        { "json", "application/json" }
    };

    /// <summary>
    /// Creates a new FileController.
    /// </summary>
    public FileController(
        IDataService dataService,
        ILogger<FileController> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// Gets an image for inline display.
    /// </summary>
    /// <param name="versionId">ContentVersion ID.</param>
    /// <param name="type">File extension for MIME type.</param>
    [HttpGet("GetImage/{versionId}")]
    public async Task<IActionResult> GetImage(string versionId, string? type = "jpg")
    {
        if (!SecurityUtils.IsValidSalesforceId(versionId))
        {
            return BadRequest("Invalid version ID.");
        }

        try
        {
            var content = await _dataService.GetFileContentAsync(versionId);
            var mimeType = GetMimeType(type ?? "jpg");

            return File(content, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get image {VersionId}", versionId);
            return NotFound();
        }
    }

    /// <summary>
    /// Downloads a file with original filename.
    /// </summary>
    /// <param name="versionId">ContentVersion ID.</param>
    /// <param name="filename">Original filename for download.</param>
    [HttpGet("Download/{versionId}/{filename?}")]
    public async Task<IActionResult> Download(string versionId, string? filename = null)
    {
        if (!SecurityUtils.IsValidSalesforceId(versionId))
        {
            return BadRequest("Invalid version ID.");
        }

        try
        {
            var content = await _dataService.GetFileContentAsync(versionId);

            var safeFilename = SecurityUtils.SanitizeFileName(filename ?? "download");
            var extension = Path.GetExtension(safeFilename).TrimStart('.');
            var mimeType = GetMimeType(extension);

            return File(content, mimeType, safeFilename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {VersionId}", versionId);
            return NotFound();
        }
    }

    /// <summary>
    /// Previews a file in browser (for PDFs and images).
    /// </summary>
    /// <param name="versionId">ContentVersion ID.</param>
    /// <param name="type">File extension.</param>
    [HttpGet("Preview/{versionId}")]
    public async Task<IActionResult> Preview(string versionId, string? type = null)
    {
        if (!SecurityUtils.IsValidSalesforceId(versionId))
        {
            return BadRequest("Invalid version ID.");
        }

        try
        {
            var content = await _dataService.GetFileContentAsync(versionId);
            var mimeType = GetMimeType(type ?? "pdf");

            // Set content disposition to inline for preview
            Response.Headers["Content-Disposition"] = "inline";

            return File(content, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview file {VersionId}", versionId);
            return NotFound();
        }
    }

    private static string GetMimeType(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return MimeTypes.TryGetValue(ext, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}
