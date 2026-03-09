namespace OWLProtect.WindowsClientUi;

internal static class OwlIconRenderer
{
    public static (string Label, string QualityLabel, Windows.UI.Color FaceColor, Windows.UI.Color EyeColor) Describe(ClientStatus status)
    {
        var palette = ResolvePalette(status);
        return (
            Label: status.State switch
            {
                "ApprovalPending" => "Pending approval",
                "Healthy" => "Connected",
                "LocalNetworkPoor" or "LowBandwidth" or "HighJitter" or "GatewayDegraded" => "Degraded",
                "AdminDisconnected" => "Disconnected by admin",
                "AuthExpired" => "Reauthentication required",
                _ => "Disconnected"
            },
            QualityLabel: status.SignalStrengthPercent switch
            {
                >= 75 => "Quality strong",
                >= 45 => "Quality moderate",
                > 0 => "Quality poor",
                _ => "Quality unknown"
            },
            FaceColor: ToWinUiColor(palette.Face),
            EyeColor: ToWinUiColor(palette.Eyes));
    }

    private static (System.Drawing.Color Face, System.Drawing.Color Eyes) ResolvePalette(ClientStatus? status)
    {
        var face = status?.State switch
        {
            "Healthy" => System.Drawing.Color.FromArgb(255, 34, 120, 86),
            "ApprovalPending" => System.Drawing.Color.FromArgb(255, 34, 104, 170),
            "LocalNetworkPoor" or "LowBandwidth" or "HighJitter" or "GatewayDegraded" => System.Drawing.Color.FromArgb(255, 191, 120, 42),
            "AdminDisconnected" or "PolicyBlocked" or "AuthExpired" => System.Drawing.Color.FromArgb(255, 178, 61, 49),
            _ => System.Drawing.Color.FromArgb(255, 79, 95, 107)
        };

        var eyes = status?.SignalStrengthPercent switch
        {
            >= 75 => System.Drawing.Color.FromArgb(255, 88, 202, 133),
            >= 45 => System.Drawing.Color.FromArgb(255, 239, 181, 56),
            > 0 => System.Drawing.Color.FromArgb(255, 214, 88, 75),
            _ => System.Drawing.Color.FromArgb(255, 160, 165, 170)
        };

        if (string.Equals(status?.State, "AdminDisconnected", StringComparison.Ordinal))
        {
            eyes = System.Drawing.Color.FromArgb(255, 214, 88, 75);
        }

        return (face, eyes);
    }

    private static Windows.UI.Color ToWinUiColor(System.Drawing.Color color) =>
        Microsoft.UI.ColorHelper.FromArgb(color.A, color.R, color.G, color.B);
}
