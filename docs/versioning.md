# Versioning

Avila uses calendar-based major versions.

The release marker lives in `Versionate.txt` and is the single source of truth for the current engine release.

Current official release:

```txt
26.0 Release
```

Meaning:

- `26` matches the 2026 release cycle.
- `0` is the first official release in that cycle.
- future patch releases can move to `26.1`, `26.2`, and so on if needed.

The packaged output mirrors `Versionate.txt` into `dist` and `buildclear/dist`, so the executable and the release folder stay in sync.
