# Answer preview upgrade — 2026-09-22

The owner explicitly requested that Telegram completion notifications include the beginning of the Codex answer. This supersedes the title-only message restriction in the historical July blueprint for this upgrade. Other filtering, privacy, delivery, and installation requirements continue to apply.

## Behavior

- Preserve the completion status, PC label, and 12-grapheme task title.
- Add `답변: ` followed by the first 50 normalized Unicode grapheme clusters from the completed turn's `last-assistant-message`. Append one ellipsis if truncated; include shorter answers in full.
- Collapse whitespace/control separators and preserve Korean combining characters and emoji sequences.
- Missing, blank, wrong-type, duplicate, or invalid-Unicode answer fields keep the original three-line notification. Never substitute the user's prompt or an unrelated turn.
- Encrypt only the bounded preview with current-user DPAPI before queue/spool persistence. Do not retain the answer's remainder or log preview content.
- Preserve shadow-only capture, root/subagent filtering, duplicate suppression, frozen retry content, and the existing upstream notifier.

## Storage and compatibility

`MinimalEvent` gains optional encrypted `AnswerPreviewDpapi`; `DeliveryEnvelope` gains optional `AnswerPreview`. Existing records without either field remain valid.

The optional `event_answer_previews` table holds encrypted prefixes by event ID. Event insertion and preview insertion share one transaction; duplicate events do not overwrite the original prefix. The existing v1 `events` schema and `user_version` remain unchanged for rollback compatibility. New connections enable foreign keys so event retention also removes previews. Old builds can still read the original queue; pending four-line delivery envelopes require the new worker.

## Verification performed

- Locked .NET 8.0.422 restore and Release build succeeded with zero warnings/errors.
- 108 unit tests and 101 integration tests passed, including Korean/emoji boundaries, malformed/missing payload fields, shadow/subagent behavior, legacy database upgrade, transactional deduplication, retention, emergency-spool recovery, and stable retry text after worker restart.
- NuGet vulnerability audit: clean.
- Self-contained Windows x64 package `1.0.1-personal.2` built successfully; package manifest verified and both executables passed launch smoke checks. File version: `1.0.1.0`.
- A synthetic answer prefix passed through the new parser, real Windows DPAPI round trip, renderer, and production Telegram client. Telegram acknowledged the four-line message successfully. This is API acceptance evidence, not confirmation that a phone displayed it.
- Existing installation online diagnostics confirmed valid credentials and Telegram connectivity. Its pre-existing `THREAD_STATE_TIMEOUT` quarantines were preserved.

## Installation boundary

The normal transactional installer requires desktop shutdown and preserves Telegram credentials, capture mode, pause state, PC alias, and queue data. The protected local helper waited for shutdown as a one-time, current-user interactive-token Windows scheduled task, independent of the Codex process tree, and removed its own task registration after completion.

The first helper, launched directly from the agent's shell, was no longer running after the user restarted the apps and had no apply/completion record. Both installed executable hashes still matched the old version, so that first attempt is recorded as **not installed**.

The scheduled-task recovery completed installation at `2026-09-21T17:14:32Z` and post-install verification at `2026-09-21T17:14:46Z`. A subsequent read-only check confirmed both installed executable hashes match the tested `1.0.1-personal.2` package and report file version `1.0.1.0`; the installed manifest passes all 14 entries, credentials and runtime settings are preserved, live capture is enabled and unpaused, the wrapped notify hook and scheduled tasks are active, and Telegram connectivity is OK. The one-time upgrade task no longer exists. Overall diagnostics still report the same pre-existing 380 quarantined events; they were not deleted or replayed. No automatic 50-character completion after installation had yet been observed at this checkpoint; the earlier synthetic Telegram send and executable-hash verification are separate evidence.

The initial upgrade did not include a public release or another PC. The owner's subsequent request explicitly authorized transfer to a specified connected PC through GitHub. That follow-up uses a draft release for the exact tested package and the separate [remote handoff instructions](remote-answer-preview-handoff.md); it does not publish a general release or copy local credentials/state.
