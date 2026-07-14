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
- Post-rollback 12-hour gate: PASS
  - at 2026-07-12 22:27 KST, approximately 14 hours after the canary.9 rollback installation, the user confirmed a normal Telegram completion after a PC reboot and a Windows-originated task
  - the immediate online doctor returned `OK`: Telegram, scheduled tasks, installation ACL, package manifest, runtime configuration, upstream preservation, and Codex notify wrapping all passed
  - queue observation was 27 sent, zero inflight, zero quarantine, zero emergency/corrupt spool, and six unresolved pending rows; the pending rows are non-blocking at this gate and must be classified again at the 24-hour check
- Post-rollback 24-hour delivery observation: PASS; formal qualification: DEGRADED
  - at 2026-07-13 08:47 KST, after a Codex app update and PC reboot, the user confirmed normal Telegram delivery from Android Remote while Windows was locked
  - app-update compatibility passed: Codex notify remained `BRIDGE_ACTIVE_WRAPPED`, the captured vendor remained `VENDOR_OK`, and Telegram, scheduled tasks, installation ACL, package manifest, runtime configuration, emergency spool, and local state checks all passed
  - the online doctor reported 30 sent, zero inflight, four quarantine, and three pending rows; the four earliest non-current shadow observations reached the designed 24-hour resolver timeout and raised non-blocking `EVENT_QUARANTINED`
  - the blueprint requires explicit reviewed acknowledgement and suppression before qualification, but the corresponding control command was missing from the implementation; therefore this gate is not recorded as fully passed even though requested delivery succeeded
- Post-rollback 48-hour delivery observation: PASS at approximately +51 hours; formal qualification: DEGRADED
  - at 2026-07-14 11:43 KST, after another PC reboot, the user confirmed normal Telegram delivery from Android Remote while Windows was locked
  - the recorded last successful send was `2026-07-14T02:43:01.8924413Z`, matching the reported test; the queue contained 36 sent, zero inflight, seven quarantine, and four pending rows
  - the first online doctor also found that the Codex app update had removed the previously captured Computer Use executable path, raising `UPSTREAM_BLOCKED`; the active verified `turn-ended --previous-notify` wrapper already referenced the current replacement executable and Telegram delivery was unaffected
  - the current active Computer Use executable was re-adopted through the existing controlled launch-contract path, updating only the DPAPI-protected upstream record; the follow-up doctor reported `VENDOR_OK` and cleared `UPSTREAM_BLOCKED`
  - after recovery, Codex notify remained `BRIDGE_ACTIVE_WRAPPED`, and Telegram, scheduled tasks, installation ACL, package manifest, runtime configuration, emergency spool, Codex state compatibility, and local state checks all passed
  - formal status remains `DEGRADED` only because seven non-current shadow observations are quarantined; the signed recovery build is still required for explicit reviewed acknowledgement and suppression
- Quarantine acknowledgement recovery: implemented and verified for the next signed candidate
  - `quarantine list` exposes only sequence, timestamp, capture mode, fixed reason code, and a 12-character opaque event prefix
  - `quarantine acknowledge <sequence>` displays the reviewed row, requires interactive confirmation, atomically converts only that exact quarantined event to `suppressed`, never sends or replays it, and clears `EVENT_QUARANTINED` only after no quarantined rows remain
  - repeated acknowledgement of an already transitioned row fails closed and leaves remaining quarantine health intact
  - 81 unit and 85 integration tests pass; source formatting and NuGet vulnerability audit are clean
  - unsigned package smoke build `0.1.1-preview.2` passed all 14 inner-manifest hashes with outer manifest SHA-256 `97ec8bbd20fb3701ab692188c7c9ea5b18a237be84dedaa69e09abb52040cbca`; it remains undeployed and is not a public release
  - public draft PR `#5` (`https://github.com/auto-mini/codex-telegram-completion-bridge/pull/5`) contains only the public code, tests, and operator documentation; GitHub-hosted CI, dependency review, Gitleaks, package smoke verification, and CodeQL all passed
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
- The first public GitHub-hosted test exposed that an elevated Windows runner can create descendants owned by `Administrators` even under a current-user-owned protected root. Installation now normalizes every non-reparse descendant to the planned user SID, revalidates the ACL before task enablement and commit, repeats the check after rollback/recovery, and traverses without following junctions.
- The follow-up hosted run proved the install normalization effective and exposed the same token-default-owner behavior for later atomic state writes. `AtomicFile` now assigns the current user SID to its fully flushed temporary file before every move or replacement, covering new and updated encrypted/configuration state without weakening descendant ACL validation.
- SignPath will sign only the two project executables and the two project deployment scripts. No upstream binary will be shipped under the project signature.
- Exact SignPath organization/project/policy slugs, API credentials, certificate identity, and signed-package verification are deliberately deferred until Foundation acceptance; they must not be guessed.
- The current public policy and activation gates are in `docs/code-signing-policy.md`.

