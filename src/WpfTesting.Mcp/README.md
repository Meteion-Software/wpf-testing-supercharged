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
- The app under test, already built.

## Build

```powershell
dotnet publish src/WpfTesting.Mcp -c Release -r win-x64 -o tools/wpf-mcp
```

## Configure an MCP client

Point the client at the published exe. For Claude Code, add a `.mcp.json` at the root of the app's repo:

```json
{
  "mcpServers": {
    "wpf-testing": {
      "command": "tools/wpf-mcp/WpfTesting.Mcp.exe",
      "env": {
        "WPF_MCP_APP_PATH": "src/MyApp/bin/Debug/net8.0-windows/MyApp.exe"
      }
    }
  }
}
```

Don't use `dotnet run` as the command. Build output written to stdout corrupts the MCP stream.

### Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `WPF_MCP_APP_PATH` | none | Exe that `launch_app` starts when no `exePath` is given. |
| `WPF_MCP_TIMEOUT_MS` | `5000` | How long element lookups retry before failing. |
| `WPF_MCP_ARTIFACTS_DIR` | `./TestResults/screenshots` | Base folder for relative screenshot `savePath`s and failure screenshots. |

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
| App lifecycle | `launch_app`, `attach_app`, `close_app`, `list_windows` |
| Inspection | `get_ui_tree`, `find_elements`, `get_element`, `get_text`, `read_grid` |
| Interaction | `click`, `invoke`, `set_text`, `type_text`, `press_keys`, `set_checked`, `select_item`, `expand_collapse`, `focus` |
| Verification | `wait_for`, `assert_element`, `screenshot` |
| Test authoring | `get_recorded_steps` |

Each action reports the element's new state and any window that opened or closed, so the agent can see a validation dialog without asking for another snapshot.

## Example agent session

```text
launch_app {}
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
