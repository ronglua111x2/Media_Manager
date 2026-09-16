# Audit Method and Scope

## Baseline

The audit covers the complete first-party working tree at commit
`4f4e970c2c8c33e57e797203ccdcc7781aae24df` on `auto-torrent`. The tree was
clean when the baseline was captured on 2026-09-16.

Existing documentation is context, not proof. Claims are checked against the
current code. For example, historical test counts are remeasured rather than
copied from planning documents.

## Included

- WPF startup, dependency injection, application lifecycle, views, resources,
  bindings, commands, and ViewModels.
- First-party models, common helpers, services, and integration clients.
- SQLite schema, migrations, queries, transactions, backup, and restore.
- Torrent search, validation, cart, linking, reconciliation, pack, and
  Auto-Track workflows.
- State-folder settings, credentials, logs, external process launches,
  WebView2, and network trust boundaries.
- `MediaManager.Core`, tests, fixtures, project files, package references, and
  Release/x64 build behavior.

## Classified separately

- `ThirdParty/Sonarr.Parser`: integration and license boundary only. Vendored
  implementation warnings are not first-party findings unless app integration
  makes them actionable.
- Existing `docs`: checked for material drift, but not reviewed as production
  code.
- `tools`: checked only for build-graph impact.

## Excluded

- Generated `bin` and `obj` output.
- Binary images, icons, and other non-executable assets.
- Local IDE configuration and ignored developer artifacts.
- Feature requests, architecture modernization, broad refactors, dependency
  upgrades, formatting-only changes, and speculative tests.

## Severity

### Critical

A reproducible or directly provable defect that can cause at least one of:

- irreversible media or application-state loss/corruption;
- filesystem mutation outside the intended managed roots;
- credential disclosure, arbitrary code execution, or equivalent compromise;
- repeatable startup failure for a supported configuration;
- complete failure of a core unattended workflow with destructive or
  unrecoverable impact.

A Critical candidate requires a concrete path through current code, affected
state, and either a safe reproduction or proof that does not depend on
speculation.

### High

A serious correctness, reliability, privacy, or recoverability defect with
material impact, but with a workaround, limited blast radius, or no evidence
that it reaches the Critical threshold.

### Medium

A localized defect, fragile edge case, diagnosability gap, or maintainability
problem that can lead to incorrect behavior but is not currently severe.

### Low

Minor correctness, documentation, build hygiene, or maintainability drift.

## Evidence standard

Each finding records:

1. affected paths and relevant call chain;
2. observed or statically proven behavior;
3. impact and preconditions;
4. confidence and reproduction status;
5. recommended action consistent with the no-feature policy.

Potential risks that cannot be proven are retained as review notes, not
findings.

## Critical-fix gate

All severities are documented first. High, Medium, and Low items are
documentation-only in this session. If a Critical defect is found, a minimal
fix and regression-verification proposal is documented separately and no
production file is changed until the user explicitly approves that proposal.

## Verification safety

- Build and unit-test commands may write only generated build output.
- Database, link, torrent, backup, and restore behavior is reviewed statically
  or with isolated temporary fixtures.
- Live media roots, qBittorrent state, cloud backups, credentials, and the live
  state folder are never mutated by audit verification.
