using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace WpfTesting.Mcp;

public sealed record BuildResult(string ExePath, string Summary);

/// <summary>Finds a WPF project, builds it with extra compile symbols, and locates the exe MSBuild produced.</summary>
public static partial class ProjectBuilder
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "packages"];

    /// <summary>Resolves a project path (a .csproj, or a folder holding exactly one) or discovers one from the working directory.</summary>
    public static string ResolveProject(string? projectPath, string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
            return ResolveGivenPath(Path.GetFullPath(projectPath, workingDirectory));

        var candidates = Discover(workingDirectory);
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new McpException(
                $"No WPF project (UseWPF + WinExe) found at or above {workingDirectory}. Pass projectPath or set WPF_MCP_PROJECT_PATH."),
            _ => throw new McpException(
                "More than one WPF project was found. Pass projectPath (or set WPF_MCP_PROJECT_PATH) with one of:\n  " +
                string.Join("\n  ", candidates)),
        };
    }

    /// <summary>Builds the project and returns the exe path. Throws an McpException carrying only the compiler errors on failure.</summary>
    public static BuildResult Build(string project, string configuration, IReadOnlyList<string> constants, TimeSpan timeout)
    {
        ValidateConfiguration(configuration);
        foreach (var constant in constants)
            ValidateConstant(constant);

        string? propsFile = null;
        try
        {
            var args = new List<string> { "build", project, "-c", configuration, "-nologo", "-v", "q", "-m:1", "-nodeReuse:false", "-clp:NoSummary" };
            if (constants.Count > 0)
            {
                propsFile = WriteConstantsProps(constants);
                args.Add($"-p:CustomAfterMicrosoftCommonTargets={propsFile}");
            }

            var stopwatch = Stopwatch.StartNew();
            var build = RunDotnet(args, Path.GetDirectoryName(project)!, timeout);
            if (build.ExitCode != 0)
                throw new McpException(FormatBuildFailure(project, build.Output));

            var exe = FindBuiltExe(project, configuration);
            var symbols = constants.Count > 0 ? $", symbols: {string.Join(", ", constants)}" : "";
            return new BuildResult(exe, $"Built {Path.GetFileName(project)} ({configuration}{symbols}) in {stopwatch.Elapsed.TotalSeconds:0.#}s.");
        }
        finally
        {
            if (propsFile is not null)
                TryDelete(propsFile);
        }
    }

    public static void ValidateConstant(string constant)
    {
        if (!ConstantPattern().IsMatch(constant))
            throw new McpException($"Invalid compile symbol '{constant}'. Use letters, digits and underscores, starting with a letter or underscore.");
    }

    private static void ValidateConfiguration(string configuration)
    {
        if (!ConfigurationPattern().IsMatch(configuration))
            throw new McpException($"Invalid configuration '{configuration}'.");
    }

    private static string ResolveGivenPath(string path)
    {
        if (File.Exists(path))
            return path;
        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*.csproj");
            if (projects.Length == 1)
                return projects[0];
            throw new McpException(projects.Length == 0
                ? $"No .csproj in {path}."
                : $"More than one .csproj in {path}. Pass the project file:\n  {string.Join("\n  ", projects)}");
        }
        throw new McpException($"Project not found: {path}");
    }

    /// <summary>Searches the working directory's tree, then each parent in turn, stopping at the first level with matches or at the repo root.</summary>
    private static List<string> Discover(string workingDirectory)
    {
        for (var dir = new DirectoryInfo(workingDirectory); dir is not null; dir = dir.Parent)
        {
            var found = new List<string>();
            Search(dir.FullName, depth: 0, found);
            if (found.Count > 0)
                return found;
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0)
                break;
        }
        return [];
    }

    private static void Search(string directory, int depth, List<string> found)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
            {
                if (IsWpfExe(file))
                    found.Add(file);
            }
            if (depth >= 4)
                return;
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                    Search(sub, depth + 1, found);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsWpfExe(string csproj)
    {
        var text = File.ReadAllText(csproj);
        return WpfPattern().IsMatch(text) && WinExePattern().IsMatch(text);
    }

    public static string FindBuiltExe(string project, string configuration)
    {
        var result = RunDotnet(
            ["msbuild", project, "-nologo", $"-p:Configuration={configuration}", "-getProperty:TargetDir", "-getProperty:AssemblyName"],
            Path.GetDirectoryName(project)!, TimeSpan.FromSeconds(60));
        if (result.ExitCode != 0)
            throw new McpException($"Could not read the build output location: {Tail(result.Output)}");

        try
        {
            using var json = JsonDocument.Parse(result.Output);
            var props = json.RootElement.GetProperty("Properties");
            var exe = Path.Combine(props.GetProperty("TargetDir").GetString()!, props.GetProperty("AssemblyName").GetString()! + ".exe");
            return File.Exists(exe)
                ? exe
                : throw new McpException($"The build succeeded but {exe} does not exist. Is this project a WinExe?");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new McpException($"Could not read the build output location: {Tail(result.Output)}");
        }
    }

    /// <summary>
    /// A props file imported after the project's own properties, so the symbols are appended to whatever
    /// DefineConstants the project already has (DEBUG, TRACE, ...) instead of replacing it.
    /// </summary>
    private static string WriteConstantsProps(IReadOnlyList<string> constants)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wpf-mcp-{Guid.NewGuid():N}.targets");
        var xml = $"""
            <Project>
              <PropertyGroup>
                <DefineConstants>$(DefineConstants);{string.Join(';', constants)}</DefineConstants>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(path, xml);
        return path;
    }

    private static (int ExitCode, string Output) RunDotnet(IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var sync = new object();
        void Append(string? line)
        {
            if (line is null)
                return;
            lock (sync)
                output.AppendLine(line);
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new McpException($"dotnet {args[0]} did not finish within {timeout.TotalSeconds:0}s and was stopped.");
        }
        process.WaitForExit(); // flush the async readers
        lock (sync)
            return (process.ExitCode, output.ToString());
    }

    private static string FormatBuildFailure(string project, string output)
    {
        var errors = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => ErrorLinePattern().IsMatch(l))
            .Distinct()
            .Take(30)
            .ToList();
        return errors.Count > 0
            ? $"Build failed for {Path.GetFileName(project)}:\n{string.Join("\n", errors)}"
            : $"Build failed for {Path.GetFileName(project)}:\n{Tail(output)}";
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(15);
        return string.Join("\n", lines);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ConstantPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial Regex ConfigurationPattern();

    [GeneratedRegex(@"<UseWPF>\s*true\s*</UseWPF>", RegexOptions.IgnoreCase)]
    private static partial Regex WpfPattern();

    [GeneratedRegex(@"<OutputType>\s*WinExe\s*</OutputType>", RegexOptions.IgnoreCase)]
    private static partial Regex WinExePattern();

    [GeneratedRegex(@"\berror [A-Za-z]+\d+", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLinePattern();
}
