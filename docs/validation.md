# Validation status

This page separates evidence from the existing local canary from qualification of a future public signed release. It contains no bot token, chat identifier, PC name, task title, prompt, response, or user-profile path.

## Existing local canary evidence

The pre-public canary has demonstrated:

- Telegram delivery from local Codex use and Android Remote;
- delivery while the Windows session was locked;
- delivery from a newly created task and a previously opened task;
- correct coexistence with the verified Computer Use wrapper;
- restart recovery, durable deduplication, and no observed duplicate notifications;
- 13 successful completion sends with zero delivery retries at the recorded checkpoint; and
- observed completion-to-Telegram latency of approximately 3–10 seconds.

An attempted new unsigned executable hash was blocked by Smart App Control and was rolled back transactionally to the previously exercised canary. Smart App Control and Microsoft Defender were not disabled and no exclusion was created. This incident is why unsigned public previews are not treated as generally deployable production releases.

The current public source includes the shorter 12-grapheme Telegram title display. PC2 confirmed that display, restart recovery, locked-session delivery, and Android Remote delivery with `personal.2`, but also exposed three pre-promotion defects: a renamed title could fall back to the stale legacy database value after restart, short titles could not pass shadow verification, and the candidate smoke script rejected an empty Bridge argument during parameter binding. `personal.3` fixed the short-title and smoke defects and again passed upgrade, doctor, reboot, locked-session, and Android delivery checks, but its rename test failed because it treated a separately generated desktop description as the task title. The current source instead reads Codex's newest valid exact-thread `session_index.jsonl` name, retains the legacy database only as the no-name fallback, and remains pending exact `personal.4` PC2 requalification.

## Public source and package checks

The public-release preparation passed:

- locked NuGet restore;
- Release build with zero warnings and zero errors;
- 81 unit tests and 85 integration tests;
- a NuGet audit with no known vulnerable packages;
- Gitleaks scans of the working tree and private development history with no detected secret;
- PowerShell, XML, GitHub Actions, formatting, and whitespace validation;
- self-contained Windows x64 publication with uniform `0.1.0.0` product/file metadata;
- confirmation that no native `e_sqlite3.dll` is bundled;
- installation-tree owner normalization and ACL revalidation for elevated Windows installer processes;
- two independent builds of the same version producing the same outer manifest hash;
- complete inner-manifest hash verification and release-ZIP round-trip verification; and
- successful creation of a read-only, applicable installation plan without applying it.

## Public signed-release gates

The following remain mandatory before a generally distributed production release:

1. public GitHub CI and the unsigned preview release succeed from the canonical repository;
2. SignPath Foundation accepts and configures the project;
3. all intended files carry the verified SignPath Foundation Authenticode signature and timestamp;
4. current-PC install, direct launch, scheduled-task, Telegram, restart, locked-session, Android Remote, and uninstall tests pass on the exact signed package; and
5. the signed deployment completes the planned 12-, 24-, and 48-hour observation gates.

## Two-PC personal pilot gate

The second owner-controlled PC uses a separate, fail-closed qualification path documented in `personal-two-pc-rollout.md`. It keeps the standard Smart App Control Base policy active and, only when that exact Base is enforced and permits supplementals, adds an unsigned supplemental policy containing 16 Authenticode hash rules for the final and rollback Bridge/Ctl executables. It adds no path, publisher, wildcard, signer, deny, or allow-all rule.

This personal path is not a public trust substitute. The exact candidate must still pass bundle verification, policy activation, four-process execution smoke, install/shadow/Telegram/live checks, restart, lock-screen, and Android Remote on PC2. PC1 remains on canary.9 until PC2 proves the policy approach.
