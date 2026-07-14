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
- Superseded 32-grapheme Telegram title-display refinement: PASS in automated verification
  - runtime Telegram text now keeps at most the first 32 Unicode grapheme clusters and appends one ellipsis
  - short titles remain byte-for-byte unchanged in the rendered line
  - the full normalized title remains only in the DPAPI-encrypted delivery envelope for verification and retry stability
  - family-emoji grapheme boundaries and the full-envelope/short-message split have dedicated unit and integration coverage
  - this intermediate build was deferred because Smart App Control blocked its new unsigned executable hash; the later 12-grapheme `personal.4` result supersedes it

## Smart App Control incident and rollback

- canary.11 applied transactionally, but Windows Code Integrity events 3033/3077 and Smart App Control detail events blocked the new unsigned bridge hash.
- Microsoft Defender reported no active or known threat; firewall rules and Mark-of-the-Web streams were absent.
- Both scheduled tasks and a direct canary.11 bridge launch were blocked, so the build was not considered operational even though retained state and credentials remained healthy.
- The previously exercised canary.9 hash passed a direct control/bridge launch and an isolated Task Scheduler probe under the same policy.
- A transactional rollback to canary.9 completed with matching package/binary hashes, live capture and Telegram credentials preserved.
- After rollback, Repair completed with result 0, Drain launched successfully, online doctor returned `OK`, and no new bridge Code Integrity block was recorded.
- The first post-rollback live completion was received in Telegram and the user observed no Windows blocking notification. The latest online check recorded 13 sent completions, no active doctor condition, and zero post-rollback Code Integrity blocks.
- Smart App Control was not disabled and no Defender, firewall, certificate-store, or application-control exception was added.
- Generally distributed multi-PC rollout remains blocked until the selected production certificate is acquired and the signed candidate is requalified. The later two-PC owner-controlled exception supersedes this statement only for the two personal machines.

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

## Original two-PC deployment scope and plan

