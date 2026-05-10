---
name: signpath-oss-github-actions
description: 'Resume the DegrandeScreenShot OSS SignPath GitHub Actions signing setup without re-deriving the verified token, org, workflow, and blocker state.'
---

# SignPath OSS GitHub Actions

Use this skill whenever work resumes on DegrandeScreenShot release signing through SignPath and GitHub Actions.

## Current verified state

- Workflow file: `.github/workflows/release.yml`.
- Correct SignPath organization ID: `f929bb63-e6fe-41f4-ab4a-ab44038c10d1`.
- Project slug: `degrande-screenshot`.
- Signing policy slug: `release-signing`.
- Local diagnostic script: `artifacts/test-signpath-token.ps1`.
- Local script and workflow preflight were updated for Windows PowerShell 5.1 compatibility using `SHA256.Create().ComputeHash(...)` and `BitConverter`.
- Known-good token fingerprint: `83a8c8b9714e932ac1cf17dcfd5254e44a54f0a29435c8091c3873354713b431`.

## Proven blocker

- GitHub Actions run `25626669147` used commit `ea341bc` and reached the SignPath action with the correct token and fingerprint.
- The `Validate SignPath token` step succeeded.
- The `Sign app via SignPath` step failed with:

```text
Trusted build system is not allowed to log into the organization 'f929bb63-e6fe-41f4-ab4a-ab44038c10d1'.
```

- This means the blocker is SignPath trusted build system configuration, not the GitHub secret, token contents, org ID, project slug, or workflow inputs.

## SignPath UI findings

- The organization-level `TrustedBuildSystems` page exists and was reachable.
- It was empty.
- Clicking `Add` opened `AddCustom` only.
- No visible way to add the predefined `GitHub.com` trusted build system was available in the UI.

## Important constraints

- Do not create a custom trusted build system for this workflow.
- Public repos using `signpath/github-action-submit-signing-request@v2` do not pass extra trusted-build-system secrets or special workflow-side connector credentials.
- Public workflow comparisons checked during this investigation included OpenRA, pandoc, VSCodium, OpenRCT2, Mudlet, and Wox-launcher; their action usage matches the current repo workflow pattern.

## Next step when revisiting

- Check whether SignPath support replied or whether the predefined `GitHub.com` trusted build system became available in the SignPath org UI.
- If available, add/link `GitHub.com` to project `degrande-screenshot`.
- Rerun the GitHub Actions release workflow on `main`.
- Inspect `Sign app via SignPath` first.
- If signing succeeds, bump and tag a new release because `v0.2.5` predates the workflow diagnostic commits.

## Reference

- See `docs/signpath-oss-github-actions.md` for the full handoff note.