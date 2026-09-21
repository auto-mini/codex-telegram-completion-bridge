# Heartbeat notification filtering

The bridge previously truncated the raw assistant reply before interpreting Codex heartbeat controls. As a result, a `DONT_NOTIFY` reply could generate an unwanted completion alert and the preview could consist of `<heartbeat> <automation_id>...` instead of useful content.

Both legacy payload parsing and the stdin Stop adapter now apply the same contract before normalizing/truncating the preview:

- An ordinary answer retains the normal completion behavior.
- A complete heartbeat envelope with one automation ID, one `NOTIFY` decision, and one nonempty message sends only that message's first 50 graphemes.
- `DONT_NOTIFY` does not enqueue or send a Telegram notification.
- Unknown decisions, missing/duplicate control fields, empty messages, or malformed heartbeat XML are suppressed instead of exposing metadata or emitting completion-only noise.
- XML DTDs and external entity resolution are prohibited; input is bounded.
- No natural-language keyword heuristic hides normal answers or errors. The bridge follows the heartbeat's explicit decision.

Verification: 122 unit tests and 113 integration tests passed, including direct legacy suppression, stdin suppression, useful message extraction, entities/CDATA, grapheme limits, malformed envelopes, and ordinary text mentioning heartbeat syntax. PowerShell adapter tests launch a real child process and check its arguments; they do not contact Telegram.

The protected installed Stop adapter was updated after saving its prior script. A live `DONT_NOTIFY` probe created no queue row; a live `NOTIFY` probe reached `sent` with the human-readable prefix, no control tags, and zero retries. Package `1.0.2-personal.1` (file version `1.0.2.0`) was built and its manifest and executable launch verified. The legacy binary replacement is prepared through the one-time Windows scheduled installer and remains pending desktop shutdown; the source PC must be rechecked after that apply step.

Deployment has two parts: update the protected installed Stop adapter so active stdin notifications use the filter, and apply the new bridge package for the legacy path. The adapter's command definition remains the same, so replacing its reviewed script does not require a new command or a global trust override. The bridge executable replacement uses the existing transactional, desktop-closed installer. Do not declare both paths deployed until the installed executable hashes match the new package.

The second-PC rollout remains on hold until this correction is verified on the source PC. Earlier `.2` transfer assets alone do not include this filter.