- The intended deployment scope is exactly two PCs owned and controlled by the user: the current canary PC and one additional PC. No public end-user or mass binary rollout is planned.
- The smaller scope does not make a new unsigned hash automatically trusted: Smart App Control already blocked a newer unsigned title-shortening build, and every subsequent unsigned build has a distinct hash. The accepted display limit is 12 Unicode grapheme clusters plus one ellipsis when truncated.
- Title shortening remains a required final feature. Canary.9 (outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`) is only the temporary soak and rollback build; its longer title display is not the accepted final state.
- The revised private path pilots PC2 first with an unsigned supplemental App Control policy attached to the unchanged standard Smart App Control Base. The policy contains only exact Authenticode hashes for the title-shortening final Bridge/Ctl and canary.9 rollback Bridge/Ctl.
- PC2 uses a new local Codex task and a secret-free handoff bundle; it cannot open or continue this PC1 task even under the same Codex account.
- If PC2 has SAC evaluation/unknown state, an additional enforced non-system Base policy, a mismatched standard Base, or a failed policy/executable smoke check, rollout stops without bypassing Windows protection.
- At this planning checkpoint, PC1 was to remain on canary.9 until PC2 proved the exact policy and package. SignPath remained the required path for a generally distributed release; later owner-only results are recorded below.

## Superseded PC2 handoff candidate (2026-07-14, personal.1)

This candidate is retained only as an audit record. It must not be deployed because the user subsequently reduced the Telegram title-display limit from 32 to 12 grapheme clusters; `personal.2` initially superseded it, `personal.3` was deployed and rejected, and `personal.4` is the replacement candidate.

- Tooling commits: `c054e52` (fail-closed rollout), `9c07a56` (minimal secret-free payload), and `3d3b4be` (Windows PowerShell path compatibility).
- Final payload version: `1.0.0-personal.1`; source package outer manifest SHA-256 `463da2e782377787caea6f552070631b4f5cf40e8899115ba62d8082a03fd342`.
- Final Bridge SHA-256: `3333457642b4bd99760a8434209718f04111cba46bf31edab52434d593015263`.
- Final Ctl SHA-256: `82d52f2b49f3acc48737fd412b7c8ed7643cb59175b6ce017edd1d70e274ea4b`.
- Rollback source is exact canary.9: outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`, Bridge `0094ed449680a2bfc0e3839f8c3f6b578f26a12c0b315d226ce84a8b7dd1d909`, Ctl `323a3949e9f1abbe647b2bfab933e8d591a40ed6a1a347402ac73381f4c5f17d`.
- Handoff ZIP: `artifacts/handoff/CodexTelegramBridge-PC2-Handoff-1.0.0-personal.1.zip`; SHA-256 `c250447454776927ed3e69d6a60182638821141ad05fe0dc76c2bec2cdb65b5b`.
- Bundle manifest outer SHA-256: `153dec3aca482d82145a250c50a5cf97e46f5743892f6fc5afc306d52109e8b2`.
- Supplemental policy ID: `{46FF2EF5-65AD-463D-8E75-7F6677352DEF}`; standard SAC Base ID `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`; XML SHA-256 `a95e015aaf53f9df289971605b932e1ab959a165ae3099d71c37d2cbb73071ab`; CIP SHA-256 `4d34e3bab51687cef34c99d54fa54372e56bab401794c823c220510e8be7e767`.
- Policy structure validation passed: Supplemental Policy, version `1.0.0.1`, only `Enabled:Unsigned System Integrity Policy`, exactly 16 Authenticode/page-hash allow rules for four project executables, zero signer/deny/path/publisher/file-name rules, empty kernel references, and all 16 rules referenced by the user-mode scenario.
- The first generated ZIP was rejected and deleted because the old canary.9 package contained a historical blueprint with the PC1 computer name. The accepted ZIP contains only four EXEs, two minimal package manifests, seven rollout scripts, two generic documents, rollout metadata, XML/CIP policy, and its two bundle manifests. It contains no old canary document.
- Validation passed: PowerShell parse and static forbidden-mutation checks, clean NuGet vulnerability audit, formatting check, Windows Code Integrity XSD validation, mock SAC readiness/extra-Base rejection, ZIP round-trip verification, exact 18-file manifest verification, deliberate tamper rejection, Windows PowerShell 5 default-path execution, no-`-Apply` install/remove guards, personal-text scan, and Gitleaks with no detected secret.
- A fresh Release restore/build completed with zero warnings and zero errors. Fresh test execution on PC1 was not possible because SAC blocked the newly rebuilt test DLL hash (`0x800711C7`); the prior 81 unit + 85 integration result remains historical evidence, and hosted CI must re-run the exact source before PC2 install.
- No supplemental policy or final executable was applied or launched on PC1. PC1 remains on the verified canary.9 installation.

## Superseded deployed PC2 candidate (2026-07-14, personal.2)

- Source commit: `4ed0e8d` — Telegram-visible titles are now limited to the first 12 Unicode grapheme clusters plus one ellipsis when truncated; the full normalized title remains only in the encrypted local envelope.
- Final payload version: `1.0.0-personal.2`; source package outer manifest SHA-256 `5b3b74c1db3a14b57a280782650dc4a61edae5458376ed34579b113e67a820df`.
- Final Bridge SHA-256: `5e600575aefdbf5ffc55a6217f57832419d1a08ccdf09ea683890b01d1147a00`.
- Final Ctl SHA-256: `5b40a7d00dacd3c4e6aa52eeb62248df05c031add69171f09ebeb66472c99590`.
- Rollback source remains exact canary.9: outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`, Bridge `0094ed449680a2bfc0e3839f8c3f6b578f26a12c0b315d226ce84a8b7dd1d909`, Ctl `323a3949e9f1abbe647b2bfab933e8d591a40ed6a1a347402ac73381f4c5f17d`.
- Handoff ZIP: `artifacts/handoff/CodexTelegramBridge-PC2-Handoff-1.0.0-personal.2.zip`; SHA-256 `23ed13e3f5466f807f5665098260de71d467eaba324e8c83ddba474ef8671a99`.
- Bundle manifest outer SHA-256: `7922ee7a3d5ffcb1e76b3caff96e5dc03b0d5d857c8e991291f2b1fa929c447a`.
- Supplemental policy ID: `{E71B8F2E-1D29-4593-94A6-692C502E660A}`; standard SAC Base ID `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`; XML SHA-256 `eb5cfa5ff945b91341c5da7a871718efcbe366504f359ab703a0191190aa3c19`; CIP SHA-256 `07300b809d22d2889b90f2db648e6520434ff39a55be81ca941ebaf5c8f3bb8c`.
- Policy structure validation passed: Supplemental Policy, version `1.0.0.2`, only `Enabled:Unsigned System Integrity Policy`, exactly 16 Authenticode/page-hash allow rules for four project executables, zero signer/deny/path/publisher/file-name rules, empty kernel references, and all 16 rules referenced by the user-mode scenario.
- Validation passed: locked restore, zero-warning/zero-error Release build, 81 unit tests, 85 integration tests, clean NuGet vulnerability audit, formatting verification, PowerShell syntax/safety checks, deterministic two-build package manifest, Code Integrity XSD validation, ZIP round-trip verification, exact 18-entry manifest verification, deliberate tamper rejection, Windows PowerShell 5 integrity/default-path execution, no-`-Apply` install/remove guards, personal-text scan, and Gitleaks with no detected secret.
- The superseded `personal.1` handoff directory, ZIP, and ZIP-hash sidecar were deleted to prevent accidental deployment. Its source release package and hashes remain as local audit evidence.
- PC2 installed this candidate in `SAC_OFF_DIRECT_TEST`; the bundled supplemental policy was not installed. Online doctor reported `overall=OK`, live capture, valid Telegram credentials/connectivity, and zero pending/quarantined rows. Reboot, locked-session delivery, and Android Telegram delivery all passed.
- PC2 then found three promotion-blocking defects: a renamed task could revert to the initial legacy `state_5.sqlite` title after reboot, a title shorter than 12 graphemes could not pass shadow verification, and `Test-PersonalCandidateExecution.ps1` rejected the Bridge's intentional empty argument during parameter binding.
- After `personal.3` passed static qualification, the `personal.2` handoff directory, ZIP, and ZIP-hash sidecar were deleted to prevent accidental redeployment. Its source release package and recorded hashes remain as local audit evidence.
- No supplemental policy or `personal.2` executable was applied or launched on PC1. PC1 remains on the verified canary.9 installation.

## Superseded deployed PC2 candidate (2026-07-14, personal.3)

- Final source/bundle commit: `04dbf94c66dd8af39a7ae4e7edd513dbef23ca11`; public Draft PR exact public-safe commit: `29837a7`.
- This candidate attempted to treat the exact-ID `thread-descriptions-v1` value in bounded `.codex-global-state.json` metadata as the current task title. PC2 runtime evidence later proved that field is a separately generated description and can remain stale after a task rename; this authority choice was incorrect.
- Shadow verification requires the complete title when the normalized title is shorter than 12 grapheme clusters and a prefix of at least 12 grapheme clusters for longer titles. Exact short titles and copied trailing ellipsis variants are accepted; partial short titles are rejected.
- `Test-PersonalCandidateExecution.ps1` now explicitly permits an empty `Arguments` string while retaining the existing mandatory parameter and expected-exit-code checks.
- Final payload version: `1.0.0-personal.3`; source package outer manifest SHA-256 `e27c0b776443170ca920c28c1a725df41554969dc1a41df5f74653210d316d7f`.
- Final Bridge SHA-256: `e600466d049694ef12ff72e8d1f387493f818706be136732c12777a1658d60db`.
- Final Ctl SHA-256: `45edc5be5b3a6b539a077956e44382956409b1577b227922b79b354b4a9fd205`.
- Rollback source remains exact canary.9: outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`, Bridge `0094ed449680a2bfc0e3839f8c3f6b578f26a12c0b315d226ce84a8b7dd1d909`, Ctl `323a3949e9f1abbe647b2bfab933e8d591a40ed6a1a347402ac73381f4c5f17d`.
- Handoff ZIP: `artifacts/handoff/CodexTelegramBridge-PC2-Handoff-1.0.0-personal.3.zip`; SHA-256 `ec43ccee528b4548d306a76efe30136f9a1f4cb1f0192c16fee506f3914d07db`.
- Bundle manifest outer SHA-256: `3cdbffaeb5f38ce49ec4d00f5c18c27fd9a860f2cd1984bc00401e971bd401d3`.
- Supplemental policy ID: `{68B2FDF0-1E67-4FFE-8352-9212042ED373}`; standard SAC Base ID `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`; XML SHA-256 `70ed607938bd051124c1c57167e8656431d20a6bca153e771c4e82e0a90e1e32`; CIP SHA-256 `4203e6f7ef9e37740660c508922d7ce9a8629680d88a7c454a144a6d304d7986`.
- Policy structure validation passed: Supplemental Policy, version `1.0.0.3`, only `Enabled:Unsigned System Integrity Policy`, exactly 16 Authenticode/page-hash allow rules for four project executables, zero signer/deny/path/publisher/file-name rules, empty kernel references, and all 16 rules referenced by the user-mode scenario.
- Source validation passed: locked restore, zero-warning/zero-error Release build, 87 unit tests and 91 integration tests locally, the same 87/91 counts on GitHub-hosted Windows for public commit `29837a7`, formatting, transitive NuGet vulnerability audit, PowerShell syntax/safety, Gitleaks, dependency review, secret scan, and CodeQL.
- Candidate validation passed without launching project executables on PC1: two-build 15-file deterministic package comparison, Code Integrity XSD validation, exact 18-entry/20-file bundle verification, ZIP hash and byte-for-byte round trip, deliberate tamper rejection, Windows PowerShell 5 integrity/policy parsing, no-`-Apply` install/remove guards, fixed empty-argument binding through a system test executable, mock SAC-ready/SAC-off/extra-Base rejection, unsigned four-EXE/no-DLL shape, upgrade-specific credential/capture guidance, personal-text scan, and Gitleaks.
- An earlier, never-delivered `personal.3` handoff was deleted before finalization after review found that its generic instructions unnecessarily repeated Telegram bootstrap on an upgrade and that the Ctl install summary incorrectly printed `capture=shadow` for preserved upgrade state. Commit `04dbf94` fixes both; no rejected `personal.3` executable was installed or launched on either PC.
- PC2 upgraded successfully in `SAC_OFF_DIRECT_TEST`; candidate execution and online doctor passed with live capture, valid Telegram credentials/connectivity, and zero pending/quarantined rows. Reboot, locked-session delivery, and Android notification also passed. Because no supplemental policy was installed, no old project policy needs removal.
- Promotion failed on the title gate. After a task rename and again after PC reboot, Telegram used an older generated description instead of the current visible task title. The legacy `state_5.sqlite` row also retained its initial title, so neither source is an authority for the renamed title.
- Read-only inspection of the installed Codex app showed that the UI issues `thread/name/set`, keeps `title` and `description` as distinct fields, and maps `thread-descriptions-v1` to the description. The official Codex [app-server contract](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md) identifies `thread/name/set` as the user-facing rename path, and the official [session-index implementation](https://github.com/openai/codex/blob/main/codex-rs/rollout/src/session_index.rs) defines name updates as append-only with the newest valid exact-ID entry winning. `personal.3` is therefore superseded and must not be redistributed; PC2 may keep it temporarily only for completion delivery until `personal.4` is installed.
- No `personal.3` executable or supplemental policy was applied or launched on PC1. PC1 remains on the verified canary.9 installation.

## Current PC2 replacement candidate (2026-07-14, personal.4)

- Final source/bundle commit: `441c289a891edace86cbf67f626c23f9c280b5fc`; implementation fix commit: `5ee32d3`; fail-closed SAC-readiness commit: `da47b1e`; public Draft PR current deployment-record commit: `03cdccd`.
- Root/subagent classification still comes from the exact compatible state-database row. For a classified root, the resolver reads a bounded, stable `session_index.jsonl` snapshot in reverse order and uses the newest valid exact-ID `{id, thread_name, updated_at}` record. The database title is used only when no indexed name exists; desktop description metadata is never title authority.
- The reader mirrors Codex's reverse lookup for malformed unrelated rows and valid EOF JSON, accepts UTF-8 BOM and CRLF, materializes only the matching title, clears its rented buffer, retries rather than using a stale database title while the index is inaccessible/changing, and rejects an index above 64 MiB.
- `doctor` and the shadow-to-live gate now probe title-index accessibility in addition to the state-database schema.
- Local and GitHub-hosted verification passes 87 unit tests and 95 integration tests, formatting, PowerShell syntax/personal-rollout safety, the transitive NuGet vulnerability audit, package smoke, Gitleaks, dependency review, secret scan, and CodeQL. Dedicated regressions cover newest-name precedence across resolver restart, stale desktop-description rejection, exact no-name database fallback, blank latest name, malformed rows, valid unterminated EOF, UTF-8 BOM/CRLF, locked index, and oversized index. The public branch remains Draft and unmerged.
- Final payload version: `1.0.0-personal.4`; source package outer manifest SHA-256 `b5a76fd55174dc7ef2380af64019b1d67ebe81d0a9a01e889835c0e8e302c967`; two independent builds produced the same 15-file content set and outer hash.
- Final Bridge SHA-256: `e1e8f0625daf32ee222fd02592edcfae5a7c6ee40959ae0ac76ea91d9f7547ca`; final Ctl SHA-256: `b6908fc1aa415ed177be0cdb1a7a987ed8a4703583a917793e848d00d903b9d8`. The package contains exactly two unsigned EXEs and no DLL.
- Rollback source remains exact canary.9: outer manifest SHA-256 `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`, Bridge `0094ed449680a2bfc0e3839f8c3f6b578f26a12c0b315d226ce84a8b7dd1d909`, Ctl `323a3949e9f1abbe647b2bfab933e8d591a40ed6a1a347402ac73381f4c5f17d`.
- Handoff ZIP: `artifacts/handoff/CodexTelegramBridge-PC2-Handoff-1.0.0-personal.4.zip`; SHA-256 `590232f3bc17e52fe85e54b39de470cd7a19ed8f84638c84ab270edcd5597154`. Bundle manifest outer SHA-256: `4e2bc9f2eb7e891b56664fa9154feb0779a934575f076fed8fdf14889ce31d46`; final minimal-package manifest outer SHA-256: `07776e31bc0bc835f072115de0f9a729ce34aa80c8bf08fa13068db3bd3ee7a5`.
- Supplemental policy ID: `{FC005318-2251-4387-8964-0BCF777763EA}`; semantic ID/version `CodexTelegramBridge-Personal-Allow-v4` / `1.0.0.4`; standard SAC Base ID `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`; XML SHA-256 `a82f8c4b127e3501f465050fca9d8f45c04884eba625ada18efc3d4408c0273b`; CIP SHA-256 `06cd25c2ef243f1c99225b13e2ce6890c63dbf9ccfcb7bb52a304be1b63ef80c`.
- Candidate validation passed without launching project executables on PC1: exact 18-entry/20-file bundle integrity, 20-file ZIP byte/hash comparison, Code Integrity XSD and 16-rule exact-hash policy structure, byte-exact XML-to-CIP recompilation, four unsigned-EXE/no-DLL shape, PowerShell 5.1 parsing, no-`-Apply` policy guards, exact bundled empty-argument binding through a system executable, mock SAC-ready/SAC-off/extra-Base/evaluation rejection, deliberate manifest-tamper rejection, personal/secret-like-file scans, and Gitleaks.
- After this static qualification, the superseded `personal.3` handoff directory, ZIP, and ZIP-hash sidecar were deleted to prevent accidental redistribution; its source package and recorded hashes remain as audit evidence.
- PC2 upgraded to exact version `1.0.0-personal.4` from source commit `441c289a891edace86cbf67f626c23f9c280b5fc` with policy ID `{FC005318-2251-4387-8964-0BCF777763EA}`. Online doctor returned `overall=OK`, `capture_mode=live`, `delivery_paused=false`, valid Telegram credentials/connectivity, and zero pending, inflight, and quarantined rows.
- PC2 runtime title qualification passed immediately after rename, after reboot on the same task, and from Android Remote while Windows was locked; each Telegram notification showed the expected 12-grapheme prefix `Personal4 제목…`. No token, DPAPI material, chat ID, raw database, or raw log was transferred for this verification.
- PC2 is qualified on `personal.4` in `SAC_OFF_DIRECT_TEST`; PC2 did not install the supplemental policy. PC1 initially remained on canary.9 after its active Smart App Control rejected the unsigned policy, then followed the separately approved owner-only SAC-off path recorded below.

## PC1 Smart App Control authorization result (2026-07-14)

- PC1 kept Secure Boot enabled and the active Microsoft inbox `VerifiedAndReputableDesktop` Base unchanged. No Defender, firewall, Smart App Control, VBS, or other security setting was disabled or relaxed.
- The exact `personal.4` CIP SHA-256 `06cd25c2ef243f1c99225b13e2ce6890c63dbf9ccfcb7bb52a304be1b63ef80c` was submitted through inbox `CiTool.exe`; the command and operation result were both zero.
- Four consecutive inventory observations matched project policy ID `{FC005318-2251-4387-8964-0BCF777763EA}`, Base ID `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}`, friendly name `CodexTelegramBridge-Personal-Allow`, version `1.0.0.4`, unsigned/non-system identity, and the sole unsigned-policy option. Every observation reported `IsOnDisk=true`, `IsAuthorized=false`, and `IsEnforced=false`.
- The exact project policy was removed with a zero operation result. Reinspection found it in neither Active nor Staged state. Diagnostic evidence is retained locally as `artifacts/audit/pc1-personal4-policy-raw-2697e84c3b5041f7aca031440185a1f5.{json,log}`; it is not a public artifact.
- At this checkpoint no `personal.4` candidate executable had been launched and the Bridge installation had not changed; PC1 still ran the previously qualified canary.9.
- The prior readiness logic was invalid because it treated the inbox Smart App Control example XML as runtime authorization evidence. It now requires the exact policy to be already identity-valid, on disk, authorized, and enforced. The compatibility-named install script no longer calls `CiTool --update-policy` or performs rollback mutation.
- Follow-up hardening validation passed PowerShell parsing, fail-closed mocks for absent, unauthorized, unenforced, identity-mismatched, missing-property, eligible, and SAC-off states, and a fresh 18-file bundle integration run from content-equivalent pre-record checkpoint `0793a2b0093cc548cda556c05bf6da46d25bf0f2`. The smoke bundle outer-manifest SHA-256 was `45485a471c074b3d359b71742d6cfc0df926d4e0c5b4659a86521c0ec3a63d1b`; its ZIP sidecar matched SHA-256 `3dc2b17cfbe2c62237b5ad422064aa7f4cc7783316fc6541eff79c7a7ca4c55d`. The bundled compatibility installer contained zero update/remove calls. This was a static packaging test only; no project executable or policy was launched or installed.
- A separate exact-hash Base policy is rejected as an alternative: Microsoft documents that multiple Base policies combine as an intersection, so a narrow new Base could block unrelated applications rather than extend the inbox Base. At this checkpoint the least-risk path was publicly trusted Authenticode signing; the owner later explicitly chose the narrower two-personal-PC exception of turning off SAC only on PC1. Public distribution still requires trusted signing.

## PC1 owner-approved SAC-off `personal.4` deployment (2026-07-14)

- After reviewing Smart App Control's role and the loss of its reputation-based app allow gate, the user explicitly approved turning off Smart App Control only on PC1. The user made the change manually in Windows Security; no bridge script, policy script, registry command, or installer changed that setting. The post-change registry state was `VerifiedAndReputablePolicyState=0`.
- Immediately after the change, Defender antivirus, real-time protection, behavior monitoring, and IOAV/download inspection were all enabled. Domain, Private, and Public firewall profiles were enabled. VBS reported running state `2`, security service `2` remained configured/running, and UAC remained enabled with `EnableLUA=1` and consent prompt behavior `5`. Secure Boot had been verified enabled before the change and was not modified; the post-change non-elevated read was access-denied and is not recorded as a fresh Secure Boot verification.
- The exact project policy had already been removed, and the last elevated audit found zero matching project policies. No supplemental policy was installed during the SAC-off deployment.
- Bundle integrity passed for `CodexTelegramBridge-PC2-Handoff-1.0.0-personal.4`. The standard administrator candidate script could not complete because its UAC prompt was not delivered to the Codex shell session. The non-elevated direct fallback then launched the final Bridge/Ctl and rollback Bridge/Ctl and observed the four expected exit codes `2/0/2/0`. Code Integrity Operational log access was denied at that privilege level, so no fresh event-log absence claim is made; successful launch of all four exact files is the direct no-block evidence.
- The detached install helper produced `status=complete`, `exitCode=0` at `2026-07-14T10:20:38.2460907Z`. Installed Bridge SHA-256 `e1e8f0625daf32ee222fd02592edcfae5a7c6ee40959ae0ac76ea91d9f7547ca` and Ctl SHA-256 `b6908fc1aa415ed177be0cdb1a7a987ed8a4703583a917793e848d00d903b9d8` matched exact `personal.4`; the installed transaction was `active`, and both `Drain` and `Repair` scheduled tasks were enabled.
- Online doctor confirmed live capture, delivery enabled, package/ACL/runtime/task checks, preserved DPAPI credentials, vendor upstream, and Telegram connectivity. It returned `DEGRADED` only for `EVENT_QUARANTINED`: seven historical `THREAD_STATE_TIMEOUT` rows from July 11-12 and five old pending rows remained. The rows were listed and reviewed as legacy state, but were not acknowledged, deleted, replayed, or suppressed. A new send succeeded after installation.
- The user confirmed actual Telegram receipt. The message showed the expected PC field and the exact 12-grapheme prefix plus ellipsis, `모바일gpt어플에서 r…`. This verifies the PC1 title-shortening path against the real Telegram presentation, not only local formatting.
- PC1 and PC2 now both run exact `personal.4` in `SAC_OFF_DIRECT_TEST` without a supplemental policy. This closes the two-owner-PC deployment only. The public Draft PR remains unmerged, and generally distributed builds still wait for trusted Authenticode signing and full signed-release qualification.

## Canary package

- Latest built candidate: `CodexTelegramBridge-1.0.0-canary.11-win-x64`
- Candidate `manifest.sha256` outer SHA-256: `e2273d72a6aab1d0dbf6ccebf33cb2333d7d81ab4905b34efd6a039731e377d5`
- Rebuild reproducibility check: PASS — canary.10 and canary.11 produced the same outer manifest hash.
- Retained current-PC rollback build: `CodexTelegramBridge-1.0.0-canary.9-win-x64`
- Retained rollback outer SHA-256: `31e01273703a9800f08da75a7c352cf914711ea72f7b236e1a0e337065a17c15`
- canary.11 is quarantined from deployment until it has a valid Authenticode signature chaining to a CA in the Microsoft Trusted Root Program.
- Package contents are fully covered by the inner manifest; unlisted immutable artifacts fail verification.
- The sibling outer-hash record must be retained separately from the release directory.

## Implemented safety properties

- Completion payload persists only opaque IDs, timestamps, mode, and encrypted delivery envelopes.
- Existing `codex-computer-use.exe turn-ended` is captured under the fixed final-path predicate and invoked independently of Telegram capture success.
- The exact verified Computer Use `--previous-notify` wrapper is accepted as an active bridge shape; nested bridge execution suppresses a second vendor launch.
- Malformed, oversized, extra-argument, untrusted-root, and unsupported nested bridge shapes fail closed, including uninstall dangling-reference checks.
- Shadow verification accepts only an exact PC plus either the complete normalized title when it is shorter than 12 graphemes or a visible prefix of at least 12 graphemes for longer titles; copied trailing UI ellipses are handled without revealing stored titles.
- Root/subagent classification remains fail-closed against the compatible Codex state database. For a classified root, the newest valid exact-ID Codex session-index name overrides the legacy database title; the bounded stable-snapshot reader materializes only the target title, clears its rented buffer, and retries while the index is inaccessible or concurrently changing.
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
| Current-PC live package | PASS — exact `personal.4` installed, hashes matched, tasks active, online Telegram and actual completion delivery passed |
| Android Remote / locked-session evidence | PASS — canary.9 passed on PC1 and exact `personal.4` passed on PC2; PC1 exact `personal.4` Telegram display passed after install |
| 12-hour post-rollback observation | PASS — rebooted Windows task delivered at approximately +14 hours; online doctor `OK` |
| 24-hour post-rollback observation | DELIVERY PASS / FORMAL DEGRADED — update, reboot, locked Android Remote passed; four legacy shadow timeouts require reviewed acknowledgement |
| 48-hour post-rollback observation | DELIVERY PASS / FORMAL DEGRADED — reboot, locked Android Remote, and Telegram delivery passed at approximately +51 hours; seven legacy shadow timeouts await reviewed acknowledgement |
| Public signed title-shortening candidate | PENDING SignPath Foundation acceptance/configuration; still required for generally distributed production, not for the completed two-personal-PC exception |
| Personal exact-hash title-shortening candidate | BOTH QUALIFIED — PC1 and PC2 passed exact `personal.4` in SAC-off direct mode without a supplemental policy; PC1 SAC-off was the user's explicit manual security decision |
| One additional PC | PASS — PC2 exact identity, online doctor, queue state, rename, reboot, locked-session, Android Remote, and Telegram checks passed on `personal.4` |

Both personal PCs now run the qualified exact `personal.4` replacement without a supplemental policy. PC1's active-SAC diagnostic remains a valid fail-closed result, and the unsigned supplemental path must not be retried. PC1's SAC-off state is an explicit owner-only exception; Defender, all firewall profiles, VBS, and UAC remained enabled. Public distribution remains gated on trusted signing and the full signed-package qualification.
