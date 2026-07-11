# Codex Telegram Completion Bridge — implementation record

## Build identity

- Date: 2026-07-11 (Asia/Seoul)
- Branch: `codex/telegram-completion-bridge`
- Frozen blueprint SHA-256: `96bb6cd8c934c494ffd55642fb26cd3b8f73193a3775c1995778a98b8fa52b8a`
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
- Unit tests: 65/65 PASS
- Integration tests: 72/72 PASS
- Total automated tests: 137/137 PASS
- Real current-user Task Scheduler create/disable/enable/query/remove test: PASS
- Actual console-subsystem upstream contract test: PASS
  - exact argv and original synthetic payload
  - inherited working directory/environment
  - NUL standard handles
  - no console window
- Source formatting verification: PASS
- Read-only current-PC install plan: PASS
  - current handler classified as recognized vendor
  - plan applicable
  - `config.toml` SHA-256 unchanged
  - no installation root, task, credential, or bridge state created

## Canary package

- Release: `CodexTelegramBridge-1.0.0-canary.4-win-x64`
- `manifest.sha256` outer SHA-256: `8c07fb071db6b885704a5f25546de2df14e50caf3e6f248a2ff0e35767fb6f99`
- Rebuild reproducibility check: PASS — canary.3 and canary.4 produced the same outer manifest hash.
- Package contents are fully covered by the inner manifest; unlisted immutable artifacts fail verification.
- The sibling outer-hash record must be retained separately from the release directory.

## Implemented safety properties

- Completion payload persists only opaque IDs, timestamps, mode, and encrypted delivery envelopes.
- Existing `codex-computer-use.exe turn-ended` is captured under the fixed final-path predicate and invoked independently of Telegram capture success.
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
| Current-PC shadow apply | PENDING — requires desktop/app-server closure |
| Three local root shadow observations | PENDING |
| Subagent and cancellation shadow checks | PENDING |
| Existing Computer Use behavior before/after | PENDING |
| Telegram bootstrap and setup test | PENDING |
| Current-PC live canary | PENDING |
| Android Remote canary | PENDING |
| 48-hour soak | PENDING |
| Additional-PC release | BLOCKED by preceding gates |

No live Telegram delivery or additional-PC rollout is authorized by this record until its preceding gates pass.
