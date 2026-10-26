# WpfTesting.Mcp

An MCP server that lets AI agents drive a WPF application through UI Automation (FlaUI UIA3) to run UI tests: launch the app, inspect the control tree, act on controls, assert results, take screenshots, and export the session as FlaUI C# for an xUnit test.

It assumes the app under test was prepared with the skills in this repo:

| Skill | What the server relies on |
|---|---|
| `add-automation-ids` | Controls have `AutomationProperties.AutomationId`, so selectors like `LoginButton` work. |
| `add-flaui-to-project` | The test project uses FlaUI.UIA3. Recorded steps use the same API. |
| `scaffold-driver-object` | Recorded steps address elements by AutomationId, which maps directly to driver properties. |
| `write-xunit-3-tests` | `get_recorded_steps` output is the starting point for a test. |

## Requirements

- Windows with an interactive desktop session (UI Automation can't drive a locked screen or a non-interactive service).
- .NET 10 SDK.
- The app under test's source. `launch_app` can build it (Debug) and launch the result, or you can point it at an exe you built yourself.

## Install

The server ships as a .NET tool, `Meteion.WpfTesting.Mcp`, with the command `meteion-wpf-mcp`.

Run it without installing (needs the .NET 10 SDK):

```powershell
dnx Meteion.WpfTesting.Mcp
```

Or install it globally:

```powershell
dotnet tool install -g Meteion.WpfTesting.Mcp
```

To build it from source instead:

```powershell
dotnet pack src/WpfTesting.Mcp -c Release -o artifacts
dotnet tool install -g Meteion.WpfTesting.Mcp --add-source artifacts
```

## Configure an MCP client

For Claude Code, add a `.mcp.json` at the root of the app's repo. With `dnx`:

```json
{
  "mcpServers": {
    "wpf-testing": {
      "command": "dnx",
      "args": ["Meteion.WpfTesting.Mcp"],
      "env": {
        "WPF_MCP_PROJECT_PATH": "src/MyApp/MyApp.csproj"
      }
    }
  }
}
```

With the global tool, use `"command": "meteion-wpf-mcp"` and drop `args`.

Don't use `dotnet run` as the command. Build output written to stdout corrupts the MCP stream.

### Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `WPF_MCP_PROJECT_PATH` | none | WPF project (`.csproj`, or a folder holding one) that `launch_app` builds and launches when no `exePath` or `projectPath` is given. |
| `WPF_MCP_APP_PATH` | none | Exe that `launch_app` starts, without building, when no `exePath` is given and no project is configured. |
| `WPF_MCP_OUTPUT_LINES` | `5000` | How many lines of app output the server keeps. |
| `WPF_MCP_TIMEOUT_MS` | `5000` | How long element lookups retry before failing. |
| `WPF_MCP_ARTIFACTS_DIR` | `./TestResults/screenshots` | Base folder for relative screenshot `savePath`s and failure screenshots. |

## Build and launch

`launch_app` can build the app first, so an agent doesn't have to run `dotnet build` and then launch the exe:

```text
launch_app { "projectPath": "src/MyApp/MyApp.csproj", "defineConstants": ["AUTOMATED_TEST"] }
  Built MyApp.csproj (Debug, symbols: AUTOMATED_TEST) in 4.2s.
  Launched MyApp.exe (pid 1234). Main window: "Login".
```

Which project is built, in order: `exePath` (nothing is built), `projectPath`, `WPF_MCP_PROJECT_PATH`, `WPF_MCP_APP_PATH` (an exe, nothing is built), then a WPF project (`UseWPF` and `WinExe`) found at or above the server's working directory. If discovery finds more than one project it lists them and stops.

| Parameter | Meaning |
|---|---|
| `projectPath` | Project to build and launch. |
| `configuration` | Defaults to `Debug`. |
| `defineConstants` | Compile symbols added to the build. They are appended to the project's own symbols (`DEBUG`, `TRACE`, and so on). |
| `noBuild` | Launch the existing build output without building. |
| `allowNonDebug` | Allow `defineConstants` with a configuration other than Debug. Refused by default. |

A running app locks its output files, so `launch_app` closes it before building. The build uses `-m:1` to avoid file-lock races between projects, and only the compiler errors are returned when it fails.

### Test-only behavior with compile symbols

Use a compile symbol for behavior that must exist only in automated test builds, such as skipping authentication:

```csharp
#if AUTOMATED_TEST
    // Skip the MSAL sign-in and use a fixed test identity.
    return TestIdentity.Default;
#else
    return await SignInWithMsalAsync();
#endif
```

Because the code is compiled out unless the symbol is defined, a Release build can never contain the bypass. The server also refuses `defineConstants` outside the Debug configuration unless `allowNonDebug` is set.

### App output

The server captures the launched app's stdout, stderr and `Debug.WriteLine`/`Trace` output. `launch_app` returns the first few startup lines, and `get_app_output` returns the rest:

```text
get_app_output {}
  [3] [stdout] Signed in as test user
  [4] [debug] LoginViewModel: loaded 12 sites
  [5] [stderr] warn: slow query (840 ms)
```

By default it returns only lines you haven't seen. Pass `since` (a sequence number, `0` for everything buffered) to re-read, `filter` (a case-insensitive regex) to narrow, and `maxLines` to limit. Output stays readable after the app exits or crashes, until the next `launch_app`.

Debug and Trace output is read from the Windows debug output channel, which only one listener can hold at a time. If another tool such as DebugView or an attached Visual Studio debugger owns it, `launch_app` says so and only stdout and stderr are captured. WPF data-binding errors appear only when the app has a trace listener configured for them.

## Selectors

Every tool that targets an element takes a `selector`:

| Form | Meaning |
|---|---|
| `LoginButton` or `#LoginButton` | AutomationId (preferred) |
| `@e12` | Ref from `get_ui_tree` / `find_elements`, valid for the session |
| `type=Button;name=OK` | Property match. Keys: `id`, `name`, `text` (name contains), `type` (ControlType), `class`, `index` (0-based) |

Lookups search all of the app's top-level windows (main window, dialogs, message boxes, popups, menus) and retry until `WPF_MCP_TIMEOUT_MS`. Pass `window` to limit the search to windows whose title contains that text.

## Tools

| Area | Tools |
|---|---|
| App lifecycle | `launch_app`, `attach_app`, `close_app`, `list_windows`, `get_app_output` |
| Inspection | `get_ui_tree`, `find_elements`, `get_element`, `get_text`, `read_grid` |
| Interaction | `click`, `invoke`, `set_text`, `type_text`, `press_keys`, `set_checked`, `select_item`, `expand_collapse`, `focus` |
| Verification | `wait_for`, `assert_element`, `screenshot` |
| Test authoring | `get_recorded_steps` |

Each action reports the element's new state and any window that opened or closed, so the agent can see a validation dialog without asking for another snapshot.

## Example agent session

```text
launch_app { "defineConstants": ["AUTOMATED_TEST"] }
  Built MyApp.csproj (Debug, symbols: AUTOMATED_TEST) in 4.2s.
  Launched MyApp.exe (pid 1234). Main window: "Login".
get_ui_tree {}
  [e1] Window #MainWindow "Login"
    [e3] Edit #UserNameTextBox
    [e4] Edit #PasswordBox
    [e5] Button #LoginButton "Log in" (disabled)
set_text   { "selector": "UserNameTextBox", "text": "alice" }
set_text   { "selector": "PasswordBox", "text": "secret" }
invoke     { "selector": "LoginButton" }
  Invoked
  New window: [e9] Window #DashboardWindow "Dashboard"
assert_element { "selector": "WelcomeText", "textContains": "alice" }
  PASS: 'WelcomeText' (Text #WelcomeText "Welcome, alice")
get_recorded_steps {}
close_app {}
```

`get_recorded_steps` returns:

```csharp
// Launch MyApp.exe
Find("UserNameTextBox").AsTextBox().Enter("alice");
Find("PasswordBox").AsTextBox().Enter("secret");
Find("LoginButton").AsButton().Invoke();
Assert.Contains("alice", Find("WelcomeText").Name);
```

With a `LoginDriver` from `scaffold-driver-object`, `Find("LoginButton").AsButton()` becomes `driver.LoginButton`.

## Notes

- `invoke` uses UIA patterns and doesn't move the mouse, so it works when the window is covered. `click` and keyboard tools use real input and bring the app's window to the front first. Don't use the machine while they run.
- `set_text` uses the Value pattern by default. Use `mode: "keyboard"` when the app reacts to key events. Use `tabOut: true` when bindings update on `LostFocus`.
- When `assert_element` returns FAIL, it attaches a screenshot of the app's topmost window (a dialog, if one is open) and saves it as `fail-<timestamp>-<selector>.png` under `WPF_MCP_ARTIFACTS_DIR`. Pass `screenshotOnFail: false` to turn this off.
- WPF virtualizes `DataGrid`/`ListView` rows. `read_grid` returns only realized rows.
- An app started with `launch_app` is killed when the server exits. An app joined with `attach_app` is left running.
