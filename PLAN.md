# GitHub Actions: Build & Release

## Context
The project is a .NET 8 WinForms app (`win-x64`, self-contained, single-file). We need a GitHub Action that builds the app and creates a GitHub Release when a version tag is pushed.

## Workflow
Create `.github/workflows/release.yml`:

1. **Trigger**: On push of tags matching `v*` (e.g., `v1.0.0`)
2. **Build**:
   - Runs on `windows-latest`
   - Setup .NET 8 SDK
   - `dotnet publish -c Release` (project already has `PublishSingleFile`, `SelfContained`, `RuntimeIdentifier` configured in `.csproj`)
3. **Release**:
   - Create a GitHub Release from the tag using `gh release create`
   - Attach the published `MouseLock.exe` as a release asset

## Files
- **Create**: `.github/workflows/release.yml`
- **Reference**: `MouseLock.csproj` (already configured for single-file publish)

## Verification
- Push a tag like `v0.1.0` after merging to trigger the workflow
- Check the Actions tab for a successful build
- Verify the GitHub Release has `MouseLock.exe` attached
