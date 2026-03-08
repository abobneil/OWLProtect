# OWLProtect

OWLProtect is a Windows-first enterprise VPN platform with:

- a React admin portal
- an ASP.NET Core control plane, gateway service, and scheduler
- a native Windows client UI and service
- shared contracts and design tokens

## Workspace layout

- `apps/admin-portal`: React admin portal
- `packages/contracts`: shared TypeScript contracts and seeded mock data
- `packages/theme`: shared design tokens and theme helpers
- `services/control-plane-api`: ASP.NET Core control plane API
- `services/gateway`: ASP.NET Core gateway service
- `services/scheduler`: ASP.NET Core background scheduler
- `windows/windows-client-service`: Windows service and named-pipe host
- `windows/windows-client-ui`: WinUI 3 client shell
