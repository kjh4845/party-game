# Repository Binary Asset Policy

## Status

- Owner: `FDN-011`
- Revision: `r01`
- Repository remote: not selected
- Git LFS: not installed and not enabled
- Player Build, deployment and external upload: not part of this policy

## Tracked and ignored boundaries

- Unity source under `Assets/`, `Packages/` and `ProjectSettings/` is repository source.
- Unity-generated `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, IDE projects and local Build folders are ignored.
- Unity `.meta` files, `Packages/manifest.json` and `Packages/packages-lock.json` must never be omitted when their source changes.
- Current PNG review/reference files are below 2 MiB each and remain ordinary Git binary candidates.

## LFS-required candidates

The following files must not be staged or committed until Git LFS is installed, a remote is selected and this policy plus `.gitattributes` is revised with active LFS filters:

- Blender/interop source: `.blend`, `.fbx`, `.glb`
- Lossless production audio/source images: `.wav`, `.flac`, `.psd`, `.exr`, `.hdr`, `.tif`, `.tiff`
- Any single binary file larger than 10 MiB

Do not install Git LFS, add a remote, rewrite history or upload an asset as an implicit implementation step.

## Hash and review rule

1. Record relative path, byte size and SHA-256 before the first reviewed commit.
2. Blender source and Unity import artifacts later use their domain generation manifest in addition to this repository inventory.
3. A changed binary must receive a new hash; never overwrite Evidence while claiming the old revision.
4. Duplicate content hashes are allowed only when the path has an explicit historical/rejected-evidence role.
5. Before a commit, run `git check-ignore` for generated paths and inspect `git status --short` for missing `.meta` or unexpected binary files.

The current inventory is [BinaryAssetInventory.yaml](BinaryAssetInventory.yaml).
