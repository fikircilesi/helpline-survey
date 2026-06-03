using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace survey.Infrastructure;

public abstract class LegacyController : Controller
{
    protected LegacySession Session => new(HttpContext.Session);

    protected string GetClientIp()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    protected string MapPath(string virtualPath)
    {
        var environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var relativePath = virtualPath
            .Replace("~", string.Empty)
            .TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(environment.WebRootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }
}