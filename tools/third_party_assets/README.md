# CC0 Asset Archive

`sync_cc0_assets.ps1` downloads every asset pack that the official Kenney catalog declares CC0 and every public KayKit repository that explicitly declares CC0. Archives and provider-scoped manifests are kept under `external/cc0-assets/`, which is intentionally excluded from Git because the complete collection is a large, third-party binary archive.

The verified archive is published at [MightyBubble/Ludots-Assets](https://github.com/MightyBubble/Ludots-Assets). Clone it into the default local asset path to obtain the complete collection through Git LFS:

```powershell
git clone https://github.com/MightyBubble/Ludots-Assets.git external/cc0-assets
```

To inspect manifests and sources without downloading the 486 MiB LFS payload:

```powershell
$env:GIT_LFS_SKIP_SMUDGE = '1'
git clone https://github.com/MightyBubble/Ludots-Assets.git external/cc0-assets
Remove-Item Env:GIT_LFS_SKIP_SMUDGE
```

Run from the repository root:

```powershell
pwsh -File tools/third_party_assets/sync_cc0_assets.ps1
```

The script stops on a missing catalog entry, a pack without an explicit CC0 declaration, a missing official archive URL, or a failed, empty, or unreadable ZIP download. It records the exact source page, license location, byte size, and SHA-256 of every archive.

Use `-Provider kenney` or `-Provider kaykit` to synchronize one publisher independently. The manifest is named for the synchronized scope, so a later partial run cannot overwrite the all-provider inventory.
