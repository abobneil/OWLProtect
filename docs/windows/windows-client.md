# Windows Client

The Windows client consists of:

- `windows/windows-client-service`: the service-owned control-plane workflow, posture collector, diagnostics sampler, and named-pipe IPC host
- `windows/windows-client-ui`: the WinUI shell that drives the service over the `owlprotect-client` named pipe

## IPC Contract

- Named-pipe requests and responses now use protocol version `1`.
- Supported commands are `status`, `connect`, `disconnect`, `sign-out`, and `support-bundle`.
- Unsupported protocol versions and unknown commands return structured pipe errors instead of ad hoc strings.

## Control-Plane Workflow

`connect` performs the full client workflow inside the Windows service:

1. Authenticate with silent SSO if configured.
2. Fall back to the interactive auth path when silent auth is unavailable or fails.
3. Enroll or re-enroll the device with the control plane.
4. Collect a local posture report and upload it.
5. Exchange the authenticated user session for a client session and policy bundle.

The current repo implementation keeps the Windows auth seam pluggable and uses configured provider tokens or username-based flows so the end-to-end workflow is deterministic in local development.

## Configuration

The Windows service reads the `WindowsClient` configuration section. The most useful keys are:

- `WindowsClient__ControlPlaneBaseUrl`
- `WindowsClient__SilentProviderId`
- `WindowsClient__SilentProviderToken`
- `WindowsClient__SilentUsername`
- `WindowsClient__InteractiveProviderId`
- `WindowsClient__InteractiveProviderToken`
- `WindowsClient__InteractiveUsername`
- `WindowsClient__SupportBundleDirectory`

## Posture Collection

The service collects and uploads the following local posture signals:

- device managed heuristic
- Windows Firewall enabled
- Microsoft Defender real-time monitoring enabled
- Secure Boot enabled
- Defender tamper protection enabled
- BitLocker protection status for the system drive when the WMI provider is available

The client computes a posture score, derives compliance reasons, and surfaces both in the WinUI shell and exported support bundle.

## Offline Recovery

- If the control plane is unreachable but the current client authorization is still inside its revalidation window, the client stays connected and enters `OfflineGrace`.
- If the revalidation window expires before the service can refresh authorization, the client transitions to `ReconnectRequired`.
- If the cached access token expires, the client transitions to `ReauthenticateRequired`.
- `sign-out` revokes the cached platform session when possible and clears the local device/session cache.

## Support Bundles

`support-bundle` exports the current client status, posture snapshot, recovery state, and recent timeline to the configured support bundle directory as JSON.

## Packaging

Use [scripts/package-windows-client.ps1](/C:/Users/nchester/Documents/GitHub/OWLProtect/scripts/package-windows-client.ps1) to produce the standalone Windows client bundle for release candidates.

The packaged bundle includes:

- published service output
- published WinUI client output
- `windows/installer/install.ps1` for installation or in-place update
- `windows/installer/uninstall.ps1` for removal
