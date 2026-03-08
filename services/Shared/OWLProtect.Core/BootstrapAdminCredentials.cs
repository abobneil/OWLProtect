namespace OWLProtect.Core;

public sealed record BootstrapAdminCredentials(
    string Username,
    string PasswordHash);

public interface IBootstrapAdminCredentialsProvider
{
    BootstrapAdminCredentials GetBootstrapAdminCredentials();
}
