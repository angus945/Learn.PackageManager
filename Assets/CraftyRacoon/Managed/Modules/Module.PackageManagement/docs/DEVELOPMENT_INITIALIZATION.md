# Development initialization contracts

The semantic core now defines portable source/projection layout and backing-transition decisions. It does not acquire sources, move directories, run Git or require Discovery.

`PackageDirectoryLayout` owns explicit relative locations, case-insensitive identity/path collision checks and managed-root containment. Source locations are PackageSet-relative; original/projection locations are Target-relative. Directory names are not architecture identities.

`DevelopmentBackingPolicy` classifies an observed projection as `Absent`, `PhysicalSnapshot`, `BootstrapGitlink`, `CorrectJunction`, or `Conflict`. Absent owned paths may be materialized by Use Existing during offline Apply. Physical snapshots and Bootstrap Gitlinks require exact source, pin, content, metadata ownership, and path ownership evidence. A correct Junction is idempotent; dirty or mismatched sources are blocked, while Project-only edits and invalid ownership are conflicts.

`LocalDraft` is valid only for an aligned physical snapshot. It is an independent committed local source, explicitly `NeedsPublish`, not a remotely restorable submodule.

Host adapters must additionally verify actual filesystem containment, reparse points, Git ownership and source/index/content fingerprints immediately before cutover. An evidence token is an observation, not authorization to skip revalidation.

These repositories contain code, not serialized Unity content. Their generated `.meta` files are ignored by canonical Git. Project GUID metadata must be preserved by the Unity projection adapter; never regenerate it during conversion. Future serialized assets require an explicit tracked-metadata policy.

Run `dotnet run --project tests/Development.Contracts/Development.Contracts.csproj`. The test assembly is excluded from Unity unless the dedicated test define is explicitly enabled.
