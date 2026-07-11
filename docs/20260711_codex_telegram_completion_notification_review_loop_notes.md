# Codex Telegram Completion Notification Review Loop Notes

- Target blueprint: `20260711_codex_telegram_completion_notification_blueprint.md`
- Target selected after the initial blueprint was created because the repository contained no pre-existing `docs/*blueprint*.md` file.
- Required convergence: three consecutive full-scope passes.

## Iterations

### Iteration 1 — FIX

- Result: `FIX`
- Blueprint issues found:
  - File-per-directory queue checks were not transactionally safe across concurrent hook invocations and state moves.
  - “one send attempt” contradicted the retry policy.
  - ARM64 packaging was unqualified scope without an ARM64 canary host.
  - Root-source allowlisting was deferred to implementer judgment.
  - Telegram chat discovery could select a stale or wrong update.
  - Config mutation did not require Codex to be closed, leaving an app/config write race.
  - Task naming implied an official OpenAI component, and repair could flash a console process.
  - A real app update was an availability-dependent acceptance criterion.
  - Remote-under-lock, emergency-spool, token-input, manifest limitation, and absent-notify cases were missing.
  - The Definition of Done prohibited opaque IDs even though they are required for durable deduplication.
- Blueprint corrections applied:
  - Replaced directory-state queue semantics with a transactional local SQLite event table plus atomic emergency spool.
  - Defined one logical notification with multiple network attempts.
  - Restricted v1 to tested x64 Windows and made ARM64 fail closed.
  - Fixed legacy root allowlist to `vscode` with a controlled extension procedure.
  - Added one-time Telegram challenge binding and private-chat validation.
  - Required app closure and plan/apply hash checks for config mutation.
  - Renamed install/task paths and moved scheduled repair into hidden WinExe mode.
  - Replaced mandatory real-update testing with a deterministic vendor-refresh simulation.
  - Added locked-session Remote, spool, no-echo secret, absent-notify, and integrity-limit rules.
  - Narrowed privacy criteria to allow opaque IDs only in protected event state.
- External non-blueprint issues found:
  - The OpenAI Codex manual helper could not run because Node.js is unavailable in the host shell.
  - Docs MCP registration could not be attempted successfully because the Windows Store `codex.exe` app alias returns Access Denied outside the app.
  - Current `codex-computer-use.exe` is not Authenticode-signed, so vendor-path repair cannot rely on signature validation.
- Current consecutive pass count: `0`

### Iteration 2 — FIX

- Result: `FIX`
- Blueprint issues found:
  - Retries re-resolved title and PC name, so an ambiguous Telegram acknowledgement followed by rename could produce inconsistent duplicate text.
  - Missing row/title, database contention, unsupported schema, and contradictory source were conflated into one permanent outcome.
  - Generic upstream argv and full config backups could contain secrets but were stored as plaintext artifacts.
  - User-editable mutex, retry, and source-allowlist settings could weaken fixed safety semantics.
  - Scheduled repair could write config while the Codex app was running despite install-time app-closure rules.
  - Local bridge-state corruption, schema migration, Telegram timeouts, chat-specific blocking, and successful-response content leakage were undefined.
  - Android notification verification was repeated per PC instead of being a current-phone canary requirement.
  - Release inputs/outputs and the limitation of a co-travelling SHA manifest were incomplete.
- Blueprint corrections applied:
  - Added a DPAPI-encrypted, retry-stable delivery envelope created once after root/title resolution.
  - Defined `root-ready`, `subagent`, `not-ready`, and `unsupported` resolver outcomes with 24-hour quarantine for unresolved state.
  - DPAPI-encrypted generic upstream state and full config backups; restricted their ACLs.
  - Moved safety constants into code-derived immutable values.
  - Made repair refuse writes while app/app-server processes run and added `REPAIR_PENDING`.
  - Added bridge DB quick-check, migration backup, fail-closed corruption state, HTTP timeouts, chat blocking, and no-response-body logging.
  - Moved Android notification verification to the current-PC live canary.
  - Defined versioned release outputs and an out-of-directory canary manifest copy.
- External non-blueprint issues found:
  - No new external issues beyond iteration 1.
- Current consecutive pass count: `0`

### Iteration 3 — FIX

