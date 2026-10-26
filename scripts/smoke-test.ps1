# Checks that the MCP server can launch an app through FlaUI, build and launch a WPF project with compile
# symbols, and capture the app's output. Needs an interactive Windows desktop session.
#
# Either installs the packed tool from -PackageSource (run after `dotnet pack`), or runs -ToolExe directly.
param(
    [string]$PackageSource = "artifacts",
    [string]$Version,
    [string]$ToolExe,
    [string]$AppExe = "$env:WINDIR\System32\winver.exe",
    [string]$FixtureProject = (Join-Path $PSScriptRoot "..\tests\WpfFixture\WpfFixture.csproj")
)

$ErrorActionPreference = "Stop"

if (-not $ToolExe) {
    $toolPath = Join-Path ([IO.Path]::GetTempPath()) "wpf-mcp-smoke"
    if (Test-Path $toolPath) { cmd /c rmdir /s /q "$toolPath" }

    $installArgs = @("tool", "install", "Meteion.WpfTesting.Mcp", "--tool-path", $toolPath, "--add-source", $PackageSource)
    if ($Version) { $installArgs += @("--version", $Version) }
    dotnet @installArgs
    if ($LASTEXITCODE -ne 0) { throw "Tool install failed." }
    $ToolExe = Join-Path $toolPath "meteion-wpf-mcp.exe"
}

$FixtureProject = (Resolve-Path $FixtureProject).Path

$psi = New-Object System.Diagnostics.ProcessStartInfo $ToolExe
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path $FixtureProject # so launch_app can discover the fixture project
$p = [System.Diagnostics.Process]::Start($psi)
$null = $p.StandardError.ReadToEndAsync() # drain so logging can't block the server

$script:nextId = 1
function Send($o) { $p.StandardInput.WriteLine(($o | ConvertTo-Json -Depth 8 -Compress)); $p.StandardInput.Flush() }
function ReadLine1([int]$seconds = 30) {
    $t = $p.StandardOutput.ReadLineAsync()
    if ($t.Wait($seconds * 1000)) { $t.Result } else { throw "Timed out waiting for the server." }
}

# Calls a tool and returns @{ Text; IsError }.
function Call([string]$name, [hashtable]$arguments = @{}, [int]$seconds = 120) {
    $id = ++$script:nextId
    Send @{ jsonrpc = "2.0"; id = $id; method = "tools/call"; params = @{ name = $name; arguments = $arguments } }
    $reply = ReadLine1 $seconds | ConvertFrom-Json
    $result = $reply.result
    [pscustomobject]@{
        Text    = ($result.content | ForEach-Object { $_.text }) -join "`n"
        IsError = [bool]$result.isError
    }
}

function Expect($condition, [string]$message, $call) {
    if (-not $condition) {
        throw "FAILED: $message`n$($call.Text)"
    }
    Write-Host "ok: $message"
}

try {
    Send @{ jsonrpc = "2.0"; id = 1; method = "initialize"; params = @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "smoke"; version = "0" } } }
    $null = ReadLine1
    Send @{ jsonrpc = "2.0"; method = "notifications/initialized" }

    # 1. Launch a plain exe.
    $r = Call "launch_app" @{ exePath = $AppExe }
    Expect (-not $r.IsError -and $r.Text -match "Launched") "launches an exe by path" $r
    $null = Call "close_app"

    # 2. Build the fixture with a compile symbol and launch it.
    $r = Call "launch_app" @{ projectPath = $FixtureProject; defineConstants = @("AUTOMATED_TEST") }
    Expect (-not $r.IsError -and $r.Text -match "Built" -and $r.Text -match "Launched") "builds and launches a project" $r
    $text = Call "get_text" @{ selector = "ModeText" }
    Expect ($text.Text -match "automated=True") "the compile symbol reaches the app" $text
    Expect ($text.Text -match "debug=True") "the project's own DEBUG symbol is kept" $text

    # 3. Output capture: stdout, stderr and Debug.WriteLine.
    # stdout/stderr arrive through pipes, so give them a moment after the window is up.
    $out = $null
    foreach ($attempt in 1..20) {
        $out = Call "get_app_output" @{ since = 0 }
        if ($out.Text -match "fixture stdout ready" -and $out.Text -match "fixture stderr ready" -and $out.Text -match "fixture debug ready") { break }
        Start-Sleep -Milliseconds 250
    }
    Expect ($out.Text -match "\[stdout\] fixture stdout ready") "captures stdout" $out
    Expect ($out.Text -match "\[stderr\] fixture stderr ready") "captures stderr" $out
    Expect ($out.Text -match "\[debug\] fixture debug ready") "captures Debug.WriteLine" $out
    $none = Call "get_app_output"
    Expect ($none.Text -eq "No new output.") "returns only unseen lines by default" $none
    $filtered = Call "get_app_output" @{ since = 0; filter = "stderr" }
    Expect ($filtered.Text -match "stderr ready" -and $filtered.Text -notmatch "stdout ready") "filters output" $filtered

    # 4. Rebuilding without the symbol must drop it again.
    $r = Call "launch_app" @{ projectPath = $FixtureProject }
    Expect (-not $r.IsError) "rebuilds without symbols" $r
    $text = Call "get_text" @{ selector = "ModeText" }
    Expect ($text.Text -match "automated=False") "the symbol is gone after rebuilding without it" $text

    # 5. noBuild launches the existing output, and a bare launch_app discovers the project from the working directory.
    $r = Call "launch_app" @{ projectPath = $FixtureProject; noBuild = $true }
    Expect (-not $r.IsError -and $r.Text -match "Launched" -and $r.Text -notmatch "Built") "launches existing output with noBuild" $r
    $r = Call "launch_app"
    Expect (-not $r.IsError -and $r.Text -match "Built WpfFixture" -and $r.Text -match "Launched") "discovers the project from the working directory" $r
    $r = Call "launch_app" @{ projectPath = $FixtureProject; noBuild = $true; defineConstants = @("AUTOMATED_TEST") }
    Expect ($r.IsError -and $r.Text -match "needs a build") "refuses defineConstants with noBuild" $r

    # 6. The Debug-only guard.
    $r = Call "launch_app" @{ projectPath = $FixtureProject; configuration = "Release"; defineConstants = @("AUTOMATED_TEST") }
    Expect ($r.IsError -and $r.Text -match "refused") "refuses compile symbols outside Debug" $r

    $null = Call "close_app"
    Write-Host "Smoke test passed."
}
finally {
    # Close the app first: killing the server doesn't kill an app it launched.
    try { $null = Call "close_app" @{ kill = $true } 10 } catch { }
    if (-not $p.HasExited) { $p.Kill() }
}
