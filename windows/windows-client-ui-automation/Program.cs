using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

var options = AutomationOptions.Parse(args);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

Directory.CreateDirectory(options.ArtifactsRoot);
Directory.CreateDirectory(options.ScreenshotsRoot);

var report = new AutomationReport(DateTimeOffset.UtcNow, new List<ScenarioResult>());

try
{
    report.Scenarios.Add(await RunInstalledClientValidationAsync(options));

    foreach (var scenario in PreviewScenario.LoadAll(options.PreviewScenarioRoot))
    {
        report.Scenarios.Add(await RunPreviewScenarioAsync(options, scenario));
    }

    report.Success = report.Scenarios.All(item => item.Success);
}
catch (Exception exception)
{
    report.Success = false;
    report.FatalError = exception.ToString();
}

var reportPath = Path.Combine(options.ArtifactsRoot, "ui-automation-report.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, jsonOptions));
Environment.ExitCode = report.Success ? 0 : 1;

static async Task<ScenarioResult> RunInstalledClientValidationAsync(AutomationOptions options)
{
    using var process = StartUiProcess(options.UiExePath, "--show-window");
    using var automation = new UIA3Automation();
    var window = WaitForWindow(automation, "OWLProtect Client", TimeSpan.FromSeconds(30));
    var connectionLabel = ReadText(window, "ConnectionBadgeText") ?? "Unavailable";
    var supportBefore = ReadText(window, "SupportBundleText") ?? string.Empty;

    InvokeButton(window, "SupportBundleButton");
    var supportAfter = await WaitForTextChangeAsync(window, "SupportBundleText", supportBefore, TimeSpan.FromSeconds(20));

    var screenshotPath = Path.Combine(options.ScreenshotsRoot, "installed-client.png");
    CaptureWindow(window, screenshotPath);

    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();

    var assertions = new List<string>
    {
        "Main window loaded.",
        $"Observed connection label: {connectionLabel}",
        $"Support bundle feedback: {supportAfter}"
    };

    return new ScenarioResult(
        Name: "installed-client",
        Success: !string.IsNullOrWhiteSpace(supportAfter) && !supportAfter.Contains("not exported", StringComparison.OrdinalIgnoreCase),
        ScreenshotPath: screenshotPath,
        Assertions: assertions,
        Error: null);
}

static async Task<ScenarioResult> RunPreviewScenarioAsync(AutomationOptions options, PreviewScenario scenario)
{
    using var process = StartUiProcess(options.UiExePath, $"--show-window --preview-status \"{scenario.Path}\"");
    using var automation = new UIA3Automation();
    var window = WaitForWindow(automation, "OWLProtect Client", TimeSpan.FromSeconds(20));
    var connectionLabel = ReadText(window, "ConnectionBadgeText");
    var qualityLabel = ReadText(window, "QualityBadgeText");
    var supportText = ReadText(window, "SupportBundleText");

    var screenshotPath = Path.Combine(options.ScreenshotsRoot, $"{scenario.Name}.png");
    CaptureWindow(window, screenshotPath);

    process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();

    var success =
        string.Equals(connectionLabel, scenario.ExpectedConnectionLabel, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(qualityLabel) &&
        !string.IsNullOrWhiteSpace(supportText);

    return new ScenarioResult(
        Name: scenario.Name,
        Success: success,
        ScreenshotPath: screenshotPath,
        Assertions:
        [
            $"Expected connection label: {scenario.ExpectedConnectionLabel}",
            $"Observed connection label: {connectionLabel ?? "missing"}",
            $"Observed quality label: {qualityLabel ?? "missing"}"
        ],
        Error: success ? null : $"Preview scenario '{scenario.Name}' did not render the expected connection label.");
}

static Process StartUiProcess(string uiExePath, string arguments)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = uiExePath,
        Arguments = arguments,
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(uiExePath) ?? Environment.CurrentDirectory
    }) ?? throw new InvalidOperationException($"Failed to launch '{uiExePath}'.");

    return process;
}

static Window WaitForWindow(UIA3Automation automation, string title, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var window = automation.GetDesktop().FindFirstDescendant(filter => filter.ByName(title))?.AsWindow();
        if (window is not null)
        {
            return window;
        }

        Thread.Sleep(500);
    }

    throw new TimeoutException($"Timed out waiting for window '{title}'.");
}

static string? ReadText(Window root, string automationId)
{
    var element = FindByAutomationId(root, automationId);
    return element?.Name;
}

static AutomationElement? FindByAutomationId(Window root, string automationId) =>
    root.FindFirstDescendant(filter => filter.ByAutomationId(automationId));

static void InvokeButton(Window root, string automationId)
{
    var button = FindByAutomationId(root, automationId)
        ?? throw new InvalidOperationException($"Automation element '{automationId}' was not found.");

    button.AsButton().Invoke();
}

static async Task<string> WaitForTextChangeAsync(Window root, string automationId, string previousValue, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var currentValue = ReadText(root, automationId) ?? string.Empty;
        if (!string.Equals(currentValue, previousValue, StringComparison.Ordinal))
        {
            return currentValue;
        }

        await Task.Delay(500);
    }

    throw new TimeoutException($"Timed out waiting for '{automationId}' to change.");
}

static void CaptureWindow(Window window, string outputPath)
{
    var bounds = window.BoundingRectangle;
    var width = Math.Max(1, (int)Math.Ceiling((double)bounds.Width));
    var height = Math.Max(1, (int)Math.Ceiling((double)bounds.Height));
    using var bitmap = new Bitmap(width, height);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, new Size(width, height));
    bitmap.Save(outputPath, ImageFormat.Png);
}

sealed record AutomationOptions(string UiExePath, string ArtifactsRoot, string PreviewScenarioRoot)
{
    public string ScreenshotsRoot => Path.Combine(ArtifactsRoot, "client-screenshots");

    public static AutomationOptions Parse(string[] args)
    {
        string? uiExePath = null;
        string? artifactsRoot = null;
        string? previewScenarioRoot = null;

        for (var index = 0; index < args.Length; index += 1)
        {
            switch (args[index])
            {
                case "--ui-exe":
                    uiExePath = args[++index];
                    break;
                case "--artifacts-root":
                    artifactsRoot = args[++index];
                    break;
                case "--preview-scenarios":
                    previewScenarioRoot = args[++index];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(uiExePath) || string.IsNullOrWhiteSpace(artifactsRoot) || string.IsNullOrWhiteSpace(previewScenarioRoot))
        {
            throw new InvalidOperationException("Expected --ui-exe, --artifacts-root, and --preview-scenarios arguments.");
        }

        return new AutomationOptions(uiExePath, artifactsRoot, previewScenarioRoot);
    }
}

sealed record PreviewScenario(string Name, string Path, string ExpectedConnectionLabel)
{
    public static IEnumerable<PreviewScenario> LoadAll(string root)
    {
        foreach (var path in Directory.GetFiles(root, "*.json").OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            var descriptor = JsonSerializer.Deserialize<PreviewScenarioDescriptor>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException($"Scenario '{path}' was invalid.");
            yield return new PreviewScenario(descriptor.Name, path, descriptor.ExpectedConnectionLabel);
        }
    }
}

sealed record PreviewScenarioDescriptor(string Name, string ExpectedConnectionLabel);

sealed record ScenarioResult(string Name, bool Success, string ScreenshotPath, IReadOnlyList<string> Assertions, string? Error);

sealed record AutomationReport(DateTimeOffset ExecutedAtUtc, List<ScenarioResult> Scenarios)
{
    public bool Success { get; set; }

    public string? FatalError { get; set; }
}