- Result: `FIX`
- Blueprint issues found:
  - Shadow observations would remain pending and could all be sent after live enablement.
  - Shadow title correctness was not verifiable without violating the no-plaintext-output privacy rule.
  - `CODEX_HOME` was dynamically re-resolved, allowing the bridge and modified config to diverge after installation.
  - Automatic selection of an unsigned vendor executable from multiple runtime directories was unsafe.
  - Machine-ID creation, operational modes, upstream working directory/environment, ID bounds, and Telegram update acknowledgement were underspecified.
  - Stop-hook fallback had no bounded spike protocol or mandatory blueprint re-review.
  - The unavoidable process-command-line exposure inherited from Codex notify was absent from the residual threat model.
- Blueprint corrections applied:
  - Added immutable `shadow`, `live`, and `paused` modes and a non-replayable shadow state.
  - Added interactive DPAPI-envelope comparison that prints only MATCH/MISMATCH.
  - Persisted the resolved absolute Codex home and added `CODEX_HOME_CONFLICT`.
  - Removed automatic unsigned executable discovery; added `UPSTREAM_BLOCKED` and explicit reviewed adoption.
  - Defined plan-generated machine identity, inherited upstream execution context, ID validation, and Telegram offset advancement.
  - Bounded Stop fallback to a removable shadow spike requiring supported trust and a new reviewed blueprint.
  - Recorded transient command-line payload visibility as an accepted compatibility residual.
- External non-blueprint issues found:
  - The current vendor upstream binary is unsigned; this was already observed in iteration 1 and is now reflected in production branch closure.
- Current consecutive pass count: `0`

### Iteration 4 — FIX

- Result: `FIX`
- Blueprint issues found:
  - Runtime mode was read by the worker rather than captured at ingestion, so a shadow event could race with live enablement and be sent.
  - Reinstall identity, config-created-from-absent cleanup, upstream in-place modification, and simultaneous health conditions were incomplete.
  - Telegram credential storage and live enablement lacked a failure-safe transaction boundary.
  - Additional PCs skipped shadow qualification.
  - Canary counts overlapped ambiguously, subagent testing assumed a native subagent notify event, and focused/unfocused coverage was absent.
  - Rollback could race with a running app or orphan a config reference after removing binaries.
  - Release-manifest verification and Windows security-prompt handling were underspecified.
- Blueprint corrections applied:
  - Added immutable per-event `ingest_mode` to database and emergency spool.
  - Defined retained identity reuse, safe absent-config cleanup, vendor hash-change blocking, and multi-condition doctor output.
  - Split Telegram validation/test from explicit live enablement and added coordinated shared-token rotation.
  - Added a mandatory per-PC shadow gate before live delivery.
  - Made canary counts disjoint, added focus coverage, and used a known subagent fixture with optional real observation.
  - Reordered rollback around app closure and paused mode and retained disabled binaries when restoration is uncertain.
  - Added outer-manifest hash comparison and forbade automatic SmartScreen/execution-policy bypasses.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 5 — FIX

- Result: `FIX`
- Blueprint issues found:
  - Immutable `ingest_mode=paused` conflicted with the promise that paused events could later be resumed.
  - The install plan's “writes nothing” claim contradicted its plan-file output, and the multi-artifact install had no durable commit/compensation order.
  - Upgrade behavior, retained identity/state, and unfinished-install recovery were incomplete.
  - Installation-root ACL tampering could redirect messages or weaken secret/state protections.
  - The Definition of Done forbade opaque identifiers in logs while diagnostics explicitly allowed a short event-id prefix.
  - A test Telegram endpoint seam could accidentally become a production redirection feature.
- Blueprint corrections applied:
  - Split immutable `capture_mode=shadow|live` from the global `delivery_paused` network gate.
  - Added a 30-minute plan, durable mutation journal, fixed apply order, and compensating rollback behavior.
  - Defined journal-aware startup and in-place upgrade with no downgrade.
  - Applied and verified a current-user/`SYSTEM` ACL across the entire installation root and added `INSTALL_ACL_BLOCKED`.
  - Explicitly allowed only a short event-id hash prefix in logs.
  - Restricted fake Telegram endpoint injection to test-assembly internals; production endpoint is not redirectable.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 6 — FIX