## Public repository and unsigned preview release

- Public repository: `https://github.com/auto-mini/codex-telegram-completion-bridge`
- Default branch: protected `main`; the local operational records remain only on the private `codex/telegram-completion-bridge` branch.
- Public release: prerelease `v0.1.0`, explicitly marked as an unsigned preview and published from commit `a2cc8c87576be69c4f031c47521c03a9f538a2ea`.
- GitHub-hosted validation passed with 81 unit tests, 84 integration tests, a clean NuGet vulnerability audit, Gitleaks, packaging smoke verification, CodeQL, and build-provenance attestation.
- Release ZIP SHA-256: `cff0a45d16f4e4be54979aabf4eae6687a76f7ddb89a5c3d3b202314b3875516`.
- Outer manifest SHA-256: `9c7dc2121ece038eb5bab1e6ecfd3d9094d5c592f990a895deb0c48fd0ec52bc`.
- Independent post-download verification passed for the ZIP hash, outer and inner manifests, the declared unsigned Authenticode state, and the GitHub build-provenance attestation.
- The SignPath Foundation application was submitted on 2026-07-12 at approximately 16:57 KST. The form returned `Form submitted` and `Thank you, we'll be in touch soon.`
- The application truthfully stated that the project is new and has no public-adoption claim. It supplied the public repository, unsigned review release, 165-test CI, NuGet audit, Gitleaks, CodeQL, GitHub build-provenance, and maintainer-controlled live-canary results as trust evidence.
- The applicant directly supplied the required account identity/contact fields and accepted the required Code of Conduct and personal-data-processing terms. Optional marketing communications were left unchecked. No applicant name or email is retained in this repository or implementation record.
- Submitted classifications were `Individual maintainer(s)`, `GitHub Actions`, and `AI / LLM tools`, with `ChatGPT / OpenAI Codex` as the exact discovery source. Application status is pending SignPath Foundation review.
- Dependabot's first unrestricted grouped update proposed two .NET 8-to-10 production-package upgrades together with a test SDK update. That PR was closed without merge; replacement draft PR `#3` limits routine NuGet version updates to minor and patch releases while leaving security updates enabled.
- Draft PR `#3` passed the full 165-test CI, dependency review, Gitleaks, packaging smoke test, NuGet audit, and CodeQL. It remains deliberately unmerged pending normal review of the public-repository maintenance policy.

## Two-PC deployment scope

- The intended deployment scope is exactly two PCs owned and controlled by the user: the current canary PC and one additional PC. No public end-user or mass binary rollout is planned.
- The smaller scope reduces rollout work but does not remove the current PC's Windows trust requirement: Smart App Control already blocked the newer unsigned build that implements the 32-grapheme Telegram title display.
- Title shortening remains a required final feature. Canary.9 (outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`) is only the temporary soak and rollback build; its longer title display is not the accepted final state.
- The default path is to complete the current-PC soak, obtain a Trusted Root Program signature through SignPath or another approved provider, qualify the signed title-shortening build on the current PC, and only then install that identical signed package on the second PC.
- A temporary canary.9 installation on the second PC is not part of the default plan because it would create avoidable duplicate rollout work and preserve the display defect. Smart App Control must not be changed or bypassed on either PC.

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
| 12-hour post-rollback observation | PASS — rebooted Windows task delivered at approximately +14 hours; online doctor `OK` |
| 24-hour post-rollback observation | DELIVERY PASS / FORMAL DEGRADED — update, reboot, locked Android Remote passed; four legacy shadow timeouts require reviewed acknowledgement |
| 48-hour post-rollback observation | DELIVERY PASS / FORMAL DEGRADED — reboot, locked Android Remote, and Telegram delivery passed at approximately +51 hours; seven legacy shadow timeouts await reviewed acknowledgement |
| Signed title-shortening candidate | PENDING SignPath Foundation acceptance/configuration and signed-candidate qualification on the current PC |
| One additional PC | PENDING until the identical signed title-shortening package passes the current-PC gate |

The restored canary.9 installation may remain live on the current PC only as the stable pre-signing canary. Final completion requires the 32-grapheme title build to pass under unchanged Windows protection and then qualify on the one additional PC.
