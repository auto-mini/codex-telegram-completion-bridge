# SignPath configuration

`artifact-configuration.xml` is the reviewed configuration proposed for the SignPath Foundation project. It signs only project-maintained executables and deployment scripts and enforces their Windows metadata.

After acceptance, import this file into the SignPath project and commit the exact organization, project, artifact-configuration, and signing-policy slugs as part of the reviewed GitHub Actions integration. Do not guess slugs or commit an API token. Signing tokens belong only in a protected GitHub environment secret.

The request parameter `version` is the four-component Windows file version printed by `scripts/package.ps1`, for example `0.1.0.0` for release `0.1.0`.
