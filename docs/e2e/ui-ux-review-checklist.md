# OWLProtect UI And UX Review Checklist

## Admin portal

- Login and bootstrap steps explain why password rotation and MFA are required.
- Fleet page distinguishes `Pending approval`, `Connected`, `Degraded`, `Disconnected by admin`, `Policy blocked`, and `Reauthentication required`.
- Device actions use explicit labels for approve and force disconnect.
- Diagnostics summaries can be understood without opening developer tools.
- Empty, loading, and refresh states are visible and non-destructive.
- Keyboard navigation reaches every primary action without trapping focus.
- Color contrast for status pills and data tables meets WCAG AA.
- Stream state, stale data, and reconnect conditions are visible to operators.

## Windows client

- First-run state explains what the user should do next.
- Pending approval, connected, degraded, and admin-disconnected states are visually distinct.
- Owl face base color matches connection state.
- Owl eye color matches quality state.
- Export support bundle action confirms where the artifact was written.
- Error messages tell the user whether reconnect or sign-in is required.
- Main actions remain usable with keyboard navigation only.

## Evidence capture

- Capture portal screenshots for login, bootstrap, dashboard, fleet, and disconnected-device views.
- Capture client screenshots for disconnected, pending approval, healthy, degraded, and admin-disconnected states.
- Record any `high` severity accessibility or UX defects and block sign-off until resolved.
