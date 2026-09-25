# Unity Utils

A collection of lightweight, high-productivity Unity Editor and runtime utilities designed to streamline game initialization, prevent multi-scene singleton conflicts, and optimize project cleanliness.

Includes a modern **UI Toolkit Dashboard**, an automated **Bootstrap Diagnostic Checker** with one-click fixes, a comprehensive **Project Cleanup Suite**, and **Android App Bundle** optimization utilities.

---

## Key Features

- **Modern UI Toolkit Dashboard (`UnityUtilsHubWindow`)**:
  - Centralized, high-performance editor hub replacing legacy IMGUI windows.
  - Dark-slate design system with real-time metric cards, responsive status badges, and tabbed workflow.
- **Bootstrap Scene Kit & Diagnostic Checker**:
  - Additively loads persistent singletons (`AudioManager`, `GameManager`, UI canvases, etc.) before any other scene's `Awake` runs.
  - Automatically handles playing directly from the bootstrap scene in Editor Play Mode by loading the first Build Settings scene additively.
  - **Diagnostic Checker (`BootstrapChecker`)**: Audits settings asset placement, scene presence on disk, build index 0 configuration, and detects leaking prefabs in gameplay scenes with **One-Click Quick Fixes**.
- **Project Cleanup Suite**:
  - **Audio Listener Cleaner**: Removes duplicate `AudioListener` components across scenes and prefabs, enforcing a single persistent listener.
  - **Missing Script Cleaner**: Detects and strips missing MonoBehaviour script references across all scenes and prefabs.
  - **TextMesh Pro CanvasRenderer Cleaner**: Strips redundant `CanvasRenderer` components attached to world-space `TextMeshPro` objects.
  - **TextMesh Pro TextContainer Cleaner**: Removes obsolete `TMPro.TextContainer` components left over from legacy TMP packages.
  - **Animator Transition Auditor**: Audits and auto-repairs invalid animator state transitions missing exit times or conditions.
  - **Legacy Component Modernizer**: Re-serializes scenes and prefabs using the active Unity engine format.
  - **Unused Codeless IAP Button Cleaner**: Strips empty, disconnected `IAPButton` components (active when `com.unity.purchasing` is installed).
- **Third-Party Asset Slimmer (`ThirdPartySlimmerView`)**:
  - **GUID-based usage audit** resolving `m_Script`/`m_Shader` references across scenes, prefabs and materials, while ignoring the pack's own Examples/Doc content so bundled demos do not make everything look "used".
  - **Slim In-Place**: deletes unused scripts, shaders and `Resources` assets, relocates editor-only inspector resources from `Resources/` into `Editor/Resources/` (out of the build), and can drop Examples/Doc/ExtraShaders folders.
  - **Extract Used Only**: relocates the referenced subset to `Assets/ThirdParty/<Pack>-Slim` with asset GUIDs preserved and removes the original pack.
  - **URP Shader Port**: rewrites used Built-in shaders to URP in place, keeping shader names and GUIDs; classic vertex/fragment shaders are converted mechanically and legacy surface shaders are transpiled with a manual-review flag. Originals are backed up for one-click restore.
  - Ships with a built-in 2DxFX profile and auto-detection.
- **Android App Bundle Size Warning Tool**:
  - Inspects and toggles Unity's built-in 200 MB App Bundle size validation warning (`Player Settings > Other Settings > Warn about App Bundle size`) directly from menus or automated CI build scripts.
- **Runtime Optimization Helpers**:
  - `SingleAudioListener`: Enforces a single active AudioListener at runtime across additively loaded scenes.
  - `CLZF2`: Zero-dependency, high-speed LZF byte compression and decompression routines.

---

## Installation

### Via Unity Package Manager (Git URL)

1. Open Unity and navigate to **Window > Package Manager**.
2. Click the **+** button in the top-left and select **Add package from git URL...**.
3. Enter:
   ```
   https://github.com/wagenheimer/UnityUtils.git
   ```
