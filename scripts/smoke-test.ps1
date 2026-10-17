# Installs the packed tool from a local folder and checks that it can launch an app through FlaUI.
# Run after `dotnet pack`. Needs an interactive Windows desktop session.
param(
    [string]$PackageSource = "artifacts",
    [string]$Version,
    [string]$AppExe = "$env:WINDIR\System32\winver.exe"
)

$ErrorActionPreference = "Stop"

$toolPath = Join-Path ([IO.Path]::GetTempPath()) "wpf-mcp-smoke"
if (Test-Path $toolPath) { cmd /c rmdir /s /q "$toolPath" }

$installArgs = @("tool", "install", "Meteion.WpfTesting.Mcp", "--tool-path", $toolPath, "--add-source", $PackageSource)
if ($Version) { $installArgs += @("--version", $Version) }
dotnet @installArgs
if ($LASTEXITCODE -ne 0) { throw "Tool install failed." }

$psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $toolPath "meteion-wpf-mcp.exe")
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)

function Send($o) { $p.StandardInput.WriteLine(($o | ConvertTo-Json -Depth 8 -Compress)); $p.StandardInput.Flush() }
function ReadLine1 {
    $t = $p.StandardOutput.ReadLineAsync()
    if ($t.Wait(30000)) { $t.Result } else { "TIMEOUT" }
}

try {
    Send @{ jsonrpc = "2.0"; id = 1; method = "initialize"; params = @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "smoke"; version = "0" } } }
    $null = ReadLine1
    Send @{ jsonrpc = "2.0"; method = "notifications/initialized" }
    Send @{ jsonrpc = "2.0"; id = 2; method = "tools/call"; params = @{ name = "launch_app"; arguments = @{ exePath = $AppExe } } }
    $reply = ReadLine1
    Write-Host $reply

    if ($reply -eq "TIMEOUT" -or $reply -match '"isError":true' -or $reply -notmatch "Launched") {
        throw "launch_app did not succeed."
    }

    Send @{ jsonrpc = "2.0"; id = 3; method = "tools/call"; params = @{ name = "close_app"; arguments = @{} } }
    $null = ReadLine1
    Write-Host "Smoke test passed."
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
}
