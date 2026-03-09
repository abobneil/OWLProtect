using System.Drawing;
using System.Runtime.InteropServices;

namespace OWLProtect.WindowsClientTray;

internal static class TrayIconRenderer
{
    public static Icon Create(ClientStatus status, int size = 32)
    {
        var palette = ResolvePalette(status);
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var scale = size / 32f;
        using var faceBrush = new SolidBrush(palette.Face);
        using var eyeBrush = new SolidBrush(palette.Eyes);
        using var beakBrush = new SolidBrush(Color.FromArgb(255, 215, 167, 49));

        graphics.FillPolygon(faceBrush, Scale(scale, (7f, 8f), (13f, 2f), (16f, 13f)));
        graphics.FillPolygon(faceBrush, Scale(scale, (25f, 8f), (19f, 2f), (16f, 13f)));
        graphics.FillEllipse(faceBrush, 5f * scale, 8f * scale, 22f * scale, 19f * scale);
        graphics.FillEllipse(eyeBrush, 10f * scale, 12f * scale, 4f * scale, 7f * scale);
        graphics.FillEllipse(eyeBrush, 18f * scale, 12f * scale, 4f * scale, 7f * scale);
        graphics.FillPolygon(beakBrush, Scale(scale, (16f, 18f), (13.5f, 24f), (18.5f, 24f)));

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static (Color Face, Color Eyes) ResolvePalette(ClientStatus status)
    {
        var face = status.State switch
        {
            "Healthy" => Color.FromArgb(255, 34, 120, 86),
            "ApprovalPending" => Color.FromArgb(255, 34, 104, 170),
            "LocalNetworkPoor" or "LowBandwidth" or "HighJitter" or "GatewayDegraded" => Color.FromArgb(255, 191, 120, 42),
            "AdminDisconnected" or "PolicyBlocked" or "AuthExpired" => Color.FromArgb(255, 178, 61, 49),
            _ => Color.FromArgb(255, 79, 95, 107)
        };

        var eyes = status.SignalStrengthPercent switch
        {
            >= 75 => Color.FromArgb(255, 88, 202, 133),
            >= 45 => Color.FromArgb(255, 239, 181, 56),
            > 0 => Color.FromArgb(255, 214, 88, 75),
            _ => Color.FromArgb(255, 160, 165, 170)
        };

        if (string.Equals(status.State, "AdminDisconnected", StringComparison.Ordinal))
        {
            eyes = Color.FromArgb(255, 214, 88, 75);
        }

        return (face, eyes);
    }

    private static PointF[] Scale(float scale, params (float X, float Y)[] points) =>
        points.Select(point => new PointF(point.X * scale, point.Y * scale)).ToArray();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
