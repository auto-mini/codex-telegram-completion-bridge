# Windows legacy notify overflow

On 2026-09-22, a previously working long-running task completed normally but its new events never reached the bridge queue. Codex's local logs recorded `codex_core::hook_runtime`, `hook_name=legacy_notify`, and Windows `os error 206` for the exact completed turns. Even an 11-character final answer failed. State resolution for the task was `RootReady`; credentials, capture mode, and the installed bridge were valid.

The failure occurs while Codex launches the legacy command with its JSON payload as an argument, before the bridge can parse or truncate the answer. Changing the preview length cannot repair that launch failure.

## Standard-input adapter

`scripts/Invoke-CodexStopHook.ps1` consumes the documented `Stop` lifecycle event through UTF-8 stdin and invokes the existing bridge with only the validated thread/turn IDs and the bounded answer preview. It requires PowerShell 7.2 or later. It normalizes whitespace, keeps 50 complete graphemes plus an optional ellipsis, and bounds both input and forwarded payload. The prompt/transcript are neither forwarded nor stored. Native argument lists preserve quotes/backslashes without shell interpolation.

The adapter returns `{}` and exit code 0; notification failures cannot block or continue a Codex turn. Only `Stop` events are accepted. The bridge still performs state resolution, subagent suppression, current-user DPAPI protection, queueing, and deduplication. A legacy event and a Stop event with the same thread/turn IDs map to the same event ID.

Reference: [official Codex hook input and Stop contracts](https://learn.chatgpt.com/docs/hooks). The current installed CLI reports `0.155.0-alpha.9.2`, with hooks enabled. Its generated app-server schema supports `hooks/list` and version-checked `config/value/write`. The official [configuration schema](https://learn.chatgpt.com/docs/config-schema.json) defines per-hook `hooks.state` entries with `trusted_hash` and `enabled`.

The local repair copied the reviewed adapter into the existing installation's protected `config` directory, saved the previous Codex config in an encrypted backup, registered one user-level Stop command, and trusted only that exact command definition hash. No global trust-bypass flag, model change, Windows security change, or credential migration was used. The existing notify configuration remains intact for compatibility.

## Verification checkpoint

- Eight focused integration cases passed: stdin far beyond the Windows argument limit, Korean/family emoji boundaries, quotes/backslashes/newlines, non-Stop events, invalid IDs, duplicate fields, and malformed input.
- PowerShell syntax checks passed.
- `hooks/list` reports the configured Stop hook enabled and trusted with no errors/warnings.
- One real missed completion was recovered through the installed adapter and normal bridge queue. Its row reached `sent` and the bridge recorded `TELEGRAM_SENT`.
- An automatic test on the already-loaded task still used its cached legacy hook and produced error 206, with no new bridge event. The user then fully restarted the desktop app; the new app-server process started after the config change.
- After restart, the same previously affected task completed a new test response. Without manual injection, the Stop adapter queued that exact turn and Telegram delivery reached `sent` at `2026-09-21T17:55:10Z`, with zero retries. The decrypted delivery envelope matched the expected first 50 graphemes plus ellipsis and had four lines. This verifies automatic capture and Telegram API acceptance; phone rendering was not inspected.
- The old legacy hook can still log error 206 on the same turn. It remains in place for upstream compatibility; successful delivery now uses the independent stdin path. Duplicate events continue to share the same queue event ID.

When removing this adapter, remove only its exact Stop handler and matching `hooks.state` entry, preserving any later-added hooks and config edits. Do not blindly restore the whole historical config backup. Keep the original bridge until automatic delivery is verified; quarantined historical rows are unrelated to this launch failure and must not be replayed or deleted as part of this repair.
