namespace OWLProtect.WindowsClientUi;

public sealed record AppLaunchOptions(bool ShowWindowOnLaunch, string? PreviewStatusPath)
{
    public bool IsPreviewMode => !string.IsNullOrWhiteSpace(PreviewStatusPath);

    public static AppLaunchOptions Parse(IEnumerable<string> args)
    {
        var showWindowOnLaunch = false;
        string? previewStatusPath = null;

        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            if (string.Equals(current, "--show-window", StringComparison.OrdinalIgnoreCase))
            {
                showWindowOnLaunch = true;
                continue;
            }

            if (current.StartsWith("--preview-status=", StringComparison.OrdinalIgnoreCase))
            {
                previewStatusPath = current["--preview-status=".Length..].Trim('"');
                continue;
            }

            if (string.Equals(current, "--preview-status", StringComparison.OrdinalIgnoreCase) && enumerator.MoveNext())
            {
                previewStatusPath = enumerator.Current.Trim('"');
            }
        }

        return new AppLaunchOptions(showWindowOnLaunch, previewStatusPath);
    }
}
