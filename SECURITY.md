# Security policy

## Supported versions

Only the newest GitHub release and the current default branch receive security fixes. Preview or explicitly unsigned releases are development artifacts and may be blocked by Windows security controls.

## Report a vulnerability

Use GitHub's private **Report a vulnerability** form in the repository Security tab. Do not open a public issue for a suspected vulnerability, bot token, chat identifier, private task title, log archive, or configuration backup.

If private vulnerability reporting is temporarily unavailable, open a public issue containing no sensitive details and ask the maintainer to establish a private channel.

## Secrets

Revoke a Telegram bot token immediately through BotFather if it may have been exposed. The project will never ask for a bot token in an issue, pull request, command-line argument, environment variable, or release diagnostic.

## Release trust

The signature state of every downloadable artifact is stated in its release notes. Do not disable Smart App Control, Microsoft Defender, or another security control to run an artifact. See the [code signing policy](docs/code-signing-policy.md).
