# SignPath OSS GitHub Actions Handoff

This note captures the verified state of the DegrandeScreenShot SignPath OSS signing setup so future work can resume without restarting the investigation.

## Goal

Use SignPath OSS signing in `.github/workflows/release.yml` to sign the Windows release artifacts produced by GitHub Actions.

## Verified identifiers

- SignPath organization ID: `f929bb63-e6fe-41f4-ab4a-ab44038c10d1`
- Project slug: `degrande-screenshot`
- Signing policy slug: `release-signing`
- Signing policy ID from API: `a0760305-ce6c-4b4e-802d-29c9ba40da74`

## Verified workflow and script state

- Workflow file: `.github/workflows/release.yml`
- Local diagnostic script: `artifacts/test-signpath-token.ps1`
- Both were updated for Windows PowerShell 5.1 compatibility:
  - use `SHA256.Create().ComputeHash(...)`
  - use `BitConverter` instead of `Convert.ToHexString`

## Known-good token verification

The local diagnostic script succeeded against the correct organization ID and produced this fingerprint:

```text
83a8c8b9714e932ac1cf17dcfd5254e44a54f0a29435c8091c3873354713b431
```

The GitHub Actions `Validate SignPath token` step later printed the exact same SHA-256 fingerprint, proving the GitHub secret matched the working local token.

## Decisive GitHub Actions run

- Workflow run: `25626669147`
- Commit: `ea341bc`

### What succeeded

- build and upload of the unsigned app artifact
- `Validate SignPath token`

### What failed

```text
Trusted build system is not allowed to log into the organization 'f929bb63-e6fe-41f4-ab4a-ab44038c10d1'.
```

## Conclusion

The following were already proven correct:

- token contents
- GitHub secret contents
- organization ID
- project slug
- signing policy slug
- basic workflow action inputs

The remaining blocker is SignPath trusted build system configuration.

## SignPath UI findings

- The organization-level `TrustedBuildSystems` page exists.
- It was empty.
- The `Add` button opened `AddCustom`.
- No visible way to add the predefined `GitHub.com` trusted build system was present.

This strongly suggests the SignPath org does not yet expose or link the predefined hosted `GitHub.com` trusted build system required by `signpath/github-action-submit-signing-request@v2`.

## What other public repos are doing

Public repos using the same SignPath GitHub Action were checked, including:

- OpenRA
- pandoc
- VSCodium
- OpenRCT2
- Mudlet
- Wox-launcher

They use the normal pattern:

```yaml
uses: signpath/github-action-submit-signing-request@v2
with:
  api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
  organization-id: ...
  project-slug: ...
  signing-policy-slug: ...
  github-artifact-id: ${{ steps.upload-unsigned.outputs.artifact-id }}
```

Some also pass:

- `artifact-configuration-slug`
- `parameters`
- a longer wait timeout

No public examples were found that pass extra trusted-build-system secrets or workflow-side connector credentials for the hosted GitHub connector.

## Important constraint

Do not create a custom trusted build system for this workflow. The hosted SignPath GitHub Action expects the predefined hosted GitHub connector path, not a custom CI connector created through `AddCustom`.

## Support status

An email was sent to SignPath support requesting that they enable/add/link the predefined `GitHub.com` trusted build system for this organization and project.

## Next step when revisiting

1. Check for a reply from SignPath support or a change in the Trusted Build Systems UI.
2. If `GitHub.com` becomes available, add/link it to project `degrande-screenshot`.
3. Rerun the release workflow on `main`.
4. Inspect `Sign app via SignPath` first.
5. If signing succeeds, bump and tag a new release because `v0.2.5` predates the final diagnostic workflow commits.