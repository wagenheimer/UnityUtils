name: UnityUtils

# Changelog

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

