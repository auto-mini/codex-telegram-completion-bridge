# Codex Telegram Completion Bridge — implementation record

## Build identity

- Date: 2026-07-12 (Asia/Seoul)
- Branch: `codex/telegram-completion-bridge`
- Revised blueprint SHA-256: `1cf0239d2eab0f7aa1405a1046b7c80af0b47e84e2e5b4e92c648e0aa8d70539`
- Implementation commits:
  - `1b4f35f` — secure event, queue, resolver, Telegram, and DPAPI core
  - `38407e5` — durable hook/worker pipeline and upstream launch contract
  - `779751a` — transactional install and operations control
  - `cdf6190` — self-contained Windows packaging
  - `35e8e87` — recovery and reproducible-publish hardening
  - `bfcb994` — verified Computer Use wrapper compatibility and duplicate-upstream suppression
  - `03b08a1` — sidebar-truncated shadow-title verification and interleaved-task qualification
- SDK used: .NET SDK 8.0.422
- Runtime target: self-contained `win-x64`, trimming disabled

## Automated and host-level verification

- Clean locked restore: PASS
- Release build with warnings as errors: PASS, 0 warnings
- Unit tests: 79/79 PASS
- Integration tests: 81/81 PASS
- Total automated tests: 160/160 PASS
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
- Shadow identity qualification: PASS
  - three distinct current-task records matched exact PC and a 12+ grapheme visible title prefix
  - an interleaved completion from another Codex task correctly produced `MISMATCH` and blocked Telegram setup
  - trailing sidebar ellipsis and multiline/long-title normalization are covered by tests
  - four non-current events with no persisted Codex thread remain pending with immutable shadow ingest mode; they are network-ineligible and retained for classification evidence before additional-PC release
- Telegram bootstrap and setup test: PASS
  - bot identity, no-webhook state, private chat challenge, setup test, and atomic DPAPI credential commit completed
  - online `getMe`/`getChat` doctor checks report `OK`
- First current-PC live completion: PASS
  - user observed exactly one Telegram completion notification
  - bridge state contains one current-task live event in `sent`, with zero delivery retries
  - post-send online doctor is `OK`, last-success time is present, and operational logs contain neither PC name nor task title
- First Android Remote live completion: PASS
  - user confirmed the request originated from Android Remote and observed its Telegram completion notification
  - bridge state contains a second distinct current-task live event in `sent`, again with zero delivery retries
  - aggregate live result is two requested completions, two sends, zero missing, and zero observed duplicates
- Expanded live canary observations: PASS so far
  - user confirmed delivery while Windows was locked, from an additional task, and from a previously opened task
  - observed end-to-end latency is approximately 3–10 seconds, within the provisional p95 10-second / max 30-second target
  - latest online doctor is `OK` with six sent completions, zero inflight, zero quarantine, and no active health condition
  - four earlier non-current, immutable-shadow events remain pending `THREAD_NOT_PERSISTED`; they cannot be sent and remain evidence to recheck at the 12/24/48-hour gates

## Canary package

- Release: `CodexTelegramBridge-1.0.0-canary.9-win-x64`
- `manifest.sha256` outer SHA-256: `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`
- Rebuild reproducibility check: PASS — canary.8 and canary.9 produced the same outer manifest hash.
- Package contents are fully covered by the inner manifest; unlisted immutable artifacts fail verification.
- The sibling outer-hash record must be retained separately from the release directory.

## Implemented safety properties

- Completion payload persists only opaque IDs, timestamps, mode, and encrypted delivery envelopes.
- Existing `codex-computer-use.exe turn-ended` is captured under the fixed final-path predicate and invoked independently of Telegram capture success.
- The exact verified Computer Use `--previous-notify` wrapper is accepted as an active bridge shape; nested bridge execution suppresses a second vendor launch.
- Malformed, oversized, extra-argument, untrusted-root, and unsupported nested bridge shapes fail closed, including uninstall dangling-reference checks.
- Shadow verification accepts only an exact PC and a no-echo visible title prefix of at least 12 graphemes; copied UI ellipses are stripped without revealing stored titles.
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
| Post-restart Computer Use wrapper compatibility | PASS |
| Three local root shadow observations | PASS — sequences 14, 15, and 16 matched |
| Subagent and cancellation shadow checks | PENDING |
| Existing Computer Use behavior before/after | PENDING |
| Telegram bootstrap and setup test | PASS |
| Current-PC live canary | IN PROGRESS — six total live sends; locked/new/old task paths observed |
| Android Remote canary | IN PROGRESS — Remote and locked-session paths passed; volume gate remains |
| 48-hour soak | PENDING |
| Additional-PC release | BLOCKED by preceding gates |

No live Telegram delivery or additional-PC rollout is authorized by this record until its preceding gates pass.
