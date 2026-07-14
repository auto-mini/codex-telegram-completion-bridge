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

The current public source includes the shorter 12-grapheme Telegram title display. PC2 confirmed the final title resolver and display with exact `personal.4`: online doctor was healthy, the renamed title remained correct immediately and after restart, and locked-session Android Remote delivery passed. PC2 was in `SAC_OFF_DIRECT_TEST`, so no supplemental policy was installed.

PC1 then exercised the exact `personal.4` policy ID under active inbox Smart App Control. Four consecutive `CiTool` observations showed the expected identity and `IsOnDisk=true`, but `IsAuthorized=false` and `IsEnforced=false`. The exact policy was removed successfully and no candidate executable was launched while SAC remained active. Readiness now fails closed unless the exact policy is already present, identity-valid, on disk, authorized, and enforced; the bundle's policy script no longer installs a policy.

After a separate risk review, the owner manually turned off only Smart App Control on PC1 through Windows Security. The bundle did not change the setting. Bundle integrity and direct execution of the final and rollback Bridge/Ctl pairs passed with all four expected exit codes. The detached transactional install completed, installed hashes matched exact `personal.4`, scheduled tasks were enabled, Telegram credentials/connectivity were healthy, and actual Telegram delivery showed the expected machine field and 12-grapheme title prefix. Defender protections and all firewall profiles remained enabled; VBS and UAC also remained active.

PC1's post-upgrade online doctor is `DEGRADED` only because seven historical `THREAD_STATE_TIMEOUT` quarantine rows and five old pending rows remain from the prior canary. Live capture, delivery, package, tasks, credentials, upstream integration, and Telegram connectivity are healthy, and a new send succeeded. Those historical rows were listed but were not automatically acknowledged, deleted, or replayed.

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

## Two-PC personal deployment status

Both owner-controlled PCs are qualified on exact `personal.4` in SAC-off direct mode, without a supplemental policy. PC1 first failed closed under active SAC, removed the unauthorized/unenforced diagnostic policy, and was upgraded only after the owner separately and explicitly chose to turn off SAC on that personal machine.

This is evidence for those two machines, not a public trust substitute and not proof that an unsigned supplemental works under active Smart App Control. A separate exact-hash Base remains unsafe because multiple enforced Base policies combine as an intersection and could block unrelated applications. Public distribution still waits for a publicly trusted signing path and full signed-package qualification.
