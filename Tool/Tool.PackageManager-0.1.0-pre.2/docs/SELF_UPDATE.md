# Tool.PackageManager Self Update

## Purpose

Self update replaces the files owned by the Tool distribution itself. It is separate from Package Management bootstrap and from post-bootstrap package-management workflows.

Entry point:

```text
Update Tool.PackageManager.cmd
```

## Version source

The updater reads GitHub **Release metadata**, not just tag names.

The selection menu contains:

```text
main                  [Development]
v0.4.0                [Stable]
v0.4.0-rc.1           [Pre-release]
v0.3.0                [Stable]
```

Rules:

- `main` is always shown as `[Development]`;
- a published release with `prerelease: false` is shown as `[Stable]`;
- a published release with `prerelease: true` is shown as `[Pre-release]`;
- releases with `draft: true` are not shown;
- a bare Git tag with no GitHub Release is not shown.

Release status therefore comes from GitHub metadata rather than from parsing names such as `alpha`, `beta`, or `rc`.

A selectable release becomes self-update compatible when its tagged revision contains:

```text
config/self-update-manifest.json
```

Older releases without the manifest are deliberately rejected instead of being applied with guessed file ownership.

## Update ownership

`config/self-update-manifest.json` declares every repository file owned by self update.

The updater may:

- overwrite files listed by the selected version;
- remove files listed by the current manifest but absent from the selected version.

It does not delete unrelated files placed in the Tool directory by the user.

## Apply model

The running updater does not overwrite itself in place.

```text
Update Tool.PackageManager.cmd
        ↓
update.ps1
        ↓
select development / stable / prerelease version
        ↓
download private GitHub archive to %TEMP%
        ↓
validate selected self-update manifest
        ↓
launch temporary apply helper
        ↓
current updater / CMD exits
        ↓
helper backs up current managed files
        ↓
copy selected managed files + remove stale managed files
        ↓
success
or rollback on failure
        ↓
show result and wait for user
```

This supports GitHub ZIP/source-download installations as well as ordinary folders. A local `.git` checkout is not required.

## Authentication

Self update uses the same GitHub CLI authentication prerequisite as bootstrap:

```text
gh auth status
gh auth token
```

The token is used only for the private GitHub archive request and is not written into the Tool directory or update manifest.


## Known-bad release blocking

`config/self-update-policy.json` can hide releases that must not be installed.

Blocked refs are omitted from the version-selection menu. This is used for releases with known updater defects that could damage the local Tool installation.

## Transaction safety

The apply helper uses explicit phases:

```text
preflight
    ↓
backup
    ↓
mutation
    ↓
success
or rollback
```

A failure during preflight must not run rollback because no target file has been modified yet.

When a newly managed file already exists locally but was not owned by the previous manifest, the updater may adopt it only if its SHA-256 content is identical to the selected version. A differing unmanaged file blocks the update.


## Version document

The Tool no longer uses a plain `VERSION` marker.

The current version is represented by exactly one Markdown file at the repository root:

```text
v<semver>.md
```

For example:

```text
v1.2.3.md
```

The filename is the version identity. Its Markdown body is also used as the GitHub Release notes.

A valid Tool distribution must contain exactly one root file matching the supported semantic-version document pattern. During self update, the selected release tag must match that document filename (without `.md`).

Advancing the version means renaming the version document, updating its release notes, and updating `config/self-update-manifest.json` to own the new filename.
