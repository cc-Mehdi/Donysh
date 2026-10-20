using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Donysh.Landing.Infrastructure;

public sealed class LandingAssetUrlBuilder(IFileVersionProvider fileVersionProvider)
{
    public string Versioned(string publicPath, string webRootPath, PathString pathBase)
    {
        var versionedWebRootPath = fileVersionProvider.AddFileVersionToPath(pathBase, webRootPath);
        var queryIndex = versionedWebRootPath.IndexOf('?', StringComparison.Ordinal);

        return queryIndex >= 0
            ? string.Concat(publicPath, versionedWebRootPath.AsSpan(queryIndex))
            : publicPath;
    }
}
