# Versioning

Avila uses calendar-based major versions.

The release marker lives in `Versionate.txt` and is the single source of truth for the current engine release.

Current official patch release:

```txt
26.0.2 Release
```

Meaning:

- `26` matches the 2026 release cycle.
- `0` is the first official release in that cycle.
- `2` is the second patch correction after the initial release.
- future patch releases can move to `26.0.3`, `26.0.4`, and so on if needed.

The `avila upcheck` command compares the local release marker against the current GitHub release and reports whether the engine is already up to date. The `avila upgrade` command downloads the latest compiled release, installs it, and updates PATH according to the selected scope.

The packaged output mirrors `Versionate.txt` into `dist` and `buildclear/dist`, so the executable and the release folder stay in sync.
