# Unity Utils

Small, independent Unity Editor utilities, installable via Unity Package Manager:

- **Bootstrap Scene Kit** — additively loads a scene of persistent singletons (audio manager,
  music player, game manager, ...) before any other scene's `Awake` runs, regardless of which
  scene Play (or the build) actually starts from. Removes the need to duplicate those objects in
  every scene "just in case." Now also handles being played *from* the bootstrap scene itself.
- **Update Checker** — automatic, zero-config update notification for projects using the package,
  just like [Rewired Helper](https://github.com/wagenheimer/RewiredHelper) has.
- **Audio Listener Cleaner** — removes duplicate `AudioListener`s across every scene in the
  project, keeping the one on your designated persistent object.
- **TMP CanvasRenderer Cleaner** — removes stray `CanvasRenderer` components sitting next to
  world-space `TextMeshPro` objects (the `CanvasRenderer` is only needed by the UGUI variant),
  which otherwise spam `"Please remove the CanvasRenderer component..."` warnings.
- **TMP TextContainer Cleaner** — removes the obsolete `TMPro.TextContainer` component left over
  on `TextMeshPro` objects from older TMP versions, which otherwise spam `"The Text Container
  component is now Obsolete and can safely be removed..."` warnings.
- **Unused IAPButton Cleaner** *(only active if `com.unity.purchasing` is installed)* — removes
  Codeless `IAPButton` components whose purchase/failure/restore events are all empty. Leftover
  IAPButton components with nothing wired to them still trigger Codeless IAP's automatic store
  connection on startup, which otherwise spams `"IStoreService.Connect called without a callback
  defined..."` warnings for projects that drive purchases through `UnityIAPServices` directly.
- **App Bundle Size Warning Tool** — toggles Unity's built-in "Warn about App Bundle size" check
  (`Player Settings > Other Settings`) from a menu item, so projects whose .aab always exceeds
  the Google Play threshold can silence the warning shown after every Android build.

Each tool is standalone — use one, some, or all of them.

## Installation

Via Unity Package Manager, using the git URL:

```
https://github.com/wagenheimer/UnityUtils.git
```

`Window > Package Manager > + > Add package from git URL...` and paste the URL above. To pin a
specific version, append `#vX.Y.Z` (see the repo's tags).

### Updating

The package ships with a built-in **Update Checker** (`Editor/UpdateChecker.cs`):

- On Editor startup it checks this repo's `package.json` (on `master`) **once every 24 hours**
  and logs / prompts when the remote version is newer than the installed one.
- You can force a check any time via **`Tools > Wagenheimer > Unity Utils > Check for Updates...`**.

No configuration required. Checks are stored in `EditorPrefs` under
`Wagenheimer.UnityUtils.UpdateChecker.*` and never block the Editor (5s network timeout).

> Versions are bumped **automatically by CI** on every push to `master` — see
> [Versioning & Releases](#versioning--releases) below.

## Bootstrap Scene Kit

### Why

If your persistent objects are `DontDestroyOnLoad` singletons that self-destruct on duplicate
(`if (instance != null) Destroy(gameObject); else { instance = this; DontDestroyOnLoad(gameObject); }`),
having a copy of them placed directly in *every* scene technically still works — but every scene
load pays the cost of instantiating and then immediately destroying full copies of those objects,
and the brief window before dedup runs is a common source of "2 audio listeners in the scene" /
"only one active EventSystem" style warnings. A single bootstrap scene removes the duplication
entirely instead of just cleaning up after it.

### How it works

`BootstrapLoader` uses `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`
— a plain static method Unity calls once at startup, before the very first scene's objects wake
up, *regardless of which scene that is*. No scene reordering, no Editor-only "Play Mode Start
Scene" tricks, no capturing "which scene did I have open" state: it works identically whether you
press Play from any scene in the Editor or launch a real build, because the callback fires before
*any* scene's `Awake`, full stop. It just additively loads your bootstrap scene alongside whatever
scene was already starting.

Keeping the persistent objects in an actual **scene** (rather than e.g. spawning them from a
ScriptableObject config) means you can keep adding more to it later — extra managers, whatever —
just by opening the scene and dropping things into it like any other scene.

#### Playing from the Bootstrap Scene

Since v1.1.0, pressing **Play while the bootstrap scene itself is open** no longer leaves you
stuck in an empty persistent-objects scene. When that happens, `BootstrapLoader` detects it and
additively loads the **first enabled scene in Build Settings**, then makes it the active scene —
so you always boot into real gameplay while keeping the bootstrap (and its persistent objects)
alive, exactly as in normal flow.

This only applies in the **Editor**: in a real build the entry point is whatever scene is first
in Build Settings, so re-loading it would just duplicate it.

### Setup

1. `Tools > Unity Utils > Bootstrap > 1. Create Settings Asset` — creates a `BootstrapSettings`
   asset under `Assets/Resources/Wagenheimer/` (must stay under a `Resources` folder so
   `BootstrapLoader` can find it at runtime).
2. On the created asset, set **Bootstrap Scene Name** and drag your persistent prefabs into
   **Persistent Prefabs**. Each should already be a self-persisting `DontDestroyOnLoad` singleton
   as described above — this kit stops the duplication from happening per-scene, it doesn't add
   the singleton behavior itself.
3. `Tools > Unity Utils > Bootstrap > 2. Create/Rebuild Bootstrap Scene` — creates the scene,
   instantiates the prefabs into it, and registers it in Build Settings (required for
   `SceneManager.LoadScene` by name to work in a build). Re-run any time you change the prefab
   list to rebuild it.
4. `Tools > Unity Utils > Bootstrap > 3. Remove Persistent Prefabs From Other Scenes` — opens
   every other scene in the project, removes any instance of those same prefabs, and saves
   whatever changed.

From then on, `BootstrapLoader` (a plain runtime static class — not attached to any scene) takes
care of loading the bootstrap scene automatically.

## Audio Listener Cleaner

`Tools > Unity Utils > Cleanup > Remove Duplicate Audio Listeners (All Scenes)`

Opens every scene, and where more than one `AudioListener` is present, keeps the one belonging to
the first prefab in your `BootstrapSettings`' Persistent Prefabs list and removes the rest. If no
`BootstrapSettings` asset exists, or that prefab isn't present in a given scene, falls back to
keeping the first one found (logging a warning so you can double check).

## TMP CanvasRenderer Cleaner

`Tools > Unity Utils > Cleanup > Remove Redundant TMP CanvasRenderer (All Scenes && Prefabs)`

Scans every scene and prefab in the project and removes any `CanvasRenderer` sitting on the same
GameObject as a world-space `TextMeshPro` component. Fully generic — no project-specific paths.

## TMP TextContainer Cleaner

`Tools > Unity Utils > Cleanup > Remove Obsolete TMP TextContainer (All Scenes && Prefabs)`
(and an `(Active Scene Only)` variant)

Scans every scene and prefab in the project and removes any obsolete `TMPro.TextContainer`
component it finds. Fully generic — no project-specific paths.

## Unused IAPButton Cleaner

`Tools > Unity Utils > Cleanup > Remove Unused IAPButton Components (All Scenes && Prefabs)`
(and an `(Active Scene Only)` variant)

Only compiled in when `com.unity.purchasing` is installed. Scans every scene and prefab, and
removes any `IAPButton` whose `onPurchaseComplete`/`onPurchaseFailed`/`onTransactionsRestored`
UnityEvents are all empty — i.e. doing nothing. Any IAPButton with real listeners wired is left
untouched (and logged), so this is safe to run even if some buttons in your project genuinely use
Codeless IAP.

## App Bundle Size Warning Tool

`Tools > Wagenheimer > Unity Utils > Android > App Bundle Size Warning`

Toggles Unity's built-in **"Warn about App Bundle size"** check (`Player Settings > Other Settings
> Build > Warn about App Bundle size`, threshold 200 MB by default). After every release AAB build,
Unity checks the bundle's estimated download size and warns when it exceeds the Google Play limit —
useful once, annoying forever for games that always exceed it.

- **Toggle** — checked menu item; the checkmark reflects the current Player Settings state.
- **Enable** / **Disable** — explicit, with a confirmation log.
- **Log Status** — prints the current state and threshold to the Console.

The state is stored in the project itself (`AndroidValidateAppBundleSize` in
`ProjectSettings.asset`), so it is committed with the project like any other Player Setting and
both paths — the undocumented `validateAppBundleSize`/`appBundleSizeToValidate` PlayerSettings
properties and a direct `ProjectSettings.asset` fallback — produce the same result.

Build scripts can call it automatically before `BuildPipeline.BuildPlayer`:

```csharp
Wagenheimer.UnityUtils.Editor.AabSizeWarningTool.SetEnabled(false);
```

`SetEnabled` is a silent no-op when the state already matches, so calling it on every Android
build is free.

## Versioning & Releases

Version bumps are fully automated by GitHub Actions (`.github/workflows/bump-version.yml`):

| Commit message | Version bump |
|---|---|
| `feat:` / `feat(scope):` | **minor** |
| `fix:` / anything else | **patch** |
| `feat!:` or `BREAKING CHANGE` in body | **major** |

On every push to `master`, the workflow:

1. Bumps `package.json` according to the commit message;
2. Regenerates the top entry of [`CHANGELOG.md`](CHANGELOG.md) from the commits since the last tag;
3. Commits as `chore: bump version to X.Y.Z`, tags `vX.Y.Z` and pushes;
4. Creates a GitHub Release with auto-generated release notes.

Because of that, users get update notifications from the Update Checker automatically — you never
touch `package.json` or the changelog by hand. Just write conventional commit messages.

> Note: pushes that only touch `package.json` / `CHANGELOG.md` don't trigger a new bump (that's
> the bot committing), and neither do commits starting with `chore: bump version`.

## Requirements

- Unity 2021.3+
- `com.unity.textmeshpro` (only needed for the TMP CanvasRenderer Cleaner)

## See Also

- [Rewired Helper](https://github.com/wagenheimer/RewiredHelper) — input-type detection and UI
  helper layer on top of Rewired, with the same update-check/versioning scheme.

## License

MIT — see [LICENSE](LICENSE).
