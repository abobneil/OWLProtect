namespace OWLProtect.Core;

public static class ControlPlaneApiConventions
{
    public const string Version = "v1";
    public const string ApiPrefix = "/api/" + Version;
    public const string WebSocketPrefix = ApiPrefix + "/ws";

    public static string Path(string relativePath) =>
        $"{ApiPrefix}/{relativePath.TrimStart('/')}";

    public static string SocketPath(string relativePath) =>
        $"{WebSocketPrefix}/{relativePath.TrimStart('/')}";
}
