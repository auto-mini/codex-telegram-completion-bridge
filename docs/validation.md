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

An attempted new unsigned executable hash was blocked by Smart App Control and was rolled back transactionally to the previously exercised canary. Smart App Control and Microsoft Defender were not disabled and no exclusion was created. This incident is why unsigned public previews are not treated as deployable production releases.

The current public source includes the shorter 32-grapheme Telegram title display, but that refinement has only automated-test coverage until a signed candidate can be deployed.

## Public source and package checks

The public-release preparation passed:

- locked NuGet restore;
- Release build with zero warnings and zero errors;
- 81 unit tests and 82 integration tests;
- a NuGet audit with no known vulnerable packages;
- Gitleaks scans of the working tree and private development history with no detected secret;
- PowerShell, XML, GitHub Actions, formatting, and whitespace validation;
- self-contained Windows x64 publication with uniform `0.1.0.0` product/file metadata;
- confirmation that no native `e_sqlite3.dll` is bundled;
- two independent builds of the same version producing the same outer manifest hash;
- complete inner-manifest hash verification and release-ZIP round-trip verification; and
- successful creation of a read-only, applicable installation plan without applying it.

## Pending production gates

The following remain mandatory before another-PC rollout:

1. public GitHub CI and the unsigned preview release succeed from the canonical repository;
2. SignPath Foundation accepts and configures the project;
3. all intended files carry the verified SignPath Foundation Authenticode signature and timestamp;
4. current-PC install, direct launch, scheduled-task, Telegram, restart, locked-session, Android Remote, and uninstall tests pass on the exact signed package; and
5. the signed deployment completes the planned 12-, 24-, and 48-hour observation gates.
