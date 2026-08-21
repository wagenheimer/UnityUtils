# Unity Utils

Small, independent Unity Editor utilities, installable via Unity Package Manager:

- **Bootstrap Scene Kit** — additively loads a scene of persistent singletons (audio manager,
  music player, game manager, ...) before any other scene's `Awake` runs, regardless of which
  scene Play (or the build) starts from. Removes the need to duplicate those objects in every
  scene "just in case."
- **Audio Listener Cleaner** — removes duplicate `AudioListener`s across every scene in the
  project, keeping the one on your designated persistent object.
- **TMP CanvasRenderer Cleaner** — removes stray `CanvasRenderer` components sitting next to
  world-space `TextMeshPro` objects (the `CanvasRenderer` is only needed by the UGUI variant),
  which otherwise spam `"Please remove the CanvasRenderer component..."` warnings.
- **TMP TextContainer Cleaner** — removes the obsolete `TMPro.TextContainer` component left over
  on `TextMeshPro` objects from older TMP versions, which otherwise spam `"The Text Container
  component is now Obsolete and can safely be removed..."` warnings.

Each tool is standalone — use one, some, or all of them.

## Installation

Via Unity Package Manager, using the git URL:

```
https://github.com/wagenheimer/UnityUtils.git
```

`Window > Package Manager > + > Add package from git URL...` and paste the URL above. To pin a
specific version, append `#vX.Y.Z` (see the repo's tags).

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

## Requirements

- Unity 2021.3+
- `com.unity.textmeshpro` (only needed for the TMP CanvasRenderer Cleaner)

## License

MIT — see [LICENSE](LICENSE).
