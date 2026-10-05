# Tool.PackageManager

Tool.PackageManager is a one-time declarative bootstrapper for enabling Package Management in a new or existing product Git repository.

It exists only to solve the Package Management chicken-and-egg problem. After bootstrap, Framework.PackageManagement owns subsequent package-management workflows.

## Entry points

Windows bootstrap:

    Bootstrap Package Management.cmd

Windows self update:

    Update Tool.PackageManager.cmd

The self updater shows `main` plus published GitHub Releases in an arrow-key menu. `main` is labeled `[Development]`, normal releases `[Stable]`, and prereleases `[Pre-release]`; draft releases are hidden. It downloads the selected revision, validates its self-update manifest, and replaces only Tool-owned files. It does not require the local Tool folder to be a Git checkout.

See `docs/SELF_UPDATE.md`.

The current Tool version is represented by a root Markdown document named after the version itself, for example `v1.2.3.md`; the file body doubles as release notes.


Interactive flow:

~~~text
find target Git root
        ↓
select plan with Up / Down + Enter
        ↓
browse folders from Git root
        ↓
select existing folder → enter it
        ↓
[+] Create new folder here → finalizes initial source root
        ↓
validate host source-root constraint, if any
        ↓
preview resolved paths
        ↓
install exact-revision submodules
        ↓
write bootstrap handoff
~~~

At each folder level the CLI shows the current Git-root-relative path and immediate child directories using their complete project-relative paths.

When the current directory is below the Git root, the menu also includes:

~~~text
[.] Use this folder  (<current project-relative path>/)
[+] Create new folder here  (<current project-relative path>/)
[..] Back  (<parent project-relative path>/)
~~~

`[.] Use this folder` allows an existing directory to become the initial source root without creating another folder.

While editing `New folder name`, `Esc` returns to the folder menu at the same current directory. It does not cancel the whole bootstrap.

The plan does not define a fixed installation folder. A host plan may declare a placement constraint that is required for that host to load the source.

After bootstrap, later relocation or reorganization belongs to Framework.PackageManagement, not this Tool.

## Current plans

### .NET

Installs:

~~~text
Module.PackageManagement
Framework.PackageManagement
~~~

The initial source root can be any valid folder inside the target Git repository.

### Unity

Installs all three repositories directly as Git submodules:

~~~text
Module.PackageManagement
Framework.PackageManagement
Framework.PackageManagement.Unity3D
~~~

There is no Unity Package Manager installation step.

Because Unity must compile these repositories directly, the selected initial source root must be inside a valid Unity project's `Assets` tree. The folder name remains user-selected.

Example:

~~~text
<Project Git Root>/
└─ Assets/
   └─ CraftyRacoon/
      └─ PackageManagement/
         ├─ Module.PackageManagement/
         ├─ Framework.PackageManagement/
         └─ Framework.PackageManagement.Unity3D/
~~~

A Git repository may contain the Unity project below the Git root; the same rule applies to that Unity project's own `Assets` tree.

## Requirements

- Windows PowerShell
- Git
- GitHub CLI authenticated for the required repositories

## Non-interactive examples

.NET:

~~~bat
"Bootstrap Package Management.cmd" ^
  -ProjectPath "C:\Work\Product" ^
  -Plan dotnet ^
  -SourceRoot "Submodules/PackageManagement" ^
  -NonInteractive
~~~

Unity:

~~~bat
"Bootstrap Package Management.cmd" ^
  -ProjectPath "C:\Work\Project.Project01" ^
  -Plan unity ^
  -SourceRoot "Assets/CraftyRacoon/PackageManagement" ^
  -NonInteractive
~~~

Interactive runs should omit -Plan and -SourceRoot to use the arrow-key selectors.

## Interrupted submodule retry

If a previous bootstrap attempt was interrupted after Git created the internal submodule repository but before the submodule was registered successfully, Git may leave an orphaned cache under the parent repository's `.git/modules/...` tree.

On retry, the bootstrapper:

- detects the cached Git directory for the intended submodule path;
- reads its `origin`;
- reuses it with `git submodule add --force` only when the repository identity matches the plan;
- refuses reuse when the cached origin differs or cannot be established.

This handles Git's `A git directory ... is found locally` retry case without blindly forcing an unrelated cached repository.

## Handoff

The bootstrapper writes:

    PackageManagement/bootstrap.plan.json

The handoff stores the selected plan, exact revisions, the created initial source root, and every resolved component path.

This records what the bootstrapper created. It is not a permanent source-layout policy.

## Repository boundary

This repository owns only the 0 -> 1 bootstrap step. The bootstrap plan catalog is intentionally separate from managed-package manifests and PackageSet semantics.
