# Privacy policy

Codex Telegram Completion Bridge runs on the user's Windows account. It has no developer-operated server, analytics, advertising, telemetry, crash upload, or update service.

## Data sent to Telegram

After the user explicitly configures a Telegram bot and enables live delivery, the program sends only:

- a completion-only status line;
- the configured PC alias (the Windows computer name by default); and
- at most the first 32 Unicode grapheme clusters of the visible Codex task title.

Messages are sent directly to `https://api.telegram.org` using the bot and chat selected by the user. Telegram processes and stores those messages under its own [privacy policy](https://telegram.org/privacy). A task's full response, prompt body, files, and conversation transcript are not sent by this program.

During one-time setup, the program calls Telegram Bot API methods needed to validate the bot, select the user's private chat, reject webhooks, and send a test message. It does not accept group or channel chats.

## Local data

The bot token and selected chat information are encrypted with Windows DPAPI for the current user. Queue state, configuration backups, health state, and bounded operational logs are stored under `%LOCALAPPDATA%\CodexTelegramBridge`. Operational logs are designed not to contain bot tokens or task titles. The local encrypted event envelope may temporarily contain the normalized full title so the user can verify a shadow event before live delivery.

Uninstalling preserves state by default so rollback remains possible. `scripts\uninstall.ps1 -PurgeState` removes retained mutable state after explicit confirmation.

## Network boundary

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Runtime network access is limited to the user-configured Telegram Bot API flow. Existing Codex or Computer Use components may have their own network behavior, which this project does not control.

GitHub, NuGet, and SignPath may be contacted by maintainers or automated build systems while building and publishing releases; installed runtime processes do not contact those services.
