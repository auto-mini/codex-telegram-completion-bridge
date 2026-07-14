# Code signing policy

## Current status

The first public release is intentionally marked **unsigned preview**. It exists to make the source, automated build, package shape, installation behavior, and uninstallation behavior publicly reviewable before the project applies to SignPath Foundation. Windows may block an unsigned preview. Users must not disable Smart App Control, Microsoft Defender, or another security control to run it.

The planned production service is: **Free code signing provided by SignPath.io, certificate by SignPath Foundation.** This statement describes the selected and pending signing path; it does not claim that an unsigned artifact carries a signature.

Each GitHub release must state one of these statuses near the top of its notes:

- `UNSIGNED PREVIEW — no Authenticode signature`; or
- `SIGNED — SignPath.io / SignPath Foundation`, followed by the verified signer and certificate fingerprint.

## Project roles

- Committer and reviewer: [@auto-mini](https://github.com/auto-mini)
- Signing approver: [@auto-mini](https://github.com/auto-mini)

Repository and SignPath access for these roles must use multi-factor authentication. Changes proposed by contributors without direct commit access require review. Every production signing request requires a separate manual approval; no workflow may auto-approve a release.

## Source and build policy

- The canonical source is the public GitHub repository.
- Release candidates are built only by a checked-in GitHub Actions workflow on GitHub-hosted Windows runners.
- NuGet dependencies are restored in locked mode, tested, and checked for known vulnerabilities before packaging.
- Release assets include a SHA-256 file and GitHub build-provenance attestation.
- The SignPath request is submitted only from the workflow artifact produced by that build.
- Source, dependency, workflow, packaging, and `.signpath` changes are part of the reviewed signing boundary.

## What may be signed

Only files maintained by this project may receive the project signature:

- `bin/CodexTelegramBridge.exe`
- `bin/CodexTelegramCtl.exe`
- `scripts/install.ps1`
- `scripts/uninstall.ps1`

The project uses the Windows-provided `winsqlite3.dll`; no third-party native SQLite binary is shipped or signed. No upstream binary may be signed with the project's certificate.

SignPath artifact metadata restrictions must enforce the product name, company name, copyright, original filename, and a single Windows file-version value across both executables. The reviewed artifact configuration lives under `.signpath/`.

## Privacy and system changes

Runtime privacy behavior is documented in [PRIVACY.md](../PRIVACY.md). Installation writes under the current user's local application data, updates that user's Codex `notify` configuration transactionally, and creates per-user scheduled tasks. The read-only plan is shown before apply. [Uninstallation](../README.md#rollback-and-removal) is provided and restores the captured upstream notifier when safe.

## Activation gate

A SignPath-signed release may be published only after:

1. SignPath Foundation accepts the project and its terms;
2. the GitHub App, project, artifact configuration, signing policy, and manual approval role are configured;
3. the exact certificate subject, fingerprint, timestamp, and Windows trust chain are recorded and verified;
4. the signed output passes automated signature, package-manifest, install-plan, install, task-launch, Telegram, restart, locked-session, and uninstall checks; and
5. the release notes and download page link back to this policy.

Until all gates pass, public assets remain explicitly unsigned and are not represented as generally deployable production releases.

## Owner-controlled two-PC result

PC2 qualified the exact `personal.4` package in `SAC_OFF_DIRECT_TEST`, without installing a supplemental policy. PC1's active inbox Smart App Control Base first placed the exact unsigned supplemental on disk but reported it as unauthorized and unenforced. The diagnostic policy was removed completely, and no candidate executable was launched while Smart App Control remained active.

After a separate risk review, the owner explicitly chose to turn off only Smart App Control on that personal PC through Windows Security. The bundle did not make that change. PC1 then qualified the same exact `personal.4` hashes through direct execution, transactional installation, scheduled-task, Telegram online, actual-delivery, and 12-grapheme display checks. Defender protections, all firewall profiles, VBS, and UAC remained enabled; no supplemental policy was installed.

This owner-controlled two-machine exception does not alter the public signing policy. It is neither an accepted signing substitute nor a recommendation to disable Smart App Control for an unsigned public preview. A generally distributed release still requires a publicly trusted Authenticode signature and the full activation gate above.
