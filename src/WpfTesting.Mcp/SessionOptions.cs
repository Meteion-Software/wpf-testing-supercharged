namespace WpfTesting.Mcp;

/// <summary>Server settings, read from environment variables so they can be set in the MCP client config.</summary>
public sealed record SessionOptions
{
    /// <summary>Executable launched by launch_app when no path is given (WPF_MCP_APP_PATH).</summary>
    public string? DefaultAppPath { get; init; }

    /// <summary>Project that launch_app builds and launches when no exePath is given (WPF_MCP_PROJECT_PATH).</summary>
    public string? DefaultProjectPath { get; init; }

    /// <summary>How many lines of app output the server keeps (WPF_MCP_OUTPUT_LINES).</summary>
    public int OutputBufferLines { get; init; } = 5000;

    /// <summary>How long element lookups retry before failing (WPF_MCP_TIMEOUT_MS).</summary>
    public TimeSpan FindTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Where screenshots with a relative path are written (WPF_MCP_ARTIFACTS_DIR).</summary>
    public string ArtifactsDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "TestResults", "screenshots");

    public static SessionOptions FromEnvironment()
    {
        var options = new SessionOptions
        {
            DefaultAppPath = Environment.GetEnvironmentVariable("WPF_MCP_APP_PATH"),
            DefaultProjectPath = Environment.GetEnvironmentVariable("WPF_MCP_PROJECT_PATH"),
        };
        if (int.TryParse(Environment.GetEnvironmentVariable("WPF_MCP_OUTPUT_LINES"), out var lines) && lines > 0)
            options = options with { OutputBufferLines = lines };
        if (int.TryParse(Environment.GetEnvironmentVariable("WPF_MCP_TIMEOUT_MS"), out var ms) && ms > 0)
            options = options with { FindTimeout = TimeSpan.FromMilliseconds(ms) };
        if (Environment.GetEnvironmentVariable("WPF_MCP_ARTIFACTS_DIR") is { Length: > 0 } dir)
            options = options with { ArtifactsDirectory = dir };
        return options;
    }
}
