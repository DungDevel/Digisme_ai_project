using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PosApi.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Lưu file vào thư mục chỉ định và trả về đường dẫn tương đối.
    /// </summary>
    Task<string> SaveFileAsync(IFormFile file, string folderName);

    /// <summary>
    /// Xóa file vật lý khỏi ổ cứng.
    /// </summary>
    Task DeleteFileAsync(string relativePath);

    /// <summary>
    /// Chuyển đường dẫn tương đối thành URL tuyệt đối.
    /// </summary>
    string GetAbsoluteUrl(string relativePath);
}

public class FileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileStorageService> _logger;
    private const string BaseUploadFolder = "uploads";

    public FileStorageService(IWebHostEnvironment env, ILogger<FileStorageService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string> SaveFileAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ hoặc trống.");

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension) || !file.ContentType.StartsWith("image/"))
            throw new BusinessRuleException("Chỉ cho phép upload file hình ảnh (jpg, jpeg, png, webp, gif).");

        // 1. Tạo đường dẫn thư mục: wwwroot/uploads/{folderName}
        var uploadsPath = Path.Combine(_env.WebRootPath ?? "wwwroot", BaseUploadFolder, folderName);
        if (!Directory.Exists(uploadsPath))
        {
            Directory.CreateDirectory(uploadsPath);
        }

        // 2. Tạo tên file duy nhất để tránh trùng lặp: GUID_FileName.ext
        var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var filePath = Path.Combine(uploadsPath, fileName);

        // 3. Lưu file vật lý
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 4. Trả về đường dẫn tương đối để lưu vào DB
        return Path.Combine(BaseUploadFolder, folderName, fileName).Replace('\\', '/');
    }

    public async Task DeleteFileAsync(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        var filePath = Path.Combine(_env.WebRootPath ?? "wwwroot", relativePath);

        if (File.Exists(filePath))
        {
            try
            {
                await Task.Run(() => File.Delete(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa file tại: {Path}", filePath);
            }
        }
    }

    public string GetAbsoluteUrl(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return string.Empty;

        // Trong thực tế, Host sẽ được lấy từ appsettings hoặc Request
        // Ở đây tạm thời dùng một giá trị mặc định hoặc giả định frontend biết base url
        return $"/static/{relativePath}";
    }
}
