# Secret Management and Rotation

This document defines the current secret-loading strategy and the minimum rotation procedures for bootstrap and future runtime trust components.

## Secret Sources

- Local development may inject secrets directly through environment variables.
- Shared and self-hosted environments should prefer file-backed secrets mounted into the container filesystem.
- The control plane currently supports bootstrap admin secret loading through:
  - `SecretManagement__BootstrapAdminPassword`
  - `SecretManagement__BootstrapAdminPasswordFile`
  - `SecretManagement__BootstrapAdminPasswordHash`
- Generated bootstrap passwords are allowed only when `SecretManagement__AllowGeneratedBootstrapAdminPassword=true`, which is intended for disposable development environments.

## Bootstrap Admin Rotation

1. Update the secret source with the next bootstrap admin password or password hash.
2. Authenticate with the existing bootstrap admin session and change the password through `/api/v1/admins/default/password`.
3. Restart control-plane instances if they still rely on the bootstrap secret source for seeding or recovery workflows.
4. Revoke existing admin platform sessions if compromise is suspected.
5. Verify that `/api/v1/bootstrap` still reports password rotation and MFA state correctly for the seeded admin account.

## Identity Secret Rotation

- OIDC and Entra provider metadata is public and loaded dynamically from discovery and JWKS endpoints.
- Any future confidential client secrets should follow the same environment-or-mounted-file pattern as the bootstrap admin secret.
- Rotate provider secrets by updating the mounted secret, restarting control-plane instances, and verifying `/api/v1/auth/provider/login` against each configured provider.

## Signing and Trust Material Rotation

- Session-signing keys, gateway trust material, and device trust material should be sourced from mounted files or an external secret store rather than committed configuration.
- Rotate trust material by introducing the next secret or certificate, rolling control-plane and runtime components, and then revoking the previous material once all active actors have refreshed.
- The certificate and trust-rotation workflows themselves are still tracked separately under the PKI hardening backlog.
