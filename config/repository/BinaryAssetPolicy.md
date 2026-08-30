# Repository Binary Asset Policy

## Status

- Owner: `FDN-011`
- Revision: `r07`
- Repository remote: private GitHub `origin` over HTTPS (`https://github.com/kjh4845/project-hotfix.git`)
- Git LFS: `3.8.0`, repository-local filters and pre-push hook enabled
- Initial remote backup: `main` at `8d73541`, verified before the r07 policy commit
- Existing-history LFS migration: not performed and not required
- Player Build, game/Steam deployment and public publication: not part of this policy

## Tracked and ignored boundaries

- Unity source under `Assets/`, `Packages/` and `ProjectSettings/` is repository source.
- Unity-generated `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, IDE projects and local Build folders are ignored.
- Unity `.meta` files, `Packages/manifest.json` and `Packages/packages-lock.json` must never be omitted when their source changes.
- Current PNG review/reference files are below 2 MiB each and remain ordinary Git binaries, not LFS objects.
- Those review/reference PNG files may guide approved visual implementation, but they are not Player-shipping assets and must stay outside `Project hotfix/Assets/`.
- A binary may enter the Unity Player asset boundary only when `config/licenses/ThirdPartyInventory.yaml` records it with verified provenance and `shippingAllowed: true`.

## Active LFS boundary

The following production-source extensions must use the active Git LFS clean/smudge/merge filters:

- Blender/interop source: `.blend`, `.fbx`, `.glb`
- Lossless production audio/source images: `.wav`, `.flac`, `.psd`, `.exr`, `.hdr`, `.tif`, `.tiff`
- Any single binary file larger than 10 MiB must also use LFS even when its extension is not in the default list; update `.gitattributes` before staging it.

The current repository has `4` LFS-tracked files and `4` LFS-required candidates. Core revisions `af11dd2`, `9caad6a` and `2ce7194` uploaded the first three production LFS objects and authenticated fresh clones reproduced their materialized SHA-256 values and byte sizes. Core revision `d7877b3` uploaded the fourth production LFS object for the C1B-RW-002 continuous Neutral rework source (`35f21abe5b6bcd35dc2b066aa3bd29cea5fbf8f9e8bd600b50ffa3f5daedb938`, `157613` bytes). A fresh private clone first confirmed its `131`-byte pointer and declared OID/size, then authenticated LFS fetch and checkout reproduced the same materialized SHA-256 and byte size. Existing PNGs stay in ordinary Git, and existing PNG migration remains `0`. Do not run `git lfs migrate`, rewrite existing commits, force-push, or move existing review PNGs into LFS without a separate user-approved migration plan.

Blender `.blend1`, `.blend2` and other numbered backup files are ignored as recoverable editor output. Only task-declared canonical `.blend` sources and derived `.fbx` interchange assets are eligible for LFS tracking and inventory.
Canonical `.blend` sources must remain outside `Project hotfix/Assets/`. New or active FBX records need first-party `PLAYER_CONTENT` and `shippingAllowed: true` to enter the Unity `Assets` import boundary. An explicitly rejected historical FBX may remain at its immutable path only as `SUPERSEDED_CONTENT` with `shippingAllowed: false`; it must not be activated, imported as the current candidate, or used as a rework geometry input.

Before committing a new required binary:

1. Register source, license/shipping boundary, byte size and SHA-256 in the appropriate inventory or GenerationManifest.
2. Confirm `git check-attr filter -- <path>` returns `lfs`.
3. Stage the file and confirm the Git index stores an LFS pointer while the working file retains its original bytes.
4. Confirm `git lfs status` reports the intended object and no unrelated binary.
5. Push normally and verify both the Git ref and LFS object upload; never bypass the pre-push hook.

## Hash and review rule

1. Record relative path, byte size and SHA-256 before the first reviewed commit.
2. Blender source and Unity import artifacts later use their domain generation manifest in addition to this repository inventory.
3. A changed binary must receive a new hash; never overwrite Evidence while claiming the old revision.
4. Duplicate content hashes are allowed only when the path has an explicit historical/rejected-evidence role.
5. Before a commit, run `git check-ignore` for generated paths and inspect `git status --short` for missing `.meta` or unexpected binary files.
6. Unknown provenance, unclear redistribution terms, or a `shippingAllowed: false` license record blocks import into `Project hotfix/Assets/`.
7. Run `ruby tools/verify_lfs_repository.rb --verify-local-lfs --verify-remote` before the first production binary commit and after changing LFS patterns or Remote settings.

## Remote and credential boundary

- `origin` is the only product Remote and must remain the private repository `kjh4845/project-hotfix` unless the user approves a move.
- The default branch is `main`; only user-authorized repository sync may use normal pushes. Force-push and history rewrite are not allowed.
- GitHub CLI credentials live in the operating-system keyring and must never be written to repository files, logs or Evidence.
- Repository-local Git config owns the LFS filters and GitHub CLI credential helper. Do not silently convert this to system-wide LFS configuration.
- A successful Push is not a release or deployment. Player Build, Steam upload and public publication remain separate user-controlled actions.

The current inventory is [BinaryAssetInventory.yaml](BinaryAssetInventory.yaml).
