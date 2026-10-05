# Bootstrap Plan Schema v2

Source: config/bootstrap-plans.json

## Purpose

A bootstrap plan declares what must be acquired and may declare the minimum host activation constraint required for those sources to work.

Initial source placement is still selected by the user when the bootstrapper runs. Later relocation or reorganization belongs to Framework.PackageManagement.

## Installable component

~~~json
{
  "id": "Framework.PackageManagement",
  "repository": "crafty-racoon/Framework.PackageManagement",
  "url": "https://github.com/crafty-racoon/Framework.PackageManagement.git",
  "revision": "506a2a6cbcd71415da1a7f6f08edcb83fff9af4f"
}
~~~

Rules:

- id, repository, url, and revision are required.
- revision must be an exact 40-character commit SHA.
- component id becomes its initial directory name.
- plans do not declare fixed project-relative component paths.
- repository acquisition uses Git submodules.

## Optional source-root constraint

A host plan may declare:

~~~json
"sourceRootConstraint": "unity-assets"
~~~

`unity-assets` means the user-selected initial source root must be inside the `Assets` directory of a valid Unity project containing `Assets/`, `Packages/`, and `ProjectSettings/`.

This does not choose the source-root folder name. It only enforces the host loading boundary.

Examples of valid user choices:

~~~text
Assets/CraftyRacoon/PackageManagement
Assets/External/CraftyRacoon
Game/Assets/Infrastructure/PackageManagement
~~~

assuming the corresponding Unity project root is valid.

## Interactive initial folder selection

The bootstrapper starts at the target Git root and shows only the current directory's immediate child directories.

~~~text
Choose initial installation folder: <Git Root>

> [+] Create new folder here
  Assets/
  Packages/
  Submodules/
~~~

Selecting an existing directory enters that directory and repeats the same process. Directory entries are displayed with their complete Git-root-relative paths so the current project location remains explicit.

Below the Git root, the menu provides two ways to finalize the initial source root:

~~~text
[.] Use this folder
[+] Create new folder here
~~~

`[.] Use this folder` selects the existing current directory directly.

`[+] Create new folder here` asks for one new folder name, creates it, validates any plan constraint, and installs all selected Package Management components inside it. While editing the new folder name, `Esc` returns to the same folder menu instead of cancelling bootstrap.

For automation, `-SourceRoot <relative folder>` bypasses the browser and creates the directory if it does not exist. The same plan constraint validation still applies.

## Handoff

The bootstrapper records the resolved initial topology in:

    PackageManagement/bootstrap.plan.json

The handoff contains `initialSourceRoot` and each component's resolved path.

This is an observation of the initial installation result. It is not a permanent path policy. Future relocation or reorganization is owned by the normal Package Management workflow.

## Non-goals

Do not add package requirements, PackageSet inventory, projection state, update or repair decisions, relocation workflows, Junction/materialization rules, or Unity Package Manager state to this bootstrap schema.
