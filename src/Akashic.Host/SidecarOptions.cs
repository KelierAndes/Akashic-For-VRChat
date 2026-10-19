using System.Security.Cryptography;

namespace Akashic.Host;

public sealed record SidecarOptions(int Port, string Token, int? ParentPid)
{
    public const string TokenHeader = "X-Akashic-Token";

    public static SidecarOptions Read(string[] args)
    {
        var port = GetInt(args, "--port", "AKASHIC_HOST_PORT") ?? 0;
        var parent = GetInt(args, "--parent-pid", "AKASHIC_HOST_PARENT");
        var token = GetString(args, "--token")
            ?? Environment.GetEnvironmentVariable("AKASHIC_HOST_TOKEN")
            ?? NewToken();

        return new SidecarOptions(port, token, parent);
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int? GetInt(string[] args, string switchName, string environmentName)
    {
        var raw = GetString(args, switchName) ?? Environment.GetEnvironmentVariable(environmentName);
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static string? GetString(string[] args, string switchName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], switchName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
