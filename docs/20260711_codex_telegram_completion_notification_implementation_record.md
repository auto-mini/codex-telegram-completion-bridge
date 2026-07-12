# Codex Telegram Completion Bridge — implementation record

## Build identity

- Date: 2026-07-12 (Asia/Seoul)
- Branch: `codex/telegram-completion-bridge`
- Revised blueprint SHA-256: `4524af4931805cfedec06eb10d9e7f6245cf4a032b21a3875fa287792a04e748`
- Implementation commits:
  - `1b4f35f` — secure event, queue, resolver, Telegram, and DPAPI core
  - `38407e5` — durable hook/worker pipeline and upstream launch contract
  - `779751a` — transactional install and operations control
  - `cdf6190` — self-contained Windows packaging
  - `35e8e87` — recovery and reproducible-publish hardening
  - `bfcb994` — verified Computer Use wrapper compatibility and duplicate-upstream suppression
  - `03b08a1` — sidebar-truncated shadow-title verification and interleaved-task qualification
  - `606178c` — 32-grapheme Telegram title display with full encrypted local retention
  - `ec049a7` — pinned Certum Authenticode release pipeline, timestamp verification, and non-extracted native dependency
- SDK used: .NET SDK 8.0.422
- Runtime target: self-contained `win-x64`, trimming disabled

## Automated and host-level verification

- Clean locked restore: PASS
- Release build with warnings as errors: PASS, 0 warnings
- Unit tests: 85/85 PASS
- Integration tests: 82/82 PASS
- Total automated tests: 167/167 PASS
- Pinned Microsoft SignTool locked restore and Authenticode/timestamp countersigner verification: PASS
- Unsigned development-package compatibility, inner manifest, and outer-hash regression: PASS
- Required-signing preflight with an unavailable certificate fails before output mutation: PASS
- Full Certum-signed candidate: PENDING certificate acquisition and activation
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
  - latest online doctor is `OK` with 13 sent completions, zero inflight, zero quarantine, and no active health condition
  - four earlier non-current, immutable-shadow events remain pending `THREAD_NOT_PERSISTED`; they cannot be sent and remain evidence to recheck at the 12/24/48-hour gates
- Telegram title-display refinement: PASS in automated verification
  - runtime Telegram text now keeps at most the first 32 Unicode grapheme clusters and appends one ellipsis
  - short titles remain byte-for-byte unchanged in the rendered line
  - the full normalized title remains only in the DPAPI-encrypted delivery envelope for verification and retry stability
  - family-emoji grapheme boundaries and the full-envelope/short-message split have dedicated unit and integration coverage
  - deployment is deferred because Smart App Control blocks the new unsigned executable hash; the active canary.9 rollback still displays the longer title

## Smart App Control incident and rollback

- canary.11 applied transactionally, but Windows Code Integrity events 3033/3077 and Smart App Control detail events blocked the new unsigned bridge hash.
- Microsoft Defender reported no active or known threat; firewall rules and Mark-of-the-Web streams were absent.
- Both scheduled tasks and a direct canary.11 bridge launch were blocked, so the build was not considered operational even though retained state and credentials remained healthy.
- The previously exercised canary.9 hash passed a direct control/bridge launch and an isolated Task Scheduler probe under the same policy.
- A transactional rollback to canary.9 completed with matching package/binary hashes, live capture and Telegram credentials preserved.
- After rollback, Repair completed with result 0, Drain launched successfully, online doctor returned `OK`, and no new bridge Code Integrity block was recorded.
- The first post-rollback live completion was received in Telegram and the user observed no Windows blocking notification. The latest online check recorded 13 sent completions, no active doctor condition, and zero post-rollback Code Integrity blocks.
- Smart App Control was not disabled and no Defender, firewall, certificate-store, or application-control exception was added.
- Multi-PC rollout remains blocked until the selected production certificate is acquired, every shipped executable and deployment script is signed, and the signed candidate is requalified.

## Code-signing decision and readiness

- Microsoft Artifact Signing Public Trust was rejected because its current regional prerequisite excludes individuals and organizations based in South Korea.
- The earlier paid Certum path was abandoned before any order, payment, account mutation, or identity-document submission.
- The selected path is a public GitHub repository followed by an application for free OSS signing through SignPath.io with a SignPath Foundation certificate.
- SignPath requires an OSI-approved license, a public and maintained source/build relationship, documented privacy and system changes, uninstall support, explicit roles, multi-factor authentication, manual signing approval, and an already released artifact in the shape to be signed.
- The initial public artifact is therefore an explicitly unsigned preview. It must not replace the verified local canary and must not instruct users to bypass Windows protections.
- Public release preparation adds the MIT license, privacy/security/contribution policies, the required code-signing policy language, GitHub-hosted CI, secret and dependency scanning, locked restore, build provenance, and a reviewed SignPath artifact configuration.
- NuGet audit found the bundled SQLite native package affected by a high-severity advisory. The package was removed; the Windows-only application now uses the Windows-serviced and Microsoft-signed `winsqlite3.dll` instead.
- SignPath will sign only the two project executables and the two project deployment scripts. No upstream binary will be shipped under the project signature.
- Exact SignPath organization/project/policy slugs, API credentials, certificate identity, and signed-package verification are deliberately deferred until Foundation acceptance; they must not be guessed.
- The current public policy and activation gates are in `docs/code-signing-policy.md`.

## Canary package

- Latest built candidate: `CodexTelegramBridge-1.0.0-canary.11-win-x64`
- Candidate `manifest.sha256` outer SHA-256: `e2273d72a6aab1d0dbf6ccebf33cb2333d7d81ab4905b34efd6a039731e377d5`
- Rebuild reproducibility check: PASS — canary.10 and canary.11 produced the same outer manifest hash.
- Active current-PC rollback build: `CodexTelegramBridge-1.0.0-canary.9-win-x64`
- Active rollback outer SHA-256: `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`
- canary.11 is quarantined from deployment until it has a valid Authenticode signature chaining to a CA in the Microsoft Trusted Root Program.
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
| Current-PC live canary | IN PROGRESS — restored to canary.9; soak clock resets after rollback |
| Android Remote canary | IN PROGRESS — Remote and locked-session paths passed; volume gate remains |
| 48-hour soak | PENDING |
| Additional-PC release | BLOCKED by public preview, SignPath Foundation acceptance/configuration, signed-candidate qualification, and preceding gates |

The restored canary.9 installation may remain live on the current PC. No signed-candidate live apply or additional-PC rollout is authorized until its preceding gates pass.
