Place build-time reference assemblies here for both local builds and GitHub-hosted CI:

- `refs/sts2/sts2.dll`
- `refs/sts2/GodotSharp.dll`
- `refs/sts2/0Harmony.dll`

These files are resolved by `scripts/build-mod.ps1`, `scripts/install-mod.ps1`, and the GitHub Actions workflows.

For local development you can populate this directory with:

```powershell
.\scripts\sync-sts2-refs.ps1
```

This directory is intended to be committed to the repository so CI can build without depending on a local Steam install on the runner.