- Result: `FIX`
- Blueprint issues found:
  - The minimal event/spool JSON omitted the newly required immutable `ingest_mode`.
  - PC alias and computer-name normalization was undefined, allowing control characters or extra lines to violate the fixed message contract.
  - Initial Telegram configuration rules were incorrectly reused for credential rotation on already-live PCs.
  - AUTH/CHAT failures did not explicitly release the current inflight lease.
  - Health conditions had no deterministic mapping to `OK`, `BLOCKED`, or `DEGRADED`.
  - Runtime-config updates, generic-upstream failure, and per-PC elapsed time were underspecified.
- Blueprint corrections applied:
  - Added `ingest_mode` to the minimal event and spool schema with database constraints.
  - Added fixed 64-grapheme, no-control, exactly-one-line PC-name normalization.
  - Separated initial shadow-only Telegram setup from paused live reconfiguration.
  - Required transactional inflight-to-pending recovery on AUTH/CHAT blocks.
  - Defined overall health mapping while preserving every active condition.
  - Added atomic runtime-config replacement and generic upstream blocking semantics.
  - Revised later-PC qualification time to 30-60 minutes.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 7 — FIX

- Result: `FIX`
- Blueprint issues found:
  - The document would still read `review candidate` after convergence unless edited, but any post-pass edit would invalidate the three-pass proof.
  - ACL wording could be implemented as a broad deny ACE for `Users`, which can also deny the installing user through group membership.
  - One attempt counter conflated state/title resolution retries with Telegram delivery retries, and the durable resolution backoff after 15 seconds was undefined.
  - Claiming transparent preservation of arbitrary generic notify handlers exceeded what v1 can safely verify; the prescribed launch flags also did not state Codex's null-stdio launch contract.
  - Shared-bot 429 handling implied coordination across PCs even though the design has no cross-PC coordinator.
- Blueprint corrections applied:
  - Made finalization automatic from the companion three-PASS record and declared that any later blueprint edit reopens review.
  - Replaced broad access-denial wording with a protected canonical DACL containing only current-user and `SYSTEM` allow ACEs.
  - Split resolution and delivery counters and fixed the durable not-ready retry schedule through the 24-hour boundary.
  - Restricted v1 to absent or recognized Computer Use upstream handlers, made generic handlers fail closed before mutation, and aligned the recognized launch contract with official Codex source behavior.
  - Defined independent per-PC 429 compliance and a multi-PC convergence test.
- External non-blueprint issues found:
  - Explicit UTF-8 reread confirmed that an apparent Korean setup-message corruption was only PowerShell's default display decoding; the blueprint file itself was intact.
  - Official Codex source still shows legacy notify appending the JSON payload, discarding all three standard streams, and spawning asynchronously.
  - The currently captured `codex-computer-use.exe` is a Windows console-subsystem executable, so the no-visible-window launch requirement remains an explicit integration gate.
- Current consecutive pass count: `0`

### Iteration 8 — FIX

- Result: `FIX`
- Blueprint issues found:
  - The recognized-vendor rule trusted filename/argument shape without a canonical allowed-root predicate, permitting an arbitrary same-named executable path.
  - Telegram transport did not explicitly disable redirects or certificate bypass, and its 400/401/403/other-client-error branches could misclassify or discard an event.
  - Telegram documents `error_code` as subject to change, but response handling did not distinguish stable transport/body fields from advisory error data.
  - Bot bootstrap lacked challenge entropy/expiry and closure for webhook, concurrent update consumer, duplicate challenge, and wrong-chat failure.
  - Tests omitted install-journal fault injection, ACL tamper gates, bootstrap failure cases, complete HTTP branch coverage, and shared-bot multi-PC 429 convergence.
  - The 16-24 hour estimate was not realistic for the specified Windows transactions, recovery paths, and integration matrix.
- Blueprint corrections applied:
  - Defined one canonical recognized-vendor predicate using exact argv, final-path containment, regular-file checks, and captured hash/size; reused it across install, repair, doctor, and adoption.
  - Disabled HTTP redirects and certificate bypass, preserved the inflight event on every non-success branch, split auth/chat/API blocks, and added a generic fail-closed Telegram API condition.
  - Based decisions on HTTP/`ok`/valid `retry_after`, never on human-readable descriptions, and treated unclassifiable client responses as blocked rather than lost.
  - Added a memory-only 128-bit ten-minute setup challenge and no-write failure closure for every update-consumer/chat ambiguity.
  - Expanded unit/integration coverage with installer phase fault injection, ACL fixtures, vendor path attacks, bootstrap failures, HTTP classification, and two-PC 429 convergence.
  - Revised focused engineering time to 36-56 hours and explicitly excluded redesign branches from that estimate.
