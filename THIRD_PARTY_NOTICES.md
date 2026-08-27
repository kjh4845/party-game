# Third-Party Source Inventory and Notice Index

This file is the human-readable index for `LIC-001`. It records the approved source-package inventory; it is not the final Windows Player notice bundle and does not reproduce complete license texts.

Normative records:

- Policy: [`config/licenses/LicensePolicy.yaml`](config/licenses/LicensePolicy.yaml)
- Full 58-package inventory: [`config/licenses/ThirdPartyInventory.yaml`](config/licenses/ThirdPartyInventory.yaml)
- Direct package source: [`Project hotfix/Packages/manifest.json`](Project%20hotfix/Packages/manifest.json)
- Resolved package source: [`Project hotfix/Packages/packages-lock.json`](Project%20hotfix/Packages/packages-lock.json)

Snapshot: Unity `6000.3.9f1 (7a9955a4f2fa)`, direct packages `46`, transitive packages `12`, locked entries `58`. Package license and notice files are addressed by package id, resolved version, and package-relative filename so this index does not depend on one machine's generated `Library/PackageCache` path.

Primary Unity terms referenced by the resolved packages:

- [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)
- [Unity Package Distribution License](https://unity.com/legal/licenses/unity-package-distribution-license)
- [Unity Editor Software Terms](https://unity.com/legal/editor-terms-of-service/software)

## Runtime and build candidates

“Candidate” means source that can affect a Player or its compiled assets. It does not assert that the component is present in a built Player. The user-run Windows build audit decides the final release notice set.

| Resolved package | Primary family | Package-relative evidence | Current notice disposition |
|---|---|---|---|
| `com.unity.ai.navigation@2.0.10` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.burst@1.8.28` | Unity Companion / Package Distribution, bundled permissive dependencies | `LICENSE.md`, `Third Party Notices.md` | Build candidate; preserve bundled notice if included |
| `com.unity.collections@2.6.2` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.inputsystem@1.18.0` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.mathematics@1.3.3` | Unity Companion License, bundled MIT Noise code | `LICENSE.md`, `Unity.Mathematics/Noise/LICENSE` | Reachability pending; preserve Noise license if included |
| `com.unity.render-pipelines.core@17.3.0` | Unity Companion License, bundled MIT/Zlib/inline permissive material | `LICENSE.md`, `THIRD PARTY NOTICES.md`, applicable inline shader headers | Build candidate; preserve bundled and applicable inline notices |
| `com.unity.render-pipelines.universal@17.3.0` | Unity Companion License, bundled FXAA/Boost/MIT material | `LICENSE.md`, `Third Party Notices.md`, applicable inline source headers | Build candidate; preserve bundled and applicable inline notices |
| `com.unity.render-pipelines.universal-config@17.0.3` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.shadergraph@17.3.0` | Unity Companion License, inline MIT shader headers | `LICENSE.md`, applicable `ShaderGraphLibrary` headers | Compiled-shader audit pending |
| `com.unity.timeline@1.8.10` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.transport@2.6.0` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.ugui@2.0.0` | Unity Companion License | `LICENSE.md` | No package third-party notice declared |
| `com.unity.visualscripting@1.9.9` | Unity Package Distribution License with bundled MS-PL, CC-BY-3.0, MIT, BSD, Public Domain, and bespoke notices | `LICENSE.md`, `Third Party Notices.md` | Player inclusion pending; preserve the complete bundled notice if included |

### Unity built-in engine modules

The lock contains 36 `com.unity.modules.*@1.0.0` entries: 34 direct and 2 transitive. They expose built-in Unity engine/player modules rather than standalone package source with a per-package license file. The audited Unity editor contains no LICENSE or Third Party Notice inside those module package directories.

Their current source basis is:

- each explicit module row in `config/licenses/ThirdPartyInventory.yaml`;
- its `BuiltInPackages/<package-id>/package.json` metadata in Unity `6000.3.9f1`;
- the Unity Editor Software Terms and the installed editor's aggregate `Contents/Resources/legal.txt`.

The aggregate Unity notice is not treated as a per-module mapping. Applicable Unity engine/player notices must be selected from the actual user-built Windows Player during the final audit.

## Editor and test inventory

These packages remain in the source inventory because they are resolved dependencies. Their current structure is editor/test-oriented, so they are not promoted into the Player notice set without contrary build evidence.

| Resolved package | Classification | Primary/bundled family | Package-relative evidence |
|---|---|---|---|
| `com.unity.collab-proxy@2.11.3` | Editor only | Unity Package Distribution; MIT/Zlib/BSD/Apache-2.0 notices | `LICENSE.md`, `Third Party Notices.md` |
| `com.unity.ext.nunit@2.0.5` | Test/editor | Unity Package Distribution; NUnit MIT notice | `LICENSE.md`, `Third Party Notices.md` |
| `com.unity.ide.rider@3.0.39` | Editor only | MIT | `LICENSE.md` |
| `com.unity.ide.visualstudio@2.0.26` | Editor only | MIT; bundled MIT/0BSD | `LICENSE.md`, `ThirdPartyNotices.md` |
| `com.unity.multiplayer.center@1.0.1` | Editor with unscoped Common assembly | Unity Package Distribution | `LICENSE.md`; bundled notice declares no third-party software |
| `com.unity.nuget.mono-cecil@1.11.6` | Editor-only DLL import | Unity Companion; Mono.Cecil MIT notice | `LICENSE.md`, `Third Party Notices.md` |
| `com.unity.searcher@4.9.4` | Editor only | Unity Companion Package License v1.0 | `LICENSE.md` |
| `com.unity.test-framework@1.6.0` | Test tooling | Unity Companion License | `LICENSE.md` |
| `com.unity.test-framework.performance@3.2.0` | Test tooling | Unity Companion; Perfolizer MIT notice | `LICENSE.md`, `Third Party Notices.md` |

## Review-only C2PA images

The inventory contains 18 repository images with embedded C2PA claims. C2PA provenance is not a copyright license or redistribution grant. Every one of these entries is recorded path-by-path in `reviewOnlyAssets` with:

- `intendedUse: DESIGN_REFERENCE_ONLY`;
- `reviewOnly: true`;
- `shippingAllowed: false`;
- `rightsStatus: NOT_PROVEN_FOR_SHIPPING`.

The original review files must not be directly imported, copied, re-encoded, or used as Player textures/sprites/UI, Steam store/marketing media, redistributed source art, or mechanically generated shipping derivatives. A human artist may use an approved design image as visual reference while creating a new original production asset. Shipping the original file, a re-encoded copy, or a mechanical derivative requires new commercial-use and redistribution evidence; the embedded C2PA claim alone is insufficient.

## Final Windows Player audit

No Player build was run for `LIC-001`. The audited editor installation currently has `MacStandaloneSupport` only, so Windows-specific Player files and notices are not yet observable.

After the user performs the Windows x64 build:

1. `BLD-001` compares actual managed/native Player contents against the 58-entry source inventory.
2. Applicable Unity engine and package notices are assembled without silently dropping a notice because code or shaders may have been stripped.
3. Editor/test candidates may be excluded only with build evidence showing they are absent.
4. `ALP-001` records the final release notice audit and the identity of the user-built artifact.

Automatic Build remains prohibited. This inventory is engineering evidence, not a legal opinion.
