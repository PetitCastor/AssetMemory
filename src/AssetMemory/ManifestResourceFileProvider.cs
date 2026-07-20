using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace AssetMemory;

/// <summary>
/// Serves embedded resources whose <c>LogicalName</c> exactly matches the request subpath. The
/// built-in <c>EmbeddedFileProvider</c> assumes every resource name is prefixed with the assembly's
/// root namespace and strips that prefix on lookup; every <c>EmbeddedResource</c> in this project
/// sets an explicit flat <c>LogicalName</c> instead (see the csproj), so this looks resources up
/// directly with no namespace guessing.
/// </summary>
internal sealed class ManifestResourceFileProvider(Assembly assembly) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath)
    {
        var name = subpath.TrimStart('/');
        var stream = assembly.GetManifestResourceStream(name);
        return stream is null ? new NotFoundFileInfo(name) : new ManifestResourceFileInfo(name, stream);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class ManifestResourceFileInfo(string name, Stream stream) : IFileInfo
    {
        // StaticFileMiddleware calls .ToFileTime() on this for the Last-Modified/ETag headers,
        // which throws for DateTimeOffset.MinValue (the default) -- any valid, stable value works
        // since these are immutable per build; process start time is as good as any.
        private static readonly DateTimeOffset ProcessStart = DateTimeOffset.UtcNow;

        public bool Exists => true;
        public long Length => stream.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => ProcessStart;
        public bool IsDirectory => false;

        public Stream CreateReadStream()
        {
            stream.Position = 0;
            return stream;
        }
    }
}