- External non-blueprint issues found:
  - Official Telegram Bot API documentation states that `error_code` contents may change and that `parameters.retry_after` is the machine-actionable flood-control value.
- Current consecutive pass count: `0`

### Iteration 9 — FIX

- Result: `FIX`
- Blueprint issues found:
  - The upstream logical schema omitted the executable hash/size that the recognition rule required and used an ambiguous union-like `kind` value.
  - Global health/network blocks had no durable storage contract or atomic relationship to event transitions, and transient Telegram retry health was invisible.
  - HTTP 429 without `retry_after`, oversized/malformed responses, and the exception to non-blocking queue order were incomplete.
  - The install journal was not created until after the first filesystem mutation and did not enumerate all recoverable phases.
  - A sleeping singleton worker had no wake signal, so a new completion could wait until an older retry timer or scheduled task.
  - Doctor omitted shadow/spool counts and pause/mode/age information needed to decide whether recovery was safe.
  - Simultaneous database and emergency-spool failure, delivery-envelope DPAPI failure, invalid runtime config, and rollback backlog behavior were not closed.
  - Qualification and later-PC active time omitted the substantial manual gate work.
- Blueprint corrections applied:
  - Completed the encrypted upstream schema with exact kind values and nullable executable identity fields.
  - Added a minimal transactional health-condition table, persistent global send gates, degraded retry health, and explicit recovery-clear semantics.
  - Added 429 fallback delay, response-size failure handling, and explicit global-gate exception to queue fairness.
  - Moved the active journal to a protected sibling path and fixed every durable phase from plan validation through committed task state.
  - Added a named auto-reset wake event and bounded worker wait/idle/block behavior.
  - Expanded doctor output with all event/spool states, mode, pause, and oldest pending age.
  - Added fail-closed storage/DPAPI/config branches and explicit confirmation before delivering preserved rollback backlog.
  - Added 4-8 active qualification hours, a 7-10 day first-release expectation, and 45-90 minutes per later PC.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 10 — FIX

- Result: `FIX`
- Blueprint issues found:
  - ID hashing did not reject malformed Unicode scalar sequences, allowing replacement-fallback ambiguity.
  - A single Unicode line/paragraph separator or bidi control could survive title normalization and violate or visually spoof the exact three-line message contract.
  - Resolver discovery could bypass a busy/incompatible newer state database and use a stale older row for the same thread.
  - A Telegram 429 cooldown applied only to one event even though later events on the same PC could violate the server's retry window.
  - Config apply did not recheck app processes around the swap, and absent-notify insertion did not fix the top-level TOML location.
  - Token and chat ID lived in separate files, contradicting the claim that a Telegram credential generation was replaced atomically.
  - Setup-test acknowledgement/commit failure and silent bot-identity replacement were not closed.
  - Shadow verification could echo the user-entered expected title despite the no-plaintext command-output goal.
- Blueprint corrections applied:
  - Required well-formed Unicode scalars and strict UTF-8 for event IDs.
  - Replaced all Unicode whitespace and fixed bidi controls while preserving emoji ZWJ, and mirrored the rule for PC names.
  - Made higher-priority busy state retry and higher-priority incompatible state fail closed before any older match is accepted.
  - Added a persisted per-PC global Telegram cooldown and synchronized each 429 event's next-attempt time.
  - Added app-process checks immediately before/after config swap and root-key insertion before the first TOML table.
  - Replaced separate token/chat storage with one DPAPI-encrypted token/bot-ID/private-chat credential blob and atomic generation replacement.
  - Staged credentials until acknowledged test success, documented ambiguous test duplication, and required explicit coordinated bot migration for a changed bot ID.
  - Made shadow expected-value entry no-echo.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 11 — FIX