4. To target a specific version tag, append `#vX.Y.Z` (e.g., `#v1.9.0`).

### Package Hub Integration

UnityUtils integrates directly with [UnityPackageHub](https://github.com/wagenheimer/UnityPackageHub). You can check for updates anytime via:
```
Tools > Wagenheimer > Unity Utils > Check for Updates...
```

---

## Dashboard Overview

Open the central UI Toolkit dashboard via **`Tools > Wagenheimer > Unity Utils > Dashboard...`**.

The dashboard is structured into five primary workspaces:

```
┌────────────────────────────────────────────────────────────────────────┐
│  Unity Utils  v1.9.0                                                   │
│  Bootstrap Scene Kit, Diagnostics & Optimization Suite                 │
├────────────────────────────────────────────────────────────────────────┤
│  [Bootstrap & Diagnostics]  [Project Cleanup]  [Third-Party Slim]      │
│  [Android & Tools]  [About]                                            │
├────────────────────────────────────────────────────────────────────────┤
│  [ 8 Passed ]   [ 0 Warnings ]   [ 0 Errors ]                          │
│                                                                        │
│  Bootstrap Diagnostic Engine             [Run Full Scan]  [Fix All]   │
│  ├─ BootstrapSettings Located                 [PASS]                   │
│  ├─ Bootstrap Scene Found ('bootstrap')       [PASS]                   │
│  ├─ Bootstrap Scene in Build Settings (#0)    [PASS]                   │
│  └─ No Leaking Persistent Prefabs             [PASS]                   │
│                                                                        │
│  Bootstrap Scene Operations                                            │
│  [Locate Settings]  [Rebuild Scene]  [Open Scene]  [Clean Leaks]       │
└────────────────────────────────────────────────────────────────────────┘
```

### 1. Bootstrap & Diagnostics
- **Automated Diagnostic Rules**:
  - `BootstrapSettings Asset Location`: Verifies placement inside a `Resources` directory (`Assets/Resources/Wagenheimer/BootstrapSettings.asset` or `Assets/Resources/BootstrapSettings.asset`).
  - `Scene File Verification`: Confirms the target scene file exists on disk.
  - `Build Settings Validation`: Validates that the bootstrap scene is present, enabled, and assigned to **Build Index 0**.
  - `Prefab References`: Checks for empty or null entries in the persistent prefabs array.
  - `Scene Leak Detection`: Scans all gameplay scenes to find and strip accidental duplicates of persistent prefabs.
- **One-Click Quick Fixes**:
  - Instant creation of `BootstrapSettings.asset`.
  - Automatic insertion and reordering of the bootstrap scene into `EditorBuildSettings`.
  - Batch removal of lingering persistent prefabs across all other scenes.

### 2. Project Cleanup Suite
- **Full Project Audit**: Executes all diagnostic scanners with a single click, providing live counters for audio duplicates, missing scripts, and TMP redundancies.
- **Dedicated Cleaner Modules**: Individual cards with granular actions for active scenes, all scenes, or project prefabs.

### 3. Third-Party Slimmer
- **Usage Audit**: set the package root (auto-detected for 2DxFX) and run a GUID-based audit that reports used vs. unused scripts, shaders and `Resources` assets, plus the reclaimable build footprint.
- **Slim In-Place**: confirmation-gated deletion of unused assets, moving editor-only resources out of the build, and optional removal of Examples/Doc/ExtraShaders folders.
- **Extract Used Only**: moves the referenced subset to `Assets/ThirdParty/<Pack>-Slim` preserving GUIDs and deletes the original pack.
- **URP Shader Port**: rewrites used Built-in shaders to URP in place, preserving shader names and GUIDs, with automatic backup and restore.

### 4. Android & Tools
- **App Bundle Size Warning**:
  - View current state (`ENABLED` / `DISABLED`) and validation threshold (e.g. 200 MB).
  - One-click toggling and scriptable API:
    ```csharp
    Wagenheimer.UnityUtils.Editor.AabSizeWarningTool.SetEnabled(false);
    ```
- **SingleAudioListener & CLZF2**: Documentation and direct links to runtime utilities.

### 5. About & Updates
- Installed version display, release notes, license, and direct update checks via `PackageHubWindow`.

---

## Menu Reference

All commands are grouped under **`Tools > Wagenheimer > Unity Utils`**:

| Menu Command | Shortcut / Priority | Description |
|---|---|---|
| **Dashboard...** | Priority 100 | Opens the unified UI Toolkit dashboard. |
| **Bootstrap > Run Diagnostic Checker...** | Priority 120 | Opens the dashboard directly to the Bootstrap tab. |
| **Bootstrap > Create Settings Asset** | Priority 121 | Creates a default `BootstrapSettings.asset` in `Resources/Wagenheimer/`. |
| **Bootstrap > Create or Rebuild Bootstrap Scene** | Priority 122 | Generates/repopulates the bootstrap scene with configured persistent prefabs. |
| **Bootstrap > Remove Persistent Prefabs from Other Scenes** | Priority 123 | Scans all non-bootstrap scenes and strips duplicated persistent singletons. |
| **Bootstrap > Remove Persistent Prefabs from Active Scene** | Priority 124 | Strips persistent singletons from the currently open scene. |
| **Cleanup > Open Project Cleanup...** | Priority 140 | Opens the Project Cleanup Suite in the dashboard. |
| **Third-Party > Open Third-Party Slimmer...** | Priority 146 | Opens the Third-Party Asset Slimmer dashboard tab. |
| **Android > App Bundle Size Warning > Disable** | Priority 160 | Disables Unity's AAB size warning. |
| **Android > App Bundle Size Warning > Enable** | Priority 161 | Enables Unity's AAB size warning. |
| **Android > App Bundle Size Warning > Toggle** | Priority 162 | Toggles the AAB size warning state (shows checkmark). |
| **Android > App Bundle Size Warning > Log Status** | Priority 163 | Prints current AAB threshold and status to the Console. |
| **About Unity Utils...** | Priority 190 | Opens the About tab. |
| **Check for Updates...** | Priority 200 | Opens PackageHub to check for new releases. |

---

## Runtime Usage

### Bootstrap Scene Kit (`BootstrapLoader`)

`BootstrapLoader` uses Unity's `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`. It requires zero code in your gameplay scenes:
1. Create `BootstrapSettings` via `Tools > Wagenheimer > Unity Utils > Bootstrap > Create Settings Asset`.
2. Assign your persistent prefabs (e.g. `AudioManager`, `GameManager`, `UIManager`).
3. Click `Create or Rebuild Bootstrap Scene`.
4. Run your game! Whenever any scene is played, the bootstrap scene is automatically loaded additively before any object awakens.

### SingleAudioListener

Add `SingleAudioListener` to your persistent camera or audio manager GameObject:
```csharp
using Wagenheimer.UnityUtils;

// Automatically disables competing AudioListeners when new scenes load additively.
```

### Fast Compression (`CLZF2`)

```csharp
using Wagenheimer.UnityUtils;

byte[] rawData = GetSaveDataBytes();
byte[] compressed = CLZF2.Compress(rawData);

byte[] decompressed = CLZF2.Decompress(compressed);
```

---

## Requirements

- **Unity**: 2021.3 LTS or higher.
- **Dependencies**:
  - `com.unity.textmeshpro` (3.0.6+)
  - `com.wagenheimer.packagehub` (optional, auto-bootstrapped for updates)
  - `com.unity.purchasing` (optional, for Codeless IAP button cleanup)

---

## License

MIT © [Cezar Wagenheimer](https://github.com/wagenheimer)
