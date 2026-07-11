# Codex Telegram Completion Bridge — implementation record

## Build identity

- Date: 2026-07-12 (Asia/Seoul)
- Branch: `codex/telegram-completion-bridge`
- Revised blueprint SHA-256: `059a01a1ba1194f691231c90fbc310460e337b4eb9f1ec04fe8e412e0c52ab68`
- Implementation commits:
  - `1b4f35f` — secure event, queue, resolver, Telegram, and DPAPI core
  - `38407e5` — durable hook/worker pipeline and upstream launch contract
  - `779751a` — transactional install and operations control
  - `cdf6190` — self-contained Windows packaging
  - `35e8e87` — recovery and reproducible-publish hardening
- SDK used: .NET SDK 8.0.422
- Runtime target: self-contained `win-x64`, trimming disabled

## Automated and host-level verification

- Clean locked restore: PASS
- Release build with warnings as errors: PASS, 0 warnings
- Unit tests: 73/73 PASS
- Integration tests: 80/80 PASS
- Total automated tests: 153/153 PASS
- Real current-user Task Scheduler create/disable/enable/query/remove test: PASS
- Actual console-subsystem upstream contract test: PASS
  - exact argv and original synthetic payload
  - inherited working directory/environment
  - NUL standard handles
  - no console window
- Source formatting verification: PASS
- Read-only current-PC install plan: PASS
  - initial handler classified as recognized vendor
  - plan applicable
  - `config.toml` SHA-256 unchanged
  - no installation root, task, credential, or bridge state created
- First current-PC shadow apply: PASS
  - transactional apply completed with exit code 0
  - installation ACL, manifest, state database, and both scheduled tasks verified
- Post-restart Computer Use composition observation: PASS
  - exact four-element `turn-ended --previous-notify` wrapper observed
  - nested bridge captured one shadow completion
  - revised doctor reports `BRIDGE_ACTIVE_WRAPPED`
  - canary.7 upgrade plan classifies the live wrapper as `healthy_bridge`

## Canary package

- Release: `CodexTelegramBridge-1.0.0-canary.7-win-x64`
- `manifest.sha256` outer SHA-256: `7f70acc433f1786385c09487829f35b3a10d340e40311d43be82d7ad9fe90f5c`
- Rebuild reproducibility check: PASS — canary.6 and canary.7 produced the same outer manifest hash.
- Package contents are fully covered by the inner manifest; unlisted immutable artifacts fail verification.
- The sibling outer-hash record must be retained separately from the release directory.

## Implemented safety properties

- Completion payload persists only opaque IDs, timestamps, mode, and encrypted delivery envelopes.
- Existing `codex-computer-use.exe turn-ended` is captured under the fixed final-path predicate and invoked independently of Telegram capture success.
- The exact verified Computer Use `--previous-notify` wrapper is accepted as an active bridge shape; nested bridge execution suppresses a second vendor launch.
- Malformed, oversized, extra-argument, untrusted-root, and unsupported nested bridge shapes fail closed, including uninstall dangling-reference checks.
- Hook mode performs no network I/O.
- SQLite deduplication, emergency spool, leases, independent resolution/delivery retries, persistent health gates, retention, and corruption markers are implemented.
- Bot token and selected bot/chat generation are committed as one DPAPI CurrentUser blob.
- Telegram requests use a fixed HTTPS origin, redirects disabled, bounded bodies, no parse mode, and retry/block classification.
- Install, upgrade, repair, and uninstall use protected journals, encrypted config backups, hash guards, process-race checks, compensation, and owner/DACL verification.
- Scheduled tasks are current-user, interactive-token, least-privilege, hidden, battery-capable, start-when-available, and ignore duplicate starts.
- Operational output and logs exclude token, Telegram body, prompt, response, Codex payload `cwd`, PC label, thread title, and full opaque IDs.

## Rollout gates

| Gate | Status |
|---|---|
| Implementation and automated verification | PASS |
| Package and read-only current-PC plan | PASS |
| Current-PC shadow apply | PASS |
| Post-restart Computer Use wrapper compatibility | PASS — corrected canary upgrade queued |
| Three local root shadow observations | IN PROGRESS — 1/3 observed |
| Subagent and cancellation shadow checks | PENDING |
| Existing Computer Use behavior before/after | PENDING |
| Telegram bootstrap and setup test | PENDING |
| Current-PC live canary | PENDING |
| Android Remote canary | PENDING |
| 48-hour soak | PENDING |
| Additional-PC release | BLOCKED by preceding gates |

No live Telegram delivery or additional-PC rollout is authorized by this record until its preceding gates pass.