- Result: `FIX`
- Blueprint issues found:
  - “Already the bridge” had no exact identity/manifest/upstream predicate, so a stale or bridge-like config could be misclassified as an idempotent install.
  - Scheduled repair mutated config without the install/upgrade durable journal, phase ordering, process-race checks, and crash compensation.
  - Corrupt emergency-spool files and quarantined events were counted but could leave overall health `OK`.
  - Rollback attempted to write the pause before stopping workers/tasks, so damaged runtime config could leave delivery active during rollback.
  - The token-at-rest claim did not state that same-user malware or administrators remain inside the DPAPI trust boundary.
- Blueprint corrections applied:
  - Defined an exact self-bridge predicate requiring argv, installed path, manifest, identity, SID, completed transaction, upstream decryptability, and state-schema health; all bridge-like variants now conflict.
  - Put repair under a serialized protected journal with fixed phases, encrypted backup, pre/post process checks, and startup recovery.
  - Added persistent `EVENT_QUARANTINED` and `SPOOL_CORRUPT` degraded conditions, explicit clearing, failure branches, and tests; qualification requires both empty.
  - Reordered rollback to stop workers/tasks before attempting the persistent pause.
  - Documented the same-user/administrator residual risk of DPAPI and ACL protection.
- External non-blueprint issues found:
  - No new external issues beyond prior iterations.
- Current consecutive pass count: `0`

### Iteration 12 — PASS

- Result: `PASS`
- Full-scope review covered:
  - internal consistency across completion semantics, queue states, immutable capture mode, delivery pause, health gates, rollback, repair, and retry behavior;
  - all sealed user constraints, including Android-only receipt, all root completions, subagent exclusion, completion-only runtime text, PC/thread labels, one bot/chat, current-PC-first rollout, and upstream preservation;
  - official notify/Stop/App Server evidence and current-PC config/state observations;
  - dependency order, artifacts, branch closure, measurable gates, automated/manual tests, privacy/security boundaries, and time realism;
  - undefined terms, stale terminology, Markdown structure, UTF-8 integrity, and convergence mechanics.
- Blueprint issues found: None.
- Blueprint corrections applied: None.
- External non-blueprint issues found: None beyond the already recorded tool/documentation and unsigned-vendor limitations.
- Current consecutive pass count: `1`

### Iteration 13 — PASS

- Result: `PASS`
- Full-scope review covered:
  - reverse trace from each of the 11 sealed requirements into event capture, resolver classification, normalization, credential selection, delivery, health, canary, and rollout controls;
  - state-transition and health-code completeness, persistence, clearing rules, crash recovery, worker wakeup, per-PC cooldown, and backlog behavior;
  - exact bridge/vendor/config predicates, plan/apply/repair/uninstall ordering, process races, ACL/DPAPI boundaries, and conflict fail-closure;
  - current-PC evidence, official-source assumptions, fallback limits, artifacts, tests, qualification counts, latency, soak, later-PC reuse, and effort/calendar estimates;
  - Markdown/UTF-8 integrity, stale terms, unresolved placeholders, and finalization rules.
- Blueprint issues found: None.
- Blueprint corrections applied: None.
- External non-blueprint issues found: None beyond previously recorded limitations.
- Current consecutive pass count: `2`

### Iteration 14 — PASS

- Result: `PASS`
- Full-scope review covered:
  - adversarial end-to-end scenarios for local/Remote completion, cancel/error, subagent, delayed/Unicode title, concurrency, ambiguous ACK, 429, offline/reboot, auth/chat/API failure, bot migration, app refresh, vendor loss, generic config, DB/spool/ACL failure, repair crash, rollback, and later-PC rollout;
  - detection, durable preservation, retry/block behavior, explicit recovery, and qualification closure for every scenario;
  - live accessibility and continued alignment of every cited official OpenAI/GitHub/Telegram source;
  - sealed requirements, architecture, contracts, ordering, artifacts, security/privacy, tests, success metrics, effort, remaining questions, and finalization mechanics;
  - final Markdown/UTF-8 hygiene and unchanged blueprint content during the three-pass sequence.
- Blueprint issues found: None.
- Blueprint corrections applied: None.
- External non-blueprint issues found: None beyond previously recorded limitations.
- Current consecutive pass count: `3`
- Convergence status: `FINAL — three consecutive full-scope PASS iterations achieved`
