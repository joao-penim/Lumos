# Releasing Lumos

Releases are built entirely by GitHub Actions (`.github/workflows/release.yml`). Nothing is built locally.

## Steps

1. **Update the changelog.** In `CHANGELOG.md`, add a `## [x.y.z]` section that lists what changed. The workflow copies this section into the release notes.
2. **Bump the default version** in `Directory.Build.props` (`<Version>`). This only affects local builds: release builds take the version from the tag.
3. **Open a pull request** with these changes. `main` is protected: changes land only through a PR, the `build` check (build, tests and AOT publish) must pass, and PRs are squash-merged. Merge it once CI is green.
4. **Tag the merged commit on `main` and push the tag:**

   ```powershell
   git switch main
   git pull --ff-only
   git tag v0.1.0
   git push origin v0.1.0
   ```

   Tags aren't covered by the branch protection, so pushing one doesn't need a PR.

5. **Wait for the Release workflow.** It runs the tests, then builds the UI and the Native AOT agent with the tag's version. It also:
   - creates `Lumos-x.y.z-win-x64.zip` and `SHA256SUMS.txt`;
   - creates a build-provenance attestation, for public repositories only;
   - creates a **draft** GitHub Release with the notes and a "Verify your download" section.
6. **Review the draft** on the Releases page: check the notes and download the zip to smoke-test it. Then click **Publish**.

## Notes

- **Pre-releases**: tags with a suffix (`v0.2.0-beta.1`) are marked as pre-releases automatically.
- **Tag format**: the workflow fails if the tag isn't `vMAJOR.MINOR.PATCH[-suffix]`.
- **Fixing a bad release**: delete the draft (and the tag, with `git push --delete origin vX.Y.Z`), fix the problem, then tag again. Never re-use a tag for a release that was already published: bump the patch version instead.
- **Attestations** need the repository to be public. On a private repo that step is skipped and the release ships with checksums only.
