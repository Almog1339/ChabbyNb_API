using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using System;
using System.IO;
using System.Threading.Tasks;
using System.IO.Compression;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Interface for file storage operations
    /// </summary>
    public interface IFileStorageService
    {
        /// <summary>
        /// Saves a file to storage and returns the relative URL
        /// </summary>
        Task<string> SaveFileAsync(IFormFile file, string subDirectory);

        /// <summary>
        /// Deletes a file from storage
        /// </summary>
        Task<bool> DeleteFileAsync(string fileUrl);

        /// <summary>
        /// Validates if a file meets the required criteria
        /// </summary>
        bool ValidateFile(IFormFile file, string[] allowedExtensions = null, long maxSizeBytes = 10485760);

        /// <summary>
        /// Gets the content type of a file
        /// </summary>
        string GetContentType(IFormFile file);

        /// <summary>
        /// Optimizes an image file (resizing, compression)
        /// </summary>
        Task<byte[]> OptimizeImageAsync(IFormFile imageFile, int maxWidth = 1920, int maxHeight = 1080, int quality = 85);
    }

    /// <summary>
    /// Implementation for local file storage
    /// </summary>
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<LocalFileStorageService> _logger;
        private readonly string[] _defaultAllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".mp4" };

        public LocalFileStorageService(
            IWebHostEnvironment webHostEnvironment,
            ILogger<LocalFileStorageService> logger)
        {
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Saves a file to local storage and returns the relative URL
        /// </summary>
        public async Task<string> SaveFileAsync(IFormFile file, string subDirectory)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("No file provided");
            }

            // Ensure valid content type
            string contentType = GetContentType(file);

            // Create the full directory path
            string uploadsFolder = _webHostEnvironment.WebRootPath;
            string[] pathSegments = subDirectory.Split('/', '\\');

            foreach (var segment in pathSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    uploadsFolder = Path.Combine(uploadsFolder, segment);
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                }
            }

            // Create a unique file name
            string uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            try
            {
                // Save the file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Return the relative URL
                string relativePath = "/" + string.Join("/", pathSegments) + "/" + uniqueFileName;
                return relativePath.Replace("//", "/"); // Normalize path
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving file {file.FileName} to {filePath}");
                throw new InvalidOperationException($"Could not save file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes a file from local storage
        /// </summary>
        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl))
            {
                return false;
            }

            try
            {
                // Get physical path from relative URL
                string filePath = GetPhysicalPath(fileUrl);

                // Check if file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"File not found at path: {filePath}");
                    return false;
                }

                // Delete the file - use await to make it truly async
                await Task.Run(() => File.Delete(filePath));

                _logger.LogInformation($"File deleted successfully: {fileUrl}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileUrl}");
                return false;
            }
        }

        /// <summary>
        /// Validates if a file meets the required criteria
        /// </summary>
        public bool ValidateFile(IFormFile file, string[] allowedExtensions = null, long maxSizeBytes = 10485760)
        {
            if (file == null || file.Length == 0)
            {
                return false;
            }

            // Check file size (10MB max by default)
            if (file.Length > maxSizeBytes)
            {
                return false;
            }

            // Use default extensions if none provided
            allowedExtensions = allowedExtensions ?? _defaultAllowedExtensions;

            // Check file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return Array.Exists(allowedExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the content type of a file based on its extension
        /// </summary>
        public string GetContentType(IFormFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            return extension switch
            {
                ".mp4" => "video",
                ".pdf" => "document",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "image",
                _ => "file"
            };
        }

        /// <summary>
        /// Optimizes an image file for web display
        /// </summary>
        public async Task<byte[]> OptimizeImageAsync(IFormFile imageFile, int maxWidth = 1920, int maxHeight = 1080, int quality = 85)
        {
            // Create a memory stream to hold the optimized image data
            using (var outputStream = new MemoryStream())
            {
                // Load the image using ImageSharp
                using (var inputStream = imageFile.OpenReadStream())
                using (var image = SixLabors.ImageSharp.Image.Load(inputStream))
                {
                    // Calculate dimensions while maintaining aspect ratio
                    int originalWidth = image.Width;
                    int originalHeight = image.Height;

                    if (originalWidth > maxWidth || originalHeight > maxHeight)
                    {
                        double widthRatio = (double)maxWidth / originalWidth;
                        double heightRatio = (double)maxHeight / originalHeight;
                        double ratio = Math.Min(widthRatio, heightRatio);

                        int newWidth = (int)(originalWidth * ratio);
                        int newHeight = (int)(originalHeight * ratio);

                        // Resize the image
                        image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                        {
                            Size = new SixLabors.ImageSharp.Size(newWidth, newHeight),
                            Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                        }));
                    }

                    // Determine the output format based on file extension
                    string extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                    // Save with appropriate format and compression
                    switch (extension)
                    {
                        case ".jpg":
                        case ".jpeg":
                            await image.SaveAsJpegAsync(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
                            {
                                Quality = quality
                            });
                            break;
                        case ".png":
                            await image.SaveAsPngAsync(outputStream, new SixLabors.ImageSharp.Formats.Png.PngEncoder
                            {
                                CompressionLevel = SixLabors.ImageSharp.Formats.Png.PngCompressionLevel.Level6
,
                            });
                            break;
                        case ".gif":
                            await image.SaveAsGifAsync(outputStream);
                            break;
                        case ".webp":
                            await image.SaveAsWebpAsync(outputStream, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
                            {
                                Quality = quality
                            });
                            break;
                        default:
                            // Default to JPEG if format is unknown
                            await image.SaveAsJpegAsync(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
                            {
                                Quality = quality
                            });
                            break;
                    }
                }

                // Return the optimized image data
                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Converts a relative URL to physical file path
        /// </summary>
        private string GetPhysicalPath(string relativeUrl)
        {
            // Remove leading slash if present
            if (relativeUrl.StartsWith("/"))
            {
                relativeUrl = relativeUrl.Substring(1);
            }

            // Combine with webroot path
            return Path.Combine(_webHostEnvironment.WebRootPath, relativeUrl);
        }
    }

    /// <summary>
    /// Factory for creating file storage service based on configuration
    /// </summary>
    public static class FileStorageFactory
    {
        public static IFileStorageService CreateFileStorage(
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            // Get storage type from configuration (defaults to local)
            string storageType = configuration["FileStorage:Type"] ?? "Local";

            // Return appropriate implementation based on configuration
            return storageType.ToLowerInvariant() switch
            {
                "local" => serviceProvider.GetRequiredService<LocalFileStorageService>(),
                // Add other storage providers as needed (Azure, AWS, etc.)
                _ => serviceProvider.GetRequiredService<LocalFileStorageService>() // Default to local
            };
        }
    }
}