using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Donysh.Landing.Infrastructure;

public sealed class LandingAssetUrlBuilder(
    IFileVersionProvider fileVersionProvider,
    IWebHostEnvironment environment)
{
    private readonly ConcurrentDictionary<string, string> sharedAssetVersions = new();

    public string Versioned(string publicPath, string webRootPath, PathString pathBase)
    {
        var versionedWebRootPath = fileVersionProvider.AddFileVersionToPath(pathBase, webRootPath);
        var queryIndex = versionedWebRootPath.IndexOf('?', StringComparison.Ordinal);

        if (queryIndex >= 0)
            return string.Concat(publicPath, versionedWebRootPath.AsSpan(queryIndex));

        // Local runs serve linked product images from the Finance project rather than
        // from this project's wwwroot, so MVC's file version provider cannot find them.
        if (!webRootPath.StartsWith("/images/", StringComparison.Ordinal))
            return publicPath;

        var file = new FileInfo(Path.GetFullPath(Path.Combine(
            environment.ContentRootPath, "..", "HesabYar.Web", "wwwroot", "images",
            Path.GetFileName(webRootPath))));
        if (!file.Exists)
            return publicPath;

        var cacheKey = $"{file.FullName}:{file.LastWriteTimeUtc.Ticks}:{file.Length}";
        var version = sharedAssetVersions.GetOrAdd(cacheKey, _ =>
        {
            using var stream = file.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream));
        });
        return $"{publicPath}?v={version}";
    }
}
