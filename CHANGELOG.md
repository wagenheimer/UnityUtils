name: UnityUtils

# Changelog

## [1.13.0] - 2026-09-25

### Added
- add third-party asset slimmer with usage audit, extract-used-only and URP shader port

## [1.12.0] - 2026-09-24

## [1.11.0] - 2026-09-24

### Changed
- **Bootstrap Additive Architecture**: The bootstrap scene is no longer expected or required to be Build Settings index 0. The diagnostic checker now marks scenes at index > 0 as Pass in Additive Mode with clear informational UI feedback.
- **Build Settings Fix Behavior**: Adding the bootstrap scene via the diagnostic quick-fix appends the scene rather than forcing it to index 0, preventing accidental displacement of game splash/menu startup scenes.

### Added
- **Universal Content Scene Auto-Detection (`BootstrapLoader`)**: When playing or launching directly from the bootstrap scene (in Editor or player builds), `BootstrapLoader` now automatically detects and additively loads the first non-bootstrap content scene in Build Settings, preventing frozen/empty states.

## [1.10.0] - 2026-09-24
## [1.9.0] - 2026-09-23

### Added
- **UI Toolkit Hub Window (`UnityUtilsHubWindow`)**: Complete modern dashboard replacing legacy IMGUI windows with a responsive dark-slate design system, segmented tabs, and real-time metric cards.
- **Bootstrap Diagnostic Engine (`BootstrapChecker` & `BootstrapCheckerView`)**: Automated diagnostic suite that scans settings asset location, scene on-disk presence, Build Settings indices, persistent prefab configurations, scene leaks, and audio listener conflicts, featuring one-click quick fixes.
- **Unified Project Cleanup Suite (`CleanupSuiteView`)**: Full-project one-click audit consolidating AudioListeners, Missing Scripts, TMP CanvasRenderers, TMP TextContainers, Animator Transitions, and Legacy Component modernizers.
- **Robust Runtime Loading (`BootstrapLoader`)**: Fallback lookup for `Resources/BootstrapSettings` alongside `Resources/Wagenheimer/BootstrapSettings`.
- **Streamlined Menu System**: Reorganized menu items under `Tools > Wagenheimer > Unity Utils` with standardized priority ordering.

## [1.8.1] - 2026-09-21

### Changed
- chore(deps): bump PackageHub bootstrap to v1.0.5

## [1.8.0] - 2026-09-19

## [1.7.0] - 2026-09-19

### Added
- Auto-installs `com.wagenheimer.packagehub` via git if missing, using a zero-dependency Editor bootstrap assembly (`PackageHubBootstrap`). Installing this package now pulls in PackageHub automatically, with no manual manifest edits or scoped registry required.

### Changed
- Reverted the `com.wagenheimer.packagehub` OpenUPM registry dependency added in 1.6.2: it required every consumer to configure a scoped registry manually, which defeats the "install one package, get everything" goal. The git-based auto-bootstrap replaces it.

## [1.6.2] - 2026-09-19

### Changed
- Re-added `com.wagenheimer.packagehub` as a proper semver dependency (`1.0.4`) now that it is published on the [OpenUPM registry](https://openupm.com/packages/com.wagenheimer.packagehub/). Consumers need the `com.wagenheimer` scope added to their `scopedRegistries`.

## [1.6.1] - 2026-09-18

### Fixed
- Removed `com.wagenheimer.packagehub` from `dependencies` in package.json: UPM does not support a git URL as a dependency version, which made this package fail to resolve/update in any consuming project. PackageHub must still be added directly to the consumer's manifest.json.

## [1.6.0] - 2026-09-18

## [1.5.1] - 2026-09-18

### Changed
- Standardized menu item priorities under `Tools > Wagenheimer > Unity Utils` (base priority 150) for cohesive editor grouping and ordering.
- Updated `com.wagenheimer.packagehub` dependency to `v1.0.4`.

## [1.5.0] - 2026-09-18

## [1.4.2] - 2026-09-18

### Changed
- **Centralized Update Management**: Replaced standalone update checker with dependency on `com.wagenheimer.packagehub` (`UnityPackageHub`). Updates, changelogs, and package management are now handled centrally through the unified Wagenheimer Package Hub.

## [1.4.1] - 2026-09-17

### Fixed
- add the missing meta file for CLZF2

## [1.4.0] - 2026-09-17

### Added
- add shared runtime UnityExtensions (vector/list/particle/alpha/time helpers) and reference TextMeshPro

## [1.3.0] - 2026-09-16

### Added
- add CLZF2 LZF compressor utility to runtime

## [1.2.1] - 2026-09-04

### Fixed
- nullable bool compile errors in AabSizeWarningTool

## [1.2.0] - 2026-09-04

### Added
- add App Bundle Size Warning tool to toggle Unity's AAB size check

## [1.1.4] - 2026-08-30

### Fixed
- add missing .meta files for Cleanup scripts and Runtime/Audio

## [1.1.3] - 2026-08-30

## [1.1.2] - 2026-08-30

### Changed
- Add Project Cleanup Hub window and aggregate cleanup tools

## [1.1.1] - 2026-08-22

### Fixed
- make UpdateChecker.CheckForUpdate internal so AboutWindow can call it

## [1.1.0] - 2026-08-22

### Added
- unify menus under Tools > Wagenheimer and add About window

## [1.0.8] - 2026-08-21

### Fixed
- add UpdateAvailableWindow to UnityUtils and pass correct package name

## [1.0.7] - 2026-08-21

### Fixed
- remove double semicolon from PackageDisplayName; add CHANGELOG meta

## [1.0.6] - 2026-08-21

### Added
- load first Build Settings scene when playing from bootstrap scene; add update checker

### Fixed
- move update checker menu under Tools > Wagenheimer > Unity Utils
- update checker raw URLs point to master branch

### Changed
- ci: skip existing tags when bumping version; resync version to 1.0.5
- docs: document bootstrap play-from-bootstrap behavior, update checker and CI versioning
- ci: add automatic version bump and changelog workflow

