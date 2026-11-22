# Plugins

Each folder here is one self-contained tool: engine, page and view models together, in its own
namespace (`FluentCleaner.Plugins.<Name>`). Nothing outside this folder references them except
two lines in `MainWindow.xaml.cs`, which is what puts them in the `+` menu:

```csharp
new(typeof(TweaksPage), "", "NavTweaks/Content", "Tweaks"),
new(typeof(AppsPage),   "", "NavApps/Content",   "Apps"),
```

Delete a folder and its line and the app still builds — that was verified, not assumed.

| Folder | What it does | Its data |
|---|---|---|
| `Tweaks/` | Registry settings catalogue: check, apply, reset | `WinRules.ini`, any further pack |
| `Apps/` | Uninstaller for store apps and classic programs | `AppRules.ini` (classification only) |

Data files live **inside** the plugin that reads them, not in a shared store, and the build
copies them to the same relative path (`Plugins\Tweaks\WinRules.ini` beside the exe). One
folder is the whole plugin: its code, its pages and its rules. Deleting it takes all of that
with it, and nothing has to be excluded by name — the tweak catalogue reads every `.ini` in its
own folder precisely because `AppRules.ini` is not in there.

## Shared pieces

`Shared/` holds what both plugins need and nothing else does, in namespace
`FluentCleaner.Plugins` — the parent of each plugin's own namespace, so a plugin file reaches
these without a `using`. It deliberately sits here rather than in `Services\`: the core app
calls none of it, and putting it inside one plugin folder would make the other depend on that
plugin.

| File | What it is |
|---|---|
| `Loc.cs` | English resource lookup used by the ported engines |
| `WorkProgress.cs` | One progress report shape for both command bars |
| `PluginPaths.cs` | Where a plugin's own data folder is at runtime |

Remove both plugin folders and this one goes with them; nothing else breaks. Nothing in
`Shared/` may reach into a plugin.

## Adding rules without touching code

`Plugins\Tweaks\` beside the exe is the tweak catalogue's store. Dropping a `.ini` in there
installs a pack, deleting it uninstalls it, and a section name that already exists replaces
the earlier one — so a pack can correct an entry rather than only add to it.

## Writing a new plugin

1. New folder here, namespace `FluentCleaner.Plugins.<Name>`.
2. A `Page` implementing `ISearchablePage` / `IPageActions` from `FluentCleaner.Views` where it
   makes sense; the window supplies the search box, the `[...]` menu and the breadcrumb.
3. One line in `NewTabKinds`, plus `NavYourTool.Content` in `Strings\en-US`.

Keep the engine free of UI types. Both current engines were ported from Bloatynosy precisely
because they were, which is what made the move a copy rather than a rewrite.
