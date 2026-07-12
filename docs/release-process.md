# Release process

## Unsigned public preview

1. Merge only from a clean default branch after CI, secret scanning, dependency review, tests, and the NuGet vulnerability audit pass.
2. Run the manual release workflow with a SemVer value.
3. The workflow builds on a GitHub-hosted Windows runner, creates the self-contained `win-x64` package, generates SHA-256 records, and creates a GitHub build-provenance attestation.
4. Release notes must begin with `UNSIGNED PREVIEW — no Authenticode signature`, link the privacy and code signing policies, and warn users not to bypass Windows security controls.
5. Verify the downloaded asset hash and its contents independently before using the release as the SignPath Foundation application sample.

## Signed release after SignPath acceptance

The signing workflow must upload the unsigned package contents as a GitHub Actions artifact, submit that artifact through the official SignPath GitHub action, wait for manual approval, download the signed result, and generate the final manifest only after signing. The final release must verify all four project-maintained signatures and must not sign an upstream binary.

The exact SignPath organization, project, artifact-configuration, and signing-policy slugs are not guessed or committed before the project is accepted. API tokens are stored only as GitHub environment secrets. The `signpath-release` GitHub environment must require manual approval.
