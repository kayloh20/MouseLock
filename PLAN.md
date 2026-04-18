# Fix: "Application Control policy has blocked this file"

## Context
After downloading `MouseLock.exe` from the GitHub Release, Windows blocks it with "An Application Control policy has blocked this file." This is Windows 11 Smart App Control (or WDAC) rejecting the binary because it is **unsigned** and has the Mark-of-the-Web (MOTW) attribute from being downloaded.

The project ships a self-contained single-file exe with `PublishReadyToRun` and `EnableCompressionInSingleFile` — both produce native code / compressed layouts that get flagged more aggressively by heuristic scanners and Application Control.

The only full fix is code signing. Without a certificate, we can reduce false-positive triggers and document the user-side unblock workaround.

## Approach

### 1. Reduce heuristic triggers in `MouseLock.csproj`
Remove two properties that make the binary look more suspicious without meaningfully helping end users:
- `<PublishReadyToRun>true</PublishReadyToRun>` — pre-JITs to native code; small startup win, big AV/WDAC flag surface.
- `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` — compressed single-file bundles are a known false-positive magnet.

Keep `PublishSingleFile` and `SelfContained` — those are fine and desirable.

### 2. User-side workaround (document in README)
Right-click `MouseLock.exe` → **Properties** → tick **Unblock** → **OK**. This strips the MOTW zone identifier and satisfies most Application Control checks. This is the real, immediate fix for any user hitting the block.

Alternative for PowerShell users: `Unblock-File MouseLock.exe`.

### 3. Long-term: code signing (out of scope now)
The proper fix is signing the binary with a code-signing certificate (Sectigo/DigiCert ~$100–500/year, or EV cert for instant reputation). Would be added to the release workflow via `signtool`. Noting here but not implementing — requires a cert the user doesn't currently have.

## Files to change
- `MouseLock.csproj` — remove `PublishReadyToRun` and `EnableCompressionInSingleFile`.
- (Optional) Add a short note to a `README.md` with the unblock instructions. The repo has no README today — skip unless requested.

## Verification
1. Bump the tag (e.g., push `v0.1.1`) to trigger the release workflow.
2. Download the new `MouseLock.exe` from the release.
3. Try to run it — if still blocked, right-click → Properties → Unblock → OK, then re-run.
4. Confirm the app launches normally.
