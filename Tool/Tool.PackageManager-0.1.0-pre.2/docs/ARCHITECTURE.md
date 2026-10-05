# Architecture

## Role

Tool.PackageManager is a standalone bootstrap ProductRoot whose only durable responsibility is making the first Package Management composition available inside a target product Git repository.

~~~text
Tool.PackageManager
        |
        | bootstrap only
        v
Target ProductRoot
  <user-created initial source root>/
    Module.PackageManagement/
    Framework.PackageManagement/
    <host specialization when required>/
        |
        v
Framework.PackageManagement
owns later package-management workflows
~~~

## Self-update boundary

Self update is a Tool distribution concern, separate from Package Management bootstrap.

```text
Update Tool.PackageManager.cmd
        ↓
select main / Git tag
        ↓
download + validate staged Tool distribution
        ↓
temporary external apply helper
        ↓
replace manifest-owned Tool files
        ↓
rollback on apply failure
```

`config/self-update-manifest.json` is the ownership boundary for files that may be overwritten or removed by self update. Files outside that manifest are not deleted.

The updater works from GitHub source archives and therefore does not require the local Tool folder itself to contain `.git`.

## Authority boundary

Tool.PackageManager owns:

- bootstrap plan discovery and selection;
- target Git-root resolution;
- bootstrap prerequisite checks;
- interactive browsing from the project Git root;
- creation of one new initial source folder chosen by the user;
- validation of host activation constraints;
- resolving component paths for that first installation;
- generic Git submodule acquisition;
- exact-revision checkout;
- bootstrap handoff creation.

Tool.PackageManager does not own:

- a canonical project source-folder convention;
- managed-package semantics;
- PackageSet discovery or snapshot alignment;
- normal install, update, or repair;
- post-bootstrap source relocation or reorganization;
- Junction/materialization workflows;
- Unity Editor package-management workflows.

A host activation constraint is narrower than a fixed path policy. For example, the Unity plan requires the source root to be somewhere under a valid Unity project's `Assets` tree, but does not prescribe `Assets/CraftyRacoon`, `Assets/External`, or any other folder convention.

Post-bootstrap relocation or reorganization is a Framework.PackageManagement workflow responsibility. The current Framework baseline is still read-only, so that mutation workflow is intentionally deferred rather than implemented in this bootstrapper.

## Interactive flow

~~~text
Locate target Git root
        |
        v
Select bootstrap plan
Up / Down + Enter
        |
        v
Browse folders from Git root
Up / Down + Enter
        |
        +-- select directory --> enter it and repeat
        |
        +-- [..] Back
        |
        +-- [+] Create new folder here
                     |
                     v
             enter folder name
                     |
                     v
          final initial source root
                     |
                     v
       validate host constraint
                     |
                     v
            preview resolved paths
                     |
                     v
          install exact revisions
                     |
                     v
               write handoff
~~~

An existing directory is navigation only. Creating a new folder is the operation that finalizes the initial source root.

## Bootstrap handoff

After installation the target repository contains:

    PackageManagement/bootstrap.plan.json

It records the selected plan, catalog version, user-created initial source root, component identities, exact revisions, and resolved initial paths.

This is a handoff artifact, not a permanent layout authority.

## Unity composition

The Unity plan directly installs:

~~~text
Module.PackageManagement
Framework.PackageManagement
Framework.PackageManagement.Unity3D
~~~

as Git submodules below the selected `Assets` source root.

There is no UPM activation/projection step. The generic Module and Framework repositories expose minimal Unity asmdefs; the Unity specialization references them directly.
