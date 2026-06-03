using Microsoft.AspNetCore.Http;

namespace survey.Infrastructure;

public static class FileUploadExtensions
{
    public static void SaveAs(this IFormFile file, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        file.CopyTo(stream);
    }
}