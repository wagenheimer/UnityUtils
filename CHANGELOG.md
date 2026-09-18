name: UnityUtils

# Changelog

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

