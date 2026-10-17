---
name: scaffold-driver-object
description: Scaffold a FlaUI driver class (page object) for a WPF XAML view, exposing its interactive elements as properties. Use when the user asks for a driver or page object, or wants UI automation tests for a view.
---

# Scaffold Driver Object

The Page Object Pattern is a best practice where you create a class that represents a specific screen (e.g., `LoginDriver`). This class handles finding elements, keeping your tests clean.

Create a FlaUI driver class for the given XAML view. Expose the interactive elements as properties.

## Steps

1. Read the XAML view and list its interactive controls (`Button`, `TextBox`, `PasswordBox`, `CheckBox`, `ComboBox`, `ListBox`, `DataGrid`, `TabItem`, etc.).
2. Check each control for an `AutomationProperties.AutomationId`. If any are missing, tell the user and suggest running the `add-automation-ids` skill first. Don't invent IDs the XAML doesn't have.
3. Generate the class following the conventions below.

## Conventions

- **Name:** `<ViewName>Driver`, derived from the view (`LoginView.xaml` becomes `LoginDriver`). Namespace is the test project's namespace plus `.Drivers`, in a `Drivers` folder.
- **Constructor:** takes a FlaUI `Window` and stores it in a private readonly field.
- **Properties:** one read-only property per interactive control, typed with the matching FlaUI element (`Button`, `TextBox`, `CheckBox`, `ComboBox`, etc.). The property name is the AutomationId in PascalCase.
- **Lookup:** find each element by `AutomationId` on every access, so a stale element is never cached:

  ```csharp
  public Button LoginButton => _window.FindFirstDescendant(cf => cf.ByAutomationId("LoginButton")).AsButton();
  ```

- **Actions:** expose only simple properties by default. Add a convenience method (e.g. `LogIn(string user, string password)`) only if the view clearly has one workflow.
- **No assertions:** drivers find and drive elements. Assertions belong in the tests.

## Output

Create one `.cs` file with the class. Show the user the generated code and note any controls skipped because they had no AutomationId.
