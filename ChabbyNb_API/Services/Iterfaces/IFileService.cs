// In Services folder, create FileService.cs
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Services.Iterfaces
{
    public interface IFileService
    {
        Task<string> SaveMediaFileAsync(IFormFile file, string subDirectory);
        bool IsValidMediaFile(IFormFile file, long maxSizeMB = 10);
        string GetContentType(IFormFile file);
    }

    public class FileService : IFileService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<FileService> _logger;

        public FileService(IWebHostEnvironment webHostEnvironment, ILogger<FileService> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        public async Task<string> SaveMediaFileAsync(IFormFile file, string subDirectory)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("No file provided");
            }

            string contentType = GetContentType(file);

            // Determine the subdirectory structure
            string[] pathSegments = subDirectory.Split('/', '\\');
            string uploadsFolder = _webHostEnvironment.WebRootPath;

            // Create each directory level in the path
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

            // Create a unique file name to prevent collisions
            string uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            try
            {
                // Save the file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Calculate relative URL path
                string relativePath = "/" + string.Join("/", pathSegments) + "/" + uniqueFileName;
                return relativePath.Replace("//", "/"); // Ensure no double slashes
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving file {file.FileName} to {filePath}");
                throw new InvalidOperationException($"Could not save file: {ex.Message}", ex);
            }
        }

        public bool IsValidMediaFile(IFormFile file, long maxSizeMB = 10)
        {
            if (file == null || file.Length == 0)
            {
                return false;
            }

            // Check file size (10MB max by default)
            if (file.Length > maxSizeMB * 1024 * 1024)
            {
                return false;
            }

            // Check file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                return false;
            }

            return true;
        }

        public string GetContentType(IFormFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            switch (extension)
            {
                case ".mp4":
                    return "video";
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".gif":
                    return "image";
                case ".pdf":
                    return "document";
                default:
                    return "file";
            }
        }
    }
}