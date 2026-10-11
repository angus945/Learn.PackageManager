# Validation status

## Historical GitHub Actions evidence

Module semantic contracts passed at `f9bed079b22e881cbbafe6a04cd1857eccaeb354`:
https://github.com/crafty-racoon/Module.PackageManagement/actions/runs/37244338462

Windows PowerShell 5.1 / real Git / NTFS offline contracts passed after the journal replacement correction at `2fbb664652dfb31a8852747ae79721bfbf8a70d3`:
https://github.com/crafty-racoon/Framework.PackageManagement.Unity3D/actions/runs/37245367964

The initial Windows run exposed PowerShell 5.1 converting a bare null string argument in File.Replace into an empty path. Journal replacement now uses an explicit unique prior-journal backup. That historical rerun verified declared-runtime conversion, GUID preservation, ordinary canonical blobs, fresh clone physical files, replay rejection, idempotent backing, preflight protection and caught-failure rollback for the earlier revision. The current V2 revision must pass the commands in `DEVELOPMENT_INITIALIZATION.md` again before release.

## Self-observed runtime manual gate

The C# host now supports a pre-handoff initialization state where the three Package Manager repositories are physically present and compiled by Unity, while both `runtime.bindings.json` and `bootstrap.plan.json` are absent. This path is not exercised by the PowerShell offline workflow.

Manual Unity validation must confirm:

- Project Settings reports **Self-observed runtime** instead of missing bindings;
- **Include Package Manager Runtime** discovers exactly the three current runtime roots as an explicit Create selection;
- Use Existing maps those roots only when all three are already declared by PackageSet authority and their repository, Project gitlink and exact HEAD match;
- malformed durable bindings still fail instead of falling back;
- the copied offline helper is resolved from the self-observed Unity3D repository.

## Not verified here

The actual Unity 6000.3.20f1 Editor import/compile/reopen has not been proven by the historical CI evidence above. The V2 adapter can be compiled against the matching Module/Framework worktrees and installed Unity reference assemblies, but production readiness still requires the real-Project Prepare invariants, offline Apply, reopen/Console and fresh-clone gates in DEVELOPMENT_INITIALIZATION.md.

GitHub offline tests exercise the actual PowerShell apply runner against disposable repositories, not the C# preparation UI. They do not prove behavior under power loss, multi-process source editing or all user Project asset arrangements. Keep the PR as draft until real Unity verification completes.

## Additional source-capture guards

Portable repository records reject embedded credentials, query tokens and machine-local origin paths. Projects with a local-path origin must set an explicit portable HTTPS/SSH origin before declaring it as a distribution target. During LocalDraft extraction, ignored non-metadata inherited from the parent Project blocks the operation before copying/committing source; ignored secrets/build output must not accidentally enter the new repository when the parent's .gitignore no longer applies.
