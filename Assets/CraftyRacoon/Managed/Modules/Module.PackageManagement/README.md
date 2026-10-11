# Module.PackageManagement

Platform-neutral package-management semantic core for Crafty Racoon modular products.

This repository owns reusable package-management **domain rules and inspection composition**. It does not own a UI, Unity Editor integration, Git CLI implementation, NTFS Junction implementation, or any managed product.

## Host model

The module is designed for ordinary .NET hosts.

Examples:

- .NET console / CLI
- ASP.NET Core
- Blazor treated as a normal .NET host
- Unity through a Unity-specific adapter/composition layer

Blazor is not modeled as a special browser/WASM host at this stage. A specialization should be introduced only if a real host restriction requires one.

## Architectural boundary

```text
Host / ProductRoot
        |
        +-- host adapters
        |
        v
Module.PackageManagement
        |
        +-- package definitions
        +-- requirement inspection dispatch
        +-- package state reduction
        +-- PackageSet discovery state
        +-- snapshot alignment rules
```

`PackageSet` remains an architecture-level source/revision/projection authority. It is **not** reclassified as a Module.

## Non-goals

This module does not currently own:

- installation/update orchestration;
- transaction queues;
- Git process execution;
- filesystem mutation;
- Junction/symlink creation;
- Unity Package Manager access;
- UI.

Those capabilities belong to host adapters or, when reusable orchestration is introduced, a separate `Framework.PackageManagement`.

See [Architecture](docs/ARCHITECTURE.md).

## Unity direct-submodule compilation

The repository remains host-neutral. For Unity products that compose Package Management through direct Git submodules, `src/Module.PackageManagement/Module.PackageManagement.asmdef` exposes the same source as the `Module.PackageManagement` Unity assembly. No Unity Package Manager distribution is required.
