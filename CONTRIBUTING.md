# Contributing to OWLProtect

This repository is organized as a monorepo with TypeScript workspaces, ASP.NET Core services, and Windows client projects. Keep changes scoped, link them to a GitHub issue, and validate the surfaces you touch before opening a pull request.

## Before You Start

- Check for an existing issue before starting work.
- Use the backlog taxonomy described in [.github/LABEL_TAXONOMY.md](.github/LABEL_TAXONOMY.md) when opening or triaging work.
- If the work changes architecture, contracts, security posture, or deployment behavior, document the decision in the pull request.

## Local Setup

Install the repository dependencies from the root:

```bash
npm ci
```

Use these baseline validation commands before opening a pull request:

```bash
npm run typecheck
npm run build
npm run validate:foundation
```

Build the container images affected by your change:

```bash
docker build -f apps/admin-portal/Dockerfile .
docker build -f services/control-plane-api/Dockerfile .
docker build -f services/gateway/Dockerfile .
docker build -f services/scheduler/Dockerfile .
```

Bring up the local dependency stack when you need the API and infrastructure services together:

```bash
docker compose up --build
```

## Change Scope

- Keep pull requests focused on one issue or one tightly related slice of work.
- Update shared contracts before or alongside any consumer that depends on them.
- Do not mix unrelated refactors into feature or bug-fix pull requests.
- Preserve seeded demo and bootstrap flows unless the linked issue explicitly replaces them.

## Pull Request Expectations

- Link the issue with `Closes #<number>` or `Refs #<number>`.
- Summarize the user-visible or operator-visible effect of the change.
- List the validation you ran locally.
- Call out configuration, schema, security, or rollout implications.
- Request review from the owners listed in [CODEOWNERS](.github/CODEOWNERS) for the areas you changed.

## Review and Ownership

Main workspace ownership is defined in [CODEOWNERS](.github/CODEOWNERS). If a change crosses multiple areas, expect review from each affected owner. Security-sensitive changes should explicitly note the threat model, trust boundary, or credential-handling impact in the pull request description.

## Backlog Taxonomy

Use the label families below when opening or refining work:

- `area:*` for the primary workspace or subsystem.
- `kind:*` for the type of work such as feature, docs, test, architecture, operations, or security.
- `priority:*` for delivery order.
- `wave:*` for roadmap phase alignment.
- `status:blocked` when work cannot proceed because of an external dependency.
- `status:needs-decision` when product or architecture clarification is required before implementation.

See [.github/LABEL_TAXONOMY.md](.github/LABEL_TAXONOMY.md) for the full vocabulary currently used in GitHub.
