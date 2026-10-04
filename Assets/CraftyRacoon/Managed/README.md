# PackageSet Managed Projection

This directory is owned by the PackageSet projection workflow.

Rules:

- Do not author reusable source directly under this directory.
- Do not place project-owned content under this directory.
- Each child projection must be reproducible from a canonical PackageSet source revision.
- Distribution projections are committed to this Unity repository.
- Development junctions are local-only experimental state and must not become the distribution state.

Canonical source composition repository:

`angus945/Learn.PackageSet.PackageManager`
