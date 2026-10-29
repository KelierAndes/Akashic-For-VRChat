using Akashic.Core.Services.Preview;
using Akashic.Core.Services.Preview.Shader;
using Xunit;

namespace Akashic.Tests;

public class PreviewCachePathsTests
{
    [Fact]
    public void VersionedUnderReclaimsOnlyTheSameFamily()
    {
        var root = Directory.CreateTempSubdirectory("akashic-cache-");
        try
        {
            var first = PreviewCachePaths.VersionedUnder(root.FullName, "hlsl", "hlsl-v1");
            var shim = Path.Combine(root.FullName, "hlsl", "shim-v6");
            Directory.CreateDirectory(shim);

            var second = PreviewCachePaths.VersionedUnder(root.FullName, "hlsl", "hlsl-v2");

            Assert.True(Directory.Exists(second));
            Assert.False(Directory.Exists(first));
            Assert.True(Directory.Exists(shim));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void ShimSetupReclaimsItsOwnFamilyAndSparesTheCompileCache()
    {
        var root = Directory.CreateTempSubdirectory("akashic-cache-");
        try
        {
            var container = Path.Combine(root.FullName, "hlsl");
            Directory.CreateDirectory(Path.Combine(container, "shim-v6"));
            Directory.CreateDirectory(Path.Combine(container, "hlsl-v1"));

            var shim = ShaderEnvironment.Ensure(root.FullName);

            Assert.Equal(ShaderEnvironment.Version, Path.GetFileName(shim));
            Assert.True(File.Exists(Path.Combine(shim, "Standard.shader")));
            Assert.True(Directory.Exists(Path.Combine(container, "hlsl-v1")));

            var families = Directory.EnumerateDirectories(container)
                .Select(Path.GetFileName)
                .Where(name => name!.StartsWith("shim-", StringComparison.Ordinal))
                .ToArray();

            Assert.Equivalent(new[] { ShaderEnvironment.Version }, families);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void QuotaIsClampedAndSurvivesADirectoryChange()
    {
        var root = Directory.CreateTempSubdirectory("akashic-cache-");
        try
        {
            PreviewCachePaths.Configure(root.FullName, 1);
            Assert.Equal(PreviewCachePaths.MinQuotaBytes, PreviewCachePaths.Quota);

            PreviewCachePaths.Configure(root.FullName, long.MaxValue);
            Assert.Equal(PreviewCachePaths.MaxQuotaBytes, PreviewCachePaths.Quota);

            PreviewCachePaths.ConfigureQuota(0);
            Assert.Equal(PreviewCachePaths.DefaultQuotaBytes, PreviewCachePaths.Quota);

            var four = 4L * 1024 * 1024 * 1024;
            PreviewCachePaths.ConfigureQuota(four);
            PreviewCachePaths.Configure(root.FullName);
            Assert.Equal(four, PreviewCachePaths.Quota);
        }
        finally
        {
            root.Delete(true);
            PreviewCachePaths.Configure(null, PreviewCachePaths.DefaultQuotaBytes);
        }
    }

    [Fact]
    public void EnforceQuotaDropsOldestLeavesFirst()
    {
        var root = Directory.CreateTempSubdirectory("akashic-cache-");
        try
        {
            PreviewCachePaths.Configure(root.FullName, 4L * 1024 * 1024 * 1024);

            var old = Path.Combine(root.FullName, "packages", "extract-v1", "old");
            var fresh = Path.Combine(root.FullName, "packages", "extract-v1", "fresh");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(fresh);
            File.WriteAllBytes(Path.Combine(old, "blob.bin"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(fresh, "blob.bin"), new byte[1024]);
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

            PreviewCachePaths.EnforceQuota(1500);

            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(fresh));
        }
        finally
        {
            root.Delete(true);
            PreviewCachePaths.Configure(null, PreviewCachePaths.DefaultQuotaBytes);
        }
    }

    [Fact]
    public void EnforceQuotaKeepsEverythingUnderTheLimit()
    {
        var root = Directory.CreateTempSubdirectory("akashic-cache-");
        try
        {
            PreviewCachePaths.Configure(root.FullName, 4L * 1024 * 1024 * 1024);

            var leaf = Path.Combine(root.FullName, "builds", "build-v1", "abc");
            Directory.CreateDirectory(leaf);
            File.WriteAllBytes(Path.Combine(leaf, "model.glb"), new byte[512]);

            PreviewCachePaths.EnforceQuota(64 * 1024);

            Assert.True(Directory.Exists(leaf));
        }
        finally
        {
            root.Delete(true);
            PreviewCachePaths.Configure(null, PreviewCachePaths.DefaultQuotaBytes);
        }
    }
}
