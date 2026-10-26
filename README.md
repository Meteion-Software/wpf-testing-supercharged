# WPF Testing Supercharged

Skills and an MCP server that let Claude Code drive a WPF app through UI Automation (FlaUI) and turn the session into xUnit tests.

- **Skills** (in [skills/](skills/)): add automation IDs to XAML, add FlaUI to a test project, scaffold driver classes, write xUnit 3 tests.
- **MCP server** ([src/WpfTesting.Mcp](src/WpfTesting.Mcp)): launches the app, inspects the control tree, interacts with controls, asserts, and exports recorded steps as C#. Published on NuGet as `Meteion.WpfTesting.Mcp`.

## Install as a Claude Code plugin

The plugin bundles the skills and registers the MCP server. It needs the .NET 10 SDK on Windows.

```text
/plugin marketplace add Meteion-Software/wpf-testing-supercharged
/plugin install wpf-testing-supercharged@meteion
```

To enable it for everyone working in a repo, commit this to the repo's `.claude/settings.json`:

```json
{
  "extraKnownMarketplaces": {
    "meteion": {
      "source": { "source": "github", "repo": "Meteion-Software/wpf-testing-supercharged" }
    }
  },
  "enabledPlugins": {
    "wpf-testing-supercharged@meteion": true
  }
}
```

The plugin starts the server with `dnx Meteion.WpfTesting.Mcp`. See the [server README](src/WpfTesting.Mcp/README.md) for the global-tool alternative, environment variables and the full tool list.

## Update

1. Refresh the marketplace so Claude Code sees the new release:

   ```text
   /plugin marketplace update meteion
   ```

2. Update `wpf-testing-supercharged` from the `/plugin` menu, or uninstall and reinstall it:

   ```text
   /plugin uninstall wpf-testing-supercharged@meteion
   /plugin install wpf-testing-supercharged@meteion
   ```

3. Restart Claude Code. Skills and MCP servers load at startup.

4. Run `/mcp` and check that `wpf-testing` is connected.

The plugin pins the server version in `.mcp.json`, so updating the plugin is what moves you to a new server. `dnx` caches the pinned version, so it only downloads again when the pin changes.

If you installed the server as a global tool instead, update it separately:

```powershell
dotnet tool update -g Meteion.WpfTesting.Mcp
```

## Develop locally

```powershell
claude --plugin-dir <path to this repo>
```

## License

[MIT](LICENSE)
