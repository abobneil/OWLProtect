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
- Gateway and device trust material is issued from the control plane through privileged admin APIs:
  - `POST /api/v1/gateways/{gatewayId}/trust-material`
  - `POST /api/v1/gateways/{gatewayId}/trust-material/rotate`
  - `POST /api/v1/devices/{deviceId}/trust-material`
  - `POST /api/v1/devices/{deviceId}/trust-material/rotate`
- Issuance responses return a JSON bundle containing the public certificate metadata and the private key. Store that bundle in a mounted file and point the runtime to it with `Gateway__TrustBundleFile`.
- Gateways now authenticate heartbeat and self-rotation requests with the issued trust bundle. Once a bundle is present, the gateway rotates it automatically through `POST /api/v1/gateways/trust-material/rotate` after `rotateAfterUtc`.
- Initial provisioning is still an operator action: create or confirm the gateway record, issue trust material, write the JSON response to the configured bundle file, and then start or restart the gateway.
- Admins may revoke any issued machine credential through `POST /api/v1/trust-material/{trustMaterialId}/revoke`.
