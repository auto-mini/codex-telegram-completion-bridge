# Authenticode signing runbook

Decision date: 2026-07-12

## Selected path

Use a one-year **Certum Standard Code Signing in the Cloud** certificate issued to an individual and held in the SimplySign cloud service.

This is the selected path because:

- Microsoft Artifact Signing Public Trust is not currently available to an individual or organization based in South Korea.
- Smart App Control requires an RSA signature from a publicly trusted provider; a private or self-signed root is not sufficient.
- Certum's current product supports an individual subscriber, RSA 3072-bit keys, Microsoft trust, an HSM-backed virtual card, and up to 5,000 signatures per month.
- The displayed one-year price was EUR 209 including VAT on the decision date. Confirm the final KRW amount, tax, and exchange rate before checkout.
- EV does not add a required property for this user-mode sidecar. Microsoft no longer distinguishes EV code-signing certificates in the Trusted Root Program, and this project does not sign kernel drivers.

Do not use Microsoft Artifact Signing Private Trust, a self-signed certificate, a locally installed private root, reputation-only execution, or a Smart App Control/Defender exception.

## User-visible consequences and purchase boundary

No order has been placed and no identity document has been submitted.

Before purchase, the user must explicitly accept both of these consequences:

1. The verified legal first and last name becomes the public Authenticode publisher identity embedded in every signed release.
2. The certificate is a paid product and requires identity verification. Certum documents an identity document plus a utility bill for an individual; automatic verification may be used when offered, with manual verification as the fallback.

These are the only steps that must not be performed by an unattended implementation process.

## Prepared repository controls

- `scripts/bootstrap-signing-tools.ps1` restores the pinned Microsoft Windows SDK Build Tools package in locked mode. It does not install the full SDK or require administrator rights.
- `scripts/package.ps1 -RequireCodeSigning` fails before publishing if no certificate thumbprint is supplied.
- The signing preflight requires a current RSA certificate with a private key, Code Signing EKU, Digital Signature key usage, a key size of at least 3072 bits, and an online revocation-checked chain.
- The chain must contain the pinned `Certum Code Signing 2021 CA` SHA-256 fingerprint and terminate at one of the pinned RSA roots published in Certum's official repository.
- Native SQLite is emitted beside the single-file applications instead of being extracted unsigned into a temporary directory. The two executables and `e_sqlite3.dll` are signed by Microsoft SignTool with SHA-256 and an RFC 3161 timestamp. The install and uninstall PowerShell scripts are also Authenticode-signed and timestamped.
- Existing signatures are rejected instead of appended. Every signature and timestamp is verified before `signing-record.json` and `manifest.sha256` are created.
- Install planning, apply, staging, and doctor revalidate any package containing `signing-record.json` through the Windows Authenticode trust provider. Runtime doctor uses cached revocation material so its offline mode does not introduce network access.
- Unsigned development packages remain supported for tests, but they are not eligible for the signed canary or another-PC gates.

## Certificate activation

After the user authorizes the purchase:

1. Buy the one-year Standard Code Signing in the Cloud product as an individual.
2. Complete Certum identity and address verification.
3. Activate SimplySign on the Android phone and install SimplySign Desktop on this build PC.
4. Confirm that the certificate appears in `Cert:\CurrentUser\My` and exposes its private key.
5. Do not export, copy, log, or back up the private key. The key must remain in the Certum cloud HSM.

List only eligible certificates without exposing a private key:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotBefore, NotAfter, HasPrivateKey
```

## Signed candidate build

```powershell
& .\scripts\bootstrap-signing-tools.ps1
& .\scripts\package.ps1 `
    -Version "1.0.0-rc.1" `
    -CodeSigningThumbprint "<40-hex-certificate-thumbprint>" `
    -RequireCodeSigning
```

The default timestamp endpoint is Certum's documented `http://time.certum.pl/`. HTTP is acceptable for RFC 3161 because the signed timestamp token supplies integrity; do not substitute an unreviewed endpoint.

The signed output is intentionally not byte-for-byte reproducible because each timestamp is unique. `signing-record.json` retains the deterministic pre-sign SHA-256 and final signed SHA-256 for every signed file, and the final package manifest covers the signed bytes.

## Qualification gates

Run these gates in order. Any failure keeps the current canary.9 installation active.

1. All automated tests pass.
2. All five release signatures are valid, use the selected leaf certificate, have timestamps, and pass `SignTool verify /pa /all /tw` where applicable.
3. A read-only install plan validates the signed package online.
4. Apply the signed candidate transactionally on the current PC while Codex/ChatGPT is closed.
5. Direct launch and both scheduled tasks run without a Smart App Control notification and without new Code Integrity block events.
6. `doctor --online` returns `OK`, Telegram sends one completion, and a long title confirms the 32-grapheme display limit.
7. Restart, locked-session, Android Remote, new-task, and old-task checks pass.
8. Restart the 12-, 24-, and 48-hour soak clock from the signed deployment time.
9. Only after the 48-hour gate, install the exact same signed package and verify its outer manifest hash on the second PC.

## Primary references

- Microsoft Smart App Control signing requirements: https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control
- Microsoft Artifact Signing regional prerequisites: https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- Microsoft SignTool: https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
- Certum product and technical requirements: https://shop.certum.eu/standard-code-signing-in-the-cloud.html?___store=certum_en
- Certum individual verification documents: https://support.certum.eu/en/code-signing-required-documents/
- Certum official root and subordinate fingerprints: https://www.certum.eu/en/cert_expertise_root_certificates/
