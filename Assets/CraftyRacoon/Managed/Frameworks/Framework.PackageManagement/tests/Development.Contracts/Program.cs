using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;
using CraftyRacoon.PackageManagement.Framework.Development;

internal static class Program
{
    private const string ExistingRevision = "1111111111111111111111111111111111111111";
    private const string CreatedRevision = "2222222222222222222222222222222222222222";
    private const string SourceRevision = "3333333333333333333333333333333333333333";
    private const string ProjectHead = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProjectIndex = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string PackageSetRepository = "https://github.com/example/package-set.git";
    private const string SourceRepository = "https://github.com/example/source.git";
    private const string ProjectBindingFingerprint = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string RuntimeBindingFingerprint = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private static int checks;

    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
    }

    private static void ExpectUseExistingFailure(FakeUseExistingEnvironment environment, string expectedMessage, string name)
    {
        try
        {
            PrepareUseExisting(environment);
            throw new Exception(name + ": workflow unexpectedly succeeded");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains(expectedMessage, StringComparison.Ordinal), name);
        }
    }

    private static UseExistingPackageSetPreparation PrepareUseExisting(FakeUseExistingEnvironment environment)
    {
        UseExistingPackageSetRequest request = UseRequest();
        return PrepareUseExisting(environment, request);
    }

    private static UseExistingPackageSetPreparation PrepareUseExisting(FakeUseExistingEnvironment environment, UseExistingPackageSetRequest request)
    {
        IProjectPackageSetConverter converter = new ProjectPackageSetConverter(environment);
        return converter.Prepare(request, CancellationToken.None);
    }

    public static void Main()
    {
        TestPublicSurface();
        TestProjectObservationContract();
        TestAcquisitionContract();
        TestResolveTrackingRefRejectsAlreadyPinnedProject();
        TestCanonicalDirectoryPolicy();
        TestCreatePipelineAndDeterministicPlan();
        TestCreateCollectsConflictsBeforePrepare();
        TestCreateRejectsPartialRuntime();
        TestCreateRejectsPreparedRepositoryMismatch();
        TestCreateDetectsProjectMutationDuringPrepare();
        TestCreateRecordsSourceRepository();
        TestCreateRejectsMissingSourceRepository();
        TestCreateRejectsSymbolicSourceRevision();
        TestCreateAllowsExplicitLocalDraft();
        TestCreateAuditsFailedPreparation();
        TestUseExistingPipelineAndDeterministicPlan();
        TestUseExistingCollectsConflictsBeforeExport();
        TestUseExistingRejectsNonAuthoritativePin();
        TestUseExistingRejectsNonAuthoritativeRepository();
        TestUseExistingRejectsLocalDraft();
        TestUseExistingRejectsIndexMisalignment();
        TestUseExistingRejectsResolvedRepositoryMismatch();
        TestUseExistingDetectsProjectMutationDuringPrepare();
        TestUseExistingAuditsFailedExport();
        TestUseExistingRejectsPartialRuntime();
        TestUseExistingAcceptsCompleteRuntime();
        TestUseExistingUsesAuthoritativeProjectionRoot();
        TestAlreadyConfiguredIsReadOnly();
        TestProjectBindingTrackingDriftExportsRewrite();
        TestAbsentRuntimeBindingsExportCreate();
        TestRuntimeBindingValueDriftExportsRewrite();
        TestZeroRuntimeRejectsStaleBindings();
        TestRuntimeBindingStructuralConflicts();
        TestUntrackedExactBindingsAreRejected();
        TestBindingFingerprintsReachExportPlan();
        TestAlreadyConfiguredDetectsBindingDriftDuringFinalAudit();
        TestAlreadyConfiguredDetectsSourceDriftDuringFinalAudit();
        TestAlreadyConfiguredDetectsLayoutDriftDuringFinalAudit();
        TestAlreadyConfiguredDetectsEntryAuthorityDriftDuringFinalAudit();
        TestAlreadyConfiguredDetectsSourceRepositoryDriftDuringFinalAudit();
        TestInvalidProjectBindingIsRejected();
        TestProjectBindingIndexMismatchIsRejected();
        TestRuntimeBindingIndexMismatchIsRejected();
        TestUntrackedRuntimeBindingIsRejected();
        TestValidationStopsBeforeAcquisition();
        Console.WriteLine("PASS: " + checks + " Package Management V2 workflow contracts");
    }

    private static void TestPublicSurface()
    {
        Assembly assembly = typeof(PackageSetCreator).Assembly;
        string prefix = "CraftyRacoon.PackageManagement.Framework.Development.";
        Check(assembly.GetType(prefix + "DevelopmentInitializationMode") == null, "old mode enum removed");
        Check(assembly.GetType(prefix + "DevelopmentInitializationRequest") == null, "old request removed");
        Check(assembly.GetType(prefix + "ProjectDevelopmentInitializer") == null, "old initializer removed");

        Type requestType = typeof(UseExistingPackageSetRequest);
        Check(requestType.GetProperty("Selections") == null, "Use Existing has no selections");
        Check(requestType.GetProperty("Placements") == null, "Use Existing has no placements");
        Check(requestType.GetProperty("Adoptions") == null, "Use Existing has no adoptions");
        Check(requestType.GetProperty("SourceRoot") == null, "Use Existing has no source-layout input");
        Check(requestType.GetProperty("ProjectionRoot") == null, "Use Existing has no projection-layout input");

        MethodInfo[] creatorMethods = typeof(IPackageSetCreator).GetMethods();
        MethodInfo[] converterMethods = typeof(IProjectPackageSetConverter).GetMethods();
        Check(creatorMethods.Length == 1 && creatorMethods[0].Name == "Prepare", "Create public use case exposes only Prepare");
        Check(converterMethods.Length == 1 && converterMethods[0].Name == "Prepare", "Use Existing public use case exposes only Prepare");

        Type existingPort = typeof(IUseExistingPackageSetEnvironment);
        Check(existingPort.GetMethod("CreatePackageSet") == null, "Existing port cannot create PackageSet");
        Check(existingPort.GetMethod("PrepareSource") == null, "Existing port cannot prepare PackageSet sources");
        Check(existingPort.GetMethod("FinalizePackageSet") == null, "Existing port cannot finalize PackageSet");
        Check(existingPort.GetMethod("PreparePackageSet") == null, "Existing port cannot mutate PackageSet during prepare");
    }

    private static void TestProjectObservationContract()
    {
        try
        {
            new ProjectWorkflowObservation("main", ProjectIndex, "evidence");
            throw new Exception("Project observation accepted a symbolic HEAD");
        }
        catch (ArgumentException)
        {
            Check(true, "Project observation rejects symbolic HEAD");
        }

        try
        {
            new ProjectWorkflowObservation(new string('0', 40), ProjectIndex, "evidence");
            throw new Exception("Project observation accepted the null object id");
        }
        catch (ArgumentException)
        {
            Check(true, "Project observation rejects null object id");
        }

        try
        {
            new ProjectWorkflowObservation(ProjectHead, ProjectIndex, string.Empty);
            throw new Exception("Project observation accepted empty evidence");
        }
        catch (ArgumentException)
        {
            Check(true, "Project observation requires evidence token");
        }
    }

    private static void TestCanonicalDirectoryPolicy()
    {
        Check(PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", "Module.X") == "Sources/Modules/Module.X", "module source bucket");
        Check(PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", "Framework.X") == "Sources/Frameworks/Framework.X", "framework source bucket");
        Check(PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", "Workspace.X") == "Sources/Workspaces/Workspace.X", "workspace source bucket");
        Check(PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", "Framework.X") == "Assets/Managed/Frameworks/Framework.X", "framework projection bucket");
        Check(PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", "Module.X") == "Assets/Managed/Modules/Module.X", "module projection bucket");
        Check(PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", "Workspace.X") == "Assets/Managed/Workspaces/Workspace.X", "workspace projection bucket");
    }

    private static void TestAcquisitionContract()
    {
        UseExistingPackageSetRequest unresolved = new UseExistingPackageSetRequest("project", "checkout", "https://github.com/example/package-set.git", "main", string.Empty, PackageSetAcquisitionStrategy.ResolveTrackingRefOnce);
        Check(unresolved.ExactRevision == string.Empty, "tracking ref can be resolved once before an exact revision exists");
        try
        {
            new UseExistingPackageSetRequest("project", "checkout", "https://github.com/example/package-set.git", "main", "main", PackageSetAcquisitionStrategy.CloneOrOpenExactRevision);
            throw new Exception("symbolic revision accepted as exact authority");
        }
        catch (ArgumentException)
        {
            Check(true, "exact acquisition rejects symbolic revisions");
        }
    }

    private static void TestResolveTrackingRefRejectsAlreadyPinnedProject()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        UseExistingPackageSetRequest request = new UseExistingPackageSetRequest("project", "checkout", PackageSetRepository, "main", string.Empty, PackageSetAcquisitionStrategy.ResolveTrackingRefOnce);
        try
        {
            PrepareUseExisting(environment, request);
            throw new Exception("ResolveTrackingRefOnce accepted a Project that already has an exact revision");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("already has an exact PackageSet revision", StringComparison.Ordinal), "tracking ref resolution is rejected after the Project has an exact revision");
            Check(!environment.Calls.Contains("export"), "repeated tracking ref resolution cannot export a Project rewrite");
        }
    }

    private static void TestCreatePipelineAndDeterministicPlan()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        CreatePackageSetRequest request = CreateRequest(new CreatePackageSetSelection[]
        {
            Selection("Module.B", "Assets/Original/Module.B"),
            Selection("Framework.A", "Assets/Original/Framework.A")
        });
        IPackageSetCreator creator = new PackageSetCreator(environment);
        CreatePackageSetPreparation preparation = creator.Prepare(request, CancellationToken.None);

        string calls = string.Join(",", environment.Calls.ToArray());
        Check(calls == "observe,validate,acquire,observe:Framework.A,observe:Module.B,prepare,export,observe,ready", "Create follows Observe Validate Acquire Plan Prepare invariant-check and readiness order");
        Check(preparation.Plan.Entries[0].Placement.Identity == "Framework.A", "Create plan is sorted by identity");
        Check(preparation.Plan.Entries[1].Placement.Identity == "Module.B", "Create plan ordering is deterministic");
        Check(preparation.Plan.ProjectObservation.ProjectHead == ProjectHead, "Create plan preserves immutable Project baseline evidence");
        Check(preparation.Resolution.ExactRevision == CreatedRevision, "Create returns committed exact revision");
        Check(preparation.ApplyInstructionsPath == "create-offline-plan.json", "Create exports deferred offline apply");
        Check(environment.PlanWasSorted, "Create environment receives the final sorted plan before prepare");
    }

    private static void TestCreateCollectsConflictsBeforePrepare()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.EntryObservations["Framework.A"] = Observation(DevelopmentProjectionState.PhysicalSnapshot, false, true);
        environment.EntryObservations["Module.B"] = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, false);
        CreatePackageSetRequest request = CreateRequest(new CreatePackageSetSelection[]
        {
            Selection("Module.B", "Assets/Original/Module.B"),
            Selection("Framework.A", "Assets/Original/Framework.A")
        });

        try
        {
            new PackageSetCreator(environment).Prepare(request, CancellationToken.None);
            throw new Exception("Create conflicts were ignored");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Conflicts.Count == 2, "Create reports every entry conflict");
            Check(!environment.Calls.Contains("prepare"), "Create conflict suppresses PackageSet preparation");
            Check(!environment.Calls.Contains("export"), "Create conflict suppresses offline export");
        }
    }

    private static void TestCreateRejectsPartialRuntime()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        CreatePackageSetSelection runtime = new CreatePackageSetSelection("Module.PackageManagement", "Assets/PackageManagement/Module.PackageManagement", DevelopmentSourceKind.GitSubmodule, true);
        CreatePackageSetRequest request = CreateRequest(new CreatePackageSetSelection[] { runtime });
        try
        {
            new PackageSetCreator(environment).Prepare(request, CancellationToken.None);
            throw new Exception("Create accepted partial runtime");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("all three", StringComparison.Ordinal), "Create diagnoses partial runtime");
            Check(!environment.Calls.Contains("prepare"), "partial Create runtime is rejected before mutation");
        }
    }

    private static void TestCreateRejectsPreparedRepositoryMismatch()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.PreparedRepository = "https://github.com/example/wrong-package-set.git";
        try
        {
            new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
            throw new Exception("Create accepted a prepared checkout from another repository");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("repository", StringComparison.OrdinalIgnoreCase), "Create diagnoses prepared repository mismatch");
            Check(!environment.Calls.Contains("export"), "Create repository mismatch suppresses offline export");
        }
    }

    private static void TestCreateDetectsProjectMutationDuringPrepare()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.PostPrepareObservation = ChangedProjectObservation();
        try
        {
            new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
            throw new Exception("Create ignored a Project mutation during preparation");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("changed during preparation", StringComparison.Ordinal), "Create detects Project mutation during preparation");
            Check(environment.Calls.Contains("export"), "Create retains exported operation evidence when post-check fails");
        }
    }

    private static void TestCreateRecordsSourceRepository()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        CreatePackageSetPreparation preparation = new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
        Check(preparation.Plan.Entries[0].SourceRepository == SourceRepository, "Create plan records observed source repository");
        Check(preparation.Plan.Entries[0].ObservedRepository == SourceRepository, "Create plan exposes observed source repository evidence");
    }

    private static void TestCreateRejectsMissingSourceRepository()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.EntryRepositories["Module.A"] = string.Empty;
        try
        {
            new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
            throw new Exception("Create accepted a GitSubmodule without repository identity");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("repository identity is unavailable", StringComparison.Ordinal), "Create diagnoses missing source repository identity");
            Check(!environment.Calls.Contains("prepare"), "missing source repository suppresses PackageSet preparation");
        }
    }

    private static void TestCreateRejectsSymbolicSourceRevision()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.EntryObservations["Module.A"] = new DevelopmentSourceObservation(true, "main", "main", false, true, false, true, true, true, DevelopmentProjectionState.PhysicalSnapshot, "content-evidence");
        try
        {
            new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
            throw new Exception("Create accepted a symbolic source revision");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Conflicts.Count == 2, "Create reports symbolic HEAD and pin authority conflicts");
            Check(!environment.Calls.Contains("prepare"), "symbolic source revision suppresses PackageSet preparation");
        }
    }

    private static void TestCreateAllowsExplicitLocalDraft()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.EntryRepositories["Module.Local"] = string.Empty;
        CreatePackageSetSelection localDraft = new CreatePackageSetSelection("Module.Local", "Assets/Original/Module.Local", DevelopmentSourceKind.LocalDraft);
        CreatePackageSetPreparation preparation = new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { localDraft }), CancellationToken.None);
        Check(preparation.Plan.NeedsPublish, "Create marks explicit LocalDraft sources as NeedsPublish");
        Check(preparation.Plan.Entries[0].Assessment.NeedsPublish, "LocalDraft entry carries explicit publication requirement");
        Check(preparation.Plan.Entries[0].SourceRepository == string.Empty, "Create keeps unpublished LocalDraft repository identity empty");
    }

    private static void TestCreateAuditsFailedPreparation()
    {
        FakeCreateEnvironment environment = new FakeCreateEnvironment();
        environment.MutateAndFailDuringPrepare = true;
        try
        {
            new PackageSetCreator(environment).Prepare(CreateRequest(new CreatePackageSetSelection[] { Selection("Module.A", "Assets/Original/Module.A") }), CancellationToken.None);
            throw new Exception("Create failed without auditing the Project invariant");
        }
        catch (AggregateException error)
        {
            Check(error.InnerExceptions.Count == 2, "Create retains workflow and invariant failures");
            Check(environment.Calls[environment.Calls.Count - 1] == "observe", "Create re-observes Project after failed external preparation");
        }
    }

    private static void TestUseExistingPipelineAndDeterministicPlan()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        AuthoritativePackageSetEntry[] entries = new AuthoritativePackageSetEntry[]
        {
            Entry("Module.B"),
            Entry("Framework.A")
        };
        environment.Layout = Layout(entries);
        IProjectPackageSetConverter converter = new ProjectPackageSetConverter(environment);
        UseExistingPackageSetPreparation preparation = converter.Prepare(UseRequest(), CancellationToken.None);

        string calls = string.Join(",", environment.Calls.ToArray());
        Check(calls == "observe,validate,resolve,layout,observe:Framework.A,observe:Module.B,mode,export,layout,observe:Framework.A,observe:Module.B,mode,observe,ready", "Use Existing follows Observe Validate Resolve Plan Prepare final authority-audit and readiness order");
        Check(preparation.Plan.Entries[0].Placement.Identity == "Framework.A", "Existing plan is sorted by identity");
        Check(preparation.Plan.Entries[1].Placement.Identity == "Module.B", "Existing plan ordering is deterministic");
        Check(preparation.Plan.Entries[0].SourceRepository == SourceRepository, "Existing plan records authoritative source repository");
        Check(preparation.Plan.Entries[0].ObservedRepository == SourceRepository, "Existing plan records observed source repository evidence");
        Check(preparation.Plan.ProjectObservation.IndexFingerprint == ProjectIndex, "Existing plan preserves immutable Project baseline evidence");
        Check(preparation.Plan.Resolution.ExactRevision == ExistingRevision, "Existing plan records exact revision");
        Check(environment.PlanWasSorted, "Existing environment receives the final sorted plan only at export");
    }

    private static void TestUseExistingCollectsConflictsBeforeExport()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        AuthoritativePackageSetEntry[] entries = new AuthoritativePackageSetEntry[]
        {
            Entry("Module.B"),
            Entry("Framework.A")
        };
        environment.Layout = Layout(entries);
        environment.EntryObservations["Framework.A"] = Observation(DevelopmentProjectionState.PhysicalSnapshot, false, true);
        environment.EntryObservations["Module.B"] = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, false);

        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing conflicts were ignored");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Conflicts.Count == 2, "Use Existing reports every entry conflict");
            Check(!environment.Calls.Contains("export"), "Use Existing conflict suppresses offline export");
        }
    }

    private static void TestUseExistingRejectsNonAuthoritativePin()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        environment.EntryObservations["Module.A"] = ObservationWithPin(DevelopmentProjectionState.PhysicalSnapshot, true, true, "4444444444444444444444444444444444444444");
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted a non-authoritative source pin");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("authoritative PackageSet gitlink", StringComparison.Ordinal), "Use Existing diagnoses non-authoritative source pin");
            Check(!environment.Calls.Contains("export"), "source pin mismatch suppresses offline export");
        }
    }

    private static void TestUseExistingRejectsNonAuthoritativeRepository()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        environment.EntryRepositories["Module.A"] = "https://github.com/example/wrong-source.git";
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted a source checkout from another repository");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("authoritative PackageSet submodule repository", StringComparison.Ordinal), "Use Existing diagnoses non-authoritative source repository");
            Check(!environment.Calls.Contains("export"), "source repository mismatch suppresses offline export");
        }
    }

    private static void TestUseExistingRejectsLocalDraft()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        string sourcePath = PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", "Module.Local");
        string projectionPath = PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", "Module.Local");
        AuthoritativePackageSetEntry localDraft = new AuthoritativePackageSetEntry("Module.Local", sourcePath, projectionPath, SourceRepository, SourceRevision, DevelopmentSourceKind.LocalDraft);
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { localDraft });
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted an unpublished LocalDraft entry");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("LocalDraft entries must be published", StringComparison.Ordinal), "Use Existing rejects unpublished LocalDraft authority");
            Check(!environment.Calls.Contains("export"), "LocalDraft authority suppresses offline export");
        }
    }

    private static void TestUseExistingRejectsIndexMisalignment()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        environment.EntryObservations["Module.A"] = new DevelopmentSourceObservation(true, SourceRevision, SourceRevision, false, true, false, true, true, false, DevelopmentProjectionState.PhysicalSnapshot, "unaligned-index-evidence");
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted a source whose Project index is not aligned");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("Git index", StringComparison.Ordinal), "Use Existing diagnoses Project index misalignment");
            Check(!environment.Calls.Contains("export"), "Project index misalignment suppresses offline export");
        }
    }

    private static void TestUseExistingRejectsResolvedRepositoryMismatch()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.ResolvedRepository = "https://github.com/example/Package-Set.git";
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted a checkout from another repository");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("repository identity", StringComparison.Ordinal), "Use Existing diagnoses resolved repository mismatch");
            Check(!environment.Calls.Contains("layout"), "repository mismatch stops before authoritative layout read");
        }
    }

    private static void TestUseExistingDetectsProjectMutationDuringPrepare()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.PostPrepareObservation = ChangedProjectEvidenceObservation();
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing ignored a Project mutation during preparation");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("changed during preparation", StringComparison.Ordinal), "Use Existing detects Project mutation during preparation");
            Check(environment.Calls.Contains("export"), "Use Existing retains exported operation evidence when post-check fails");
            Check(!environment.Calls.Contains("ready"), "failed final Existing audit never marks the offline plan ready");
        }
    }

    private static void TestUseExistingAuditsFailedExport()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.MutateAndFailDuringExport = true;
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing failed without auditing the Project invariant");
        }
        catch (AggregateException error)
        {
            Check(error.InnerExceptions.Count == 2, "Use Existing retains export and invariant failures");
            Check(environment.Calls[environment.Calls.Count - 1] == "observe", "Use Existing re-observes Project after failed export");
        }
    }

    private static void TestUseExistingRejectsPartialRuntime()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        AuthoritativePackageSetEntry runtime = new AuthoritativePackageSetEntry("Module.PackageManagement", "Sources/Modules/Module.PackageManagement", "Assets/Managed/Modules/Module.PackageManagement", SourceRepository, SourceRevision, DevelopmentSourceKind.GitSubmodule, true);
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { runtime });
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted partial runtime");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("all three", StringComparison.Ordinal), "Use Existing diagnoses partial runtime");
            Check(!environment.Calls.Contains("export"), "partial Existing runtime is rejected before export");
        }
    }

    private static void TestUseExistingAcceptsCompleteRuntime()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        AuthoritativePackageSetEntry[] entries = new AuthoritativePackageSetEntry[]
        {
            RuntimeEntry("Framework.PackageManagement"),
            RuntimeEntry("Framework.PackageManagement.Unity3D"),
            RuntimeEntry("Module.PackageManagement")
        };
        environment.Layout = Layout(entries);
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.Entries.Count == 3, "complete runtime composition is accepted");
        Check(environment.Calls.Contains("export"), "complete runtime composition reaches offline export");
    }

    private static void TestUseExistingUsesAuthoritativeProjectionRoot()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        string projectionRoot = "Assets/PackageSetOwned";
        AuthoritativePackageSetEntry entry = new AuthoritativePackageSetEntry("Module.A", "Sources/Modules/Module.A", projectionRoot + "/Modules/Module.A", SourceRepository, SourceRevision, DevelopmentSourceKind.GitSubmodule);
        environment.Layout = new AuthoritativePackageSetLayout("Sources", projectionRoot, new AuthoritativePackageSetEntry[] { entry });
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.Layout.ProjectionRoot == projectionRoot, "Use Existing takes the managed projection root from PackageSet authority");
        Check(preparation.Plan.ProjectBindingRequiresWrite, "a different authoritative projection root is written only by offline Apply");
    }

    private static void TestAlreadyConfiguredIsReadOnly()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.Layout = Layout(new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.AlreadyConfigured, "correct Existing topology reports AlreadyConfigured");
        Check(preparation.ApplyInstructionsPath == string.Empty, "AlreadyConfigured has no apply plan");
        Check(!environment.Calls.Contains("export"), "AlreadyConfigured is a read-only no-op");
    }

    private static void TestProjectBindingTrackingDriftExportsRewrite()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        ObservedProjectPackageSetBinding projectBinding = ProjectBinding("release", true, true);
        environment.ModeObservation = new PackageSetModeObservation(projectBinding, AbsentRuntimeBindings());
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.ProjectBindingRequiresWrite, "trackingRef-only drift requires a Project binding rewrite");
        Check(!preparation.Plan.AlreadyConfigured, "trackingRef-only drift is not already configured");
        Check(environment.Calls.Contains("export"), "trackingRef-only drift exports an offline rewrite");
    }

    private static void TestAbsentRuntimeBindingsExportCreate()
    {
        FakeUseExistingEnvironment environment = RuntimeEnvironment();
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), AbsentRuntimeBindings());
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.RuntimeBindingsRequireWrite, "absent runtime bindings require creation");
        Check(environment.Calls.Contains("export"), "absent runtime bindings export an offline create");
    }

    private static void TestRuntimeBindingValueDriftExportsRewrite()
    {
        FakeUseExistingEnvironment environment = RuntimeEnvironment();
        List<ObservedRuntimeBindingEntry> entries = RuntimeBindingEntries(environment.Layout);
        ObservedRuntimeBindingEntry first = entries[0];
        entries[0] = new ObservedRuntimeBindingEntry(first.Identity, "https://github.com/example/stale.git", first.ProjectionPath);
        ObservedRuntimeBindings runtimeBindings = RuntimeBindings(1, entries, true, true);
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), runtimeBindings);
        UseExistingPackageSetPreparation preparation = PrepareUseExisting(environment);
        Check(preparation.Plan.RuntimeBindingsRequireWrite, "valid runtime binding value drift requires a rewrite");
        Check(environment.Calls.Contains("export"), "valid runtime binding value drift exports an offline rewrite");
    }

    private static void TestZeroRuntimeRejectsStaleBindings()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        ObservedRuntimeBindings staleBindings = RuntimeBindings(1, new ObservedRuntimeBindingEntry[0], true, true);
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), staleBindings);
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("zero-runtime PackageSet accepted a stale runtime binding file");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("contains no Package Manager runtime", StringComparison.Ordinal), "zero-runtime PackageSet rejects any runtime binding file");
            Check(!environment.Calls.Contains("export"), "stale zero-runtime bindings suppress export");
        }
    }

    private static void TestRuntimeBindingStructuralConflicts()
    {
        FakeUseExistingEnvironment baseline = RuntimeEnvironment();
        List<ObservedRuntimeBindingEntry> exactEntries = RuntimeBindingEntries(baseline.Layout);
        RejectRuntimeBindings(RuntimeBindings(2, exactEntries, true, true), "schemaVersion", "wrong runtime binding schema is rejected");

        List<ObservedRuntimeBindingEntry> partialEntries = new List<ObservedRuntimeBindingEntry>();
        partialEntries.Add(exactEntries[0]);
        partialEntries.Add(exactEntries[1]);
        RejectRuntimeBindings(RuntimeBindings(1, partialEntries, true, true), "exactly the three", "partial runtime bindings are rejected");

        List<ObservedRuntimeBindingEntry> duplicateEntries = new List<ObservedRuntimeBindingEntry>();
        duplicateEntries.Add(exactEntries[0]);
        duplicateEntries.Add(exactEntries[0]);
        duplicateEntries.Add(exactEntries[2]);
        RejectRuntimeBindings(RuntimeBindings(1, duplicateEntries, true, true), "duplicate identity", "duplicate runtime bindings are rejected");

        List<ObservedRuntimeBindingEntry> extraEntries = new List<ObservedRuntimeBindingEntry>(exactEntries);
        extraEntries.Add(new ObservedRuntimeBindingEntry("Unknown.Runtime", SourceRepository, "Assets/Managed/Unknown.Runtime"));
        RejectRuntimeBindings(RuntimeBindings(1, extraEntries, true, true), "unexpected identity", "extra runtime bindings are rejected");

        string[] diagnostics = new string[] { "malformed JSON" };
        ObservedRuntimeBindings malformed = new ObservedRuntimeBindings(DurableBindingObservationState.Invalid, 0, new ObservedRuntimeBindingEntry[0], RuntimeBindingFingerprint, true, false, diagnostics);
        RejectRuntimeBindings(malformed, "malformed JSON", "malformed runtime bindings are rejected");
    }

    private static void TestUntrackedExactBindingsAreRejected()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", false, true), AbsentRuntimeBindings());
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("untracked exact-looking Project binding was accepted");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("tracked by the Project Git index", StringComparison.Ordinal), "untracked exact-looking binding is not a no-op");
            Check(!environment.Calls.Contains("export"), "untracked binding suppresses export");
        }
    }

    private static void TestBindingFingerprintsReachExportPlan()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
            PrepareUseExisting(environment);
        Check(environment.ExportedPlan != null, "Use Existing exports a plan when an entry requires Apply");
        Check(environment.ExportedPlan != null && environment.ExportedPlan.PackageSetModeObservation.ProjectBinding.ContentFingerprint == ProjectBindingFingerprint, "Project binding fingerprint reaches the immutable export plan");
        Check(environment.ExportedPlan != null && environment.ExportedPlan.PackageSetModeObservation.RuntimeBindings.ContentFingerprint == DurableBindingFingerprintForTest(), "runtime absence fingerprint reaches the immutable export plan");
    }

    private static void TestAlreadyConfiguredDetectsBindingDriftDuringFinalAudit()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        ObservedProjectPackageSetBinding changedBinding = new ObservedProjectPackageSetBinding(DurableBindingObservationState.Valid, 3, PackageSetRepository, "release", ExistingRevision, "Assets/Managed", false, ProjectBindingFingerprint, true, true, new string[0]);
        environment.PostPlanModeObservation = new PackageSetModeObservation(changedBinding, AbsentRuntimeBindings());
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("AlreadyConfigured ignored binding drift during the final authority audit");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("binding changed after planning", StringComparison.Ordinal), "AlreadyConfigured detects binding drift during final authority audit");
            Check(!environment.Calls.Contains("export"), "binding drift is detected without exporting a no-op plan");
        }
    }

    private static void TestAlreadyConfiguredDetectsSourceDriftDuringFinalAudit()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        environment.PostPlanEntryObservations["Module.A"] = new DevelopmentSourceObservation(true, SourceRevision, SourceRevision, false, true, false, true, true, false, DevelopmentProjectionState.CorrectJunction, "changed-source-evidence");
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("AlreadyConfigured ignored source evidence drift during final audit");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("source or Project entry evidence changed", StringComparison.Ordinal), "AlreadyConfigured detects source/index drift during final audit");
            Check(!environment.Calls.Contains("ready"), "source drift never marks an offline plan ready");
        }
    }

    private static void TestAlreadyConfiguredDetectsLayoutDriftDuringFinalAudit()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        environment.PostPlanLayout = new AuthoritativePackageSetLayout("Sources", "Assets/OtherManaged", new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        ExpectUseExistingFailure(environment, "layout authority changed", "AlreadyConfigured detects PackageSet layout drift during final audit");
        Check(!environment.Calls.Contains("ready"), "layout drift never marks an offline plan ready");
    }

    private static void TestAlreadyConfiguredDetectsEntryAuthorityDriftDuringFinalAudit()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        AuthoritativePackageSetEntry changed = new AuthoritativePackageSetEntry("Module.A", "Sources/Module.A", "Assets/Managed/Module.A", "https://example.invalid/changed.git", SourceRevision, DevelopmentSourceKind.GitSubmodule);
        environment.PostPlanLayout = Layout(new AuthoritativePackageSetEntry[] { changed });
        ExpectUseExistingFailure(environment, "entry authority changed", "AlreadyConfigured detects PackageSet entry authority drift during final audit");
        Check(!environment.Calls.Contains("ready"), "entry authority drift never marks an offline plan ready");
    }

    private static void TestAlreadyConfiguredDetectsSourceRepositoryDriftDuringFinalAudit()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.EntryObservations["Module.A"] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        environment.PostPlanEntryRepositories["Module.A"] = "https://example.invalid/changed.git";
        ExpectUseExistingFailure(environment, "source or Project entry evidence changed", "AlreadyConfigured detects observed source repository drift during final audit");
        Check(!environment.Calls.Contains("ready"), "source repository drift never marks an offline plan ready");
    }

    private static void TestInvalidProjectBindingIsRejected()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        string[] diagnostics = new string[] { "schemaVersion must be 3" };
        ObservedProjectPackageSetBinding invalid = new ObservedProjectPackageSetBinding(DurableBindingObservationState.Invalid, 2, PackageSetRepository, "main", ExistingRevision, "Assets/Managed", false, ProjectBindingFingerprint, true, true, diagnostics);
        environment.ModeObservation = new PackageSetModeObservation(invalid, AbsentRuntimeBindings());
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted an invalid Project binding");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("schemaVersion must be 3", StringComparison.Ordinal), "invalid Project binding diagnostics are surfaced");
            Check(!environment.Calls.Contains("export"), "invalid Project binding suppresses export");
        }
    }

    private static void TestRuntimeBindingIndexMismatchIsRejected()
    {
        FakeUseExistingEnvironment environment = RuntimeEnvironment();
        List<ObservedRuntimeBindingEntry> entries = RuntimeBindingEntries(environment.Layout);
        ObservedRuntimeBindings bindings = RuntimeBindings(1, entries, false, false);
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), bindings);
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("Use Existing accepted untracked runtime bindings");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains("tracked by the Project Git index", StringComparison.Ordinal), "untracked runtime binding is rejected");
            Check(error.Message.Contains("does not match the Project Git index", StringComparison.Ordinal), "runtime worktree/index mismatch is rejected");
        }
    }

    private static void TestProjectBindingIndexMismatchIsRejected()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        ObservedProjectPackageSetBinding mismatch = new ObservedProjectPackageSetBinding(DurableBindingObservationState.Valid, 3, PackageSetRepository, "main", ExistingRevision, "Assets/Managed", false, ProjectBindingFingerprint, true, false, new string[0]);
        environment.ModeObservation = new PackageSetModeObservation(mismatch, AbsentRuntimeBindings());
        ExpectUseExistingFailure(environment, "worktree content does not match", "Project binding index mismatch is rejected");
    }

    private static void TestUntrackedRuntimeBindingIsRejected()
    {
        FakeUseExistingEnvironment environment = RuntimeEnvironment();
        foreach (AuthoritativePackageSetEntry entry in environment.Layout.Entries) environment.EntryObservations[entry.Identity] = Observation(DevelopmentProjectionState.CorrectJunction, true, true);
        ObservedRuntimeBindings untracked = new ObservedRuntimeBindings(DurableBindingObservationState.Valid, 1, RuntimeBindingEntries(environment.Layout), RuntimeBindingFingerprint, false, true, new string[0]);
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), untracked);
        ExpectUseExistingFailure(environment, "must be tracked", "untracked runtime binding is rejected");
    }

    private static void RejectRuntimeBindings(ObservedRuntimeBindings runtimeBindings, string expectedMessage, string name)
    {
        FakeUseExistingEnvironment environment = RuntimeEnvironment();
        environment.ModeObservation = new PackageSetModeObservation(ProjectBinding("main", true, true), runtimeBindings);
        try
        {
            PrepareUseExisting(environment);
            throw new Exception(name + " (accepted)");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Message.Contains(expectedMessage, StringComparison.Ordinal), name);
            Check(!environment.Calls.Contains("export"), name + " before export");
        }
    }

    private static void TestValidationStopsBeforeAcquisition()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        environment.ValidationConflicts.Add("index is not clean");
        try
        {
            PrepareUseExisting(environment);
            throw new Exception("validation conflict was ignored");
        }
        catch (PackageSetWorkflowConflictException error)
        {
            Check(error.Conflicts.Count == 1, "validation conflicts are surfaced");
            Check(!environment.Calls.Contains("resolve"), "validation stops before acquisition");
        }
    }

    private static CreatePackageSetRequest CreateRequest(IEnumerable<CreatePackageSetSelection> selections)
    {
        return new CreatePackageSetRequest("project", "new-package-set", "PackageSet.Test", PackageSetRepository, "main", "Sources", "Assets/Managed", selections);
    }

    private static UseExistingPackageSetRequest UseRequest()
    {
        return new UseExistingPackageSetRequest("project", "existing-package-set", PackageSetRepository, "main", ExistingRevision, PackageSetAcquisitionStrategy.CloneOrOpenExactRevision);
    }

    private static CreatePackageSetSelection Selection(string identity, string path)
    {
        return new CreatePackageSetSelection(identity, path, DevelopmentSourceKind.GitSubmodule);
    }

    private static AuthoritativePackageSetEntry Entry(string identity)
    {
        string sourcePath = PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", identity);
        string projectionPath = PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", identity);
        return new AuthoritativePackageSetEntry(identity, sourcePath, projectionPath, SourceRepository, SourceRevision, DevelopmentSourceKind.GitSubmodule);
    }

    private static AuthoritativePackageSetEntry RuntimeEntry(string identity)
    {
        string sourcePath = PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath("Sources", identity);
        string projectionPath = PackageSourceDirectoryPolicy.ResolveManagedProjectionPath("Assets/Managed", identity);
        return new AuthoritativePackageSetEntry(identity, sourcePath, projectionPath, SourceRepository, SourceRevision, DevelopmentSourceKind.GitSubmodule, true);
    }

    private static AuthoritativePackageSetLayout Layout(IEnumerable<AuthoritativePackageSetEntry> entries)
    {
        return new AuthoritativePackageSetLayout("Sources", "Assets/Managed", entries);
    }

    private static FakeUseExistingEnvironment RuntimeEnvironment()
    {
        FakeUseExistingEnvironment environment = new FakeUseExistingEnvironment();
        AuthoritativePackageSetEntry[] entries = new AuthoritativePackageSetEntry[]
        {
            RuntimeEntry("Framework.PackageManagement"),
            RuntimeEntry("Framework.PackageManagement.Unity3D"),
            RuntimeEntry("Module.PackageManagement")
        };
        environment.Layout = Layout(entries);
        return environment;
    }

    private static ObservedProjectPackageSetBinding ProjectBinding(string trackingRef, bool trackedInProjectIndex, bool indexMatchesWorktree)
    {
        return new ObservedProjectPackageSetBinding(DurableBindingObservationState.Valid, 3, PackageSetRepository, trackingRef, ExistingRevision, "Assets/Managed", false, ProjectBindingFingerprint, trackedInProjectIndex, indexMatchesWorktree, new string[0]);
    }

    private static ObservedRuntimeBindings AbsentRuntimeBindings()
    {
        return new ObservedRuntimeBindings(DurableBindingObservationState.Absent, 0, new ObservedRuntimeBindingEntry[0], DurableBindingFingerprintForTest(), false, true, new string[0]);
    }

    private static string DurableBindingFingerprintForTest()
    {
        return "missing";
    }

    private static ObservedRuntimeBindings RuntimeBindings(int schemaVersion, IEnumerable<ObservedRuntimeBindingEntry> entries, bool trackedInProjectIndex, bool indexMatchesWorktree)
    {
        return new ObservedRuntimeBindings(DurableBindingObservationState.Valid, schemaVersion, entries, RuntimeBindingFingerprint, trackedInProjectIndex, indexMatchesWorktree, new string[0]);
    }

    private static List<ObservedRuntimeBindingEntry> RuntimeBindingEntries(AuthoritativePackageSetLayout layout)
    {
        List<ObservedRuntimeBindingEntry> entries = new List<ObservedRuntimeBindingEntry>();
        foreach (AuthoritativePackageSetEntry entry in layout.Entries)
        {
            if (entry.BootstrapRuntime) entries.Add(new ObservedRuntimeBindingEntry(entry.Identity, entry.Repository, entry.ProjectionPath));
        }
        return entries;
    }

    private static PackageSetModeObservation ExactModeObservation(AuthoritativePackageSetLayout layout)
    {
        List<ObservedRuntimeBindingEntry> runtimeEntries = RuntimeBindingEntries(layout);
        ObservedRuntimeBindings runtimeBindings = runtimeEntries.Count == 0 ? AbsentRuntimeBindings() : RuntimeBindings(1, runtimeEntries, true, true);
        return new PackageSetModeObservation(ProjectBinding("main", true, true), runtimeBindings);
    }

    private static DevelopmentSourceObservation Observation(DevelopmentProjectionState state, bool metadataOwnershipVerified, bool pathOwnershipVerified)
    {
        return ObservationWithPin(state, metadataOwnershipVerified, pathOwnershipVerified, SourceRevision);
    }

    private static DevelopmentSourceObservation ObservationWithPin(DevelopmentProjectionState state, bool metadataOwnershipVerified, bool pathOwnershipVerified, string exactPin)
    {
        return new DevelopmentSourceObservation(true, exactPin, exactPin, false, true, false, metadataOwnershipVerified, pathOwnershipVerified, true, state, "content-evidence");
    }

    private static ProjectWorkflowObservation BaselineProjectObservation()
    {
        return new ProjectWorkflowObservation(ProjectHead, ProjectIndex, "project-evidence");
    }

    private static ProjectWorkflowObservation ChangedProjectObservation()
    {
        return new ProjectWorkflowObservation(ProjectHead, "cccccccccccccccccccccccccccccccccccccccc", "project-evidence");
    }

    private static ProjectWorkflowObservation ChangedProjectHeadObservation()
    {
        return new ProjectWorkflowObservation("cccccccccccccccccccccccccccccccccccccccc", ProjectIndex, "project-evidence");
    }

    private static ProjectWorkflowObservation ChangedProjectEvidenceObservation()
    {
        return new ProjectWorkflowObservation(ProjectHead, ProjectIndex, "changed-project-evidence");
    }

    private sealed class FakeCreateEnvironment : ICreatePackageSetEnvironment
    {
        internal readonly List<string> Calls = new List<string>();
        internal readonly List<string> ValidationConflicts = new List<string>();
        internal readonly Dictionary<string, DevelopmentSourceObservation> EntryObservations = new Dictionary<string, DevelopmentSourceObservation>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> EntryRepositories = new Dictionary<string, string>(StringComparer.Ordinal);
        internal bool PlanWasSorted;
        internal string PreparedRepository = PackageSetRepository;
        internal ProjectWorkflowObservation? PostPrepareObservation;
        internal bool MutateAndFailDuringPrepare;
        private int observationCount;

        public ProjectWorkflowObservation Observe(CreatePackageSetRequest request)
        {
            Calls.Add("observe");
            observationCount++;
            if (observationCount > 1 && PostPrepareObservation != null) return PostPrepareObservation;
            return BaselineProjectObservation();
        }

        public IReadOnlyList<string> Validate(CreatePackageSetRequest request, ProjectWorkflowObservation observation)
        {
            Calls.Add("validate");
            return ValidationConflicts.AsReadOnly();
        }

        public PackageSetWorkspace Acquire(CreatePackageSetRequest request, CancellationToken cancellation)
        {
            Calls.Add("acquire");
            return new PackageSetWorkspace(request.PackageSetRoot);
        }

        public ObservedCreatePackageSetSource ObserveSelection(CreatePackageSetRequest request, PackageSetWorkspace workspace, PackagePlacement placement)
        {
            Calls.Add("observe:" + placement.Identity);
            DevelopmentSourceObservation? observation;
            if (!EntryObservations.TryGetValue(placement.Identity, out observation) || observation == null) observation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, true);
            string sourceRepository = SourceRepository;
            string? configuredRepository;
            if (EntryRepositories.TryGetValue(placement.Identity, out configuredRepository) && configuredRepository != null) sourceRepository = configuredRepository;
            return new ObservedCreatePackageSetSource(sourceRepository, observation);
        }

        public PackageSetResolution PreparePackageSet(CreatePackageSetPlan plan, CancellationToken cancellation)
        {
            Calls.Add("prepare");
            if (MutateAndFailDuringPrepare)
            {
                PostPrepareObservation = ChangedProjectHeadObservation();
                throw new InvalidOperationException("simulated Create preparation failure");
            }
            PlanWasSorted = plan.Entries.Count == 2 && plan.Entries[0].Placement.Identity == "Framework.A" && plan.Entries[1].Placement.Identity == "Module.B";
            return new PackageSetResolution(plan.Workspace.CheckoutPath, PreparedRepository, CreatedRevision);
        }

        public string ExportOfflineApply(CreatePackageSetPlan plan, PackageSetResolution resolution)
        {
            Calls.Add("export");
            return "create-offline-plan.json";
        }

        public void MarkOfflineApplyReady(string applyInstructionsPath)
        {
            Calls.Add("ready");
        }
    }

    private sealed class FakeUseExistingEnvironment : IUseExistingPackageSetEnvironment
    {
        internal readonly List<string> Calls = new List<string>();
        internal readonly List<string> ValidationConflicts = new List<string>();
        internal readonly Dictionary<string, DevelopmentSourceObservation> EntryObservations = new Dictionary<string, DevelopmentSourceObservation>(StringComparer.Ordinal);
        internal readonly Dictionary<string, DevelopmentSourceObservation> PostPlanEntryObservations = new Dictionary<string, DevelopmentSourceObservation>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> PostPlanEntryRepositories = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> EntryRepositories = new Dictionary<string, string>(StringComparer.Ordinal);
        internal AuthoritativePackageSetLayout Layout = Program.Layout(new AuthoritativePackageSetEntry[] { Entry("Module.A") });
        internal AuthoritativePackageSetLayout? PostPlanLayout;
        internal bool PlanWasSorted;
        internal string ResolvedRepository = PackageSetRepository;
        internal ProjectWorkflowObservation? PostPrepareObservation;
        internal PackageSetModeObservation? ModeObservation;
        internal PackageSetModeObservation? PostPlanModeObservation;
        internal UseExistingPackageSetPlan? ExportedPlan;
        internal bool MutateAndFailDuringExport;
        private int observationCount;
        private int modeObservationCount;
        private int layoutReadCount;
        private readonly Dictionary<string, int> entryObservationCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public ProjectWorkflowObservation Observe(UseExistingPackageSetRequest request)
        {
            Calls.Add("observe");
            observationCount++;
            if (observationCount > 1 && PostPrepareObservation != null) return PostPrepareObservation;
            return BaselineProjectObservation();
        }

        public IReadOnlyList<string> Validate(UseExistingPackageSetRequest request, ProjectWorkflowObservation observation)
        {
            Calls.Add("validate");
            return ValidationConflicts.AsReadOnly();
        }

        public PackageSetResolution AcquireOrResolve(UseExistingPackageSetRequest request, CancellationToken cancellation)
        {
            Calls.Add("resolve");
            return new PackageSetResolution(request.PackageSetCheckoutPath, ResolvedRepository, ExistingRevision);
        }

        public AuthoritativePackageSetLayout ReadAuthoritativeLayout(UseExistingPackageSetRequest request, PackageSetResolution resolution)
        {
            Calls.Add("layout");
            layoutReadCount++;
            if (layoutReadCount > 1 && PostPlanLayout != null) return PostPlanLayout;
            return Layout;
        }

        public ObservedPackageSetEntry ObserveEntry(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetEntry entry)
        {
            Calls.Add("observe:" + entry.Identity);
            int entryObservationCount;
            if (!entryObservationCounts.TryGetValue(entry.Identity, out entryObservationCount)) entryObservationCount = 0;
            entryObservationCount++;
            entryObservationCounts[entry.Identity] = entryObservationCount;
            DevelopmentSourceObservation? observation;
            if (entryObservationCount > 1 && PostPlanEntryObservations.TryGetValue(entry.Identity, out observation) && observation != null) return new ObservedPackageSetEntry("Assets/Original/" + entry.Identity, SourceRepository, observation);
            if (!EntryObservations.TryGetValue(entry.Identity, out observation) || observation == null) observation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, true);
            string sourceRepository = SourceRepository;
            string? configuredRepository;
            if (entryObservationCount > 1 && PostPlanEntryRepositories.TryGetValue(entry.Identity, out configuredRepository) && configuredRepository != null) sourceRepository = configuredRepository;
            if (EntryRepositories.TryGetValue(entry.Identity, out configuredRepository) && configuredRepository != null) sourceRepository = configuredRepository;
            return new ObservedPackageSetEntry("Assets/Original/" + entry.Identity, sourceRepository, observation);
        }

        public PackageSetModeObservation ObservePackageSetMode(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetLayout authoritativeLayout)
        {
            Calls.Add("mode");
            modeObservationCount++;
            if (modeObservationCount > 1 && PostPlanModeObservation != null) return PostPlanModeObservation;
            if (ModeObservation != null) return ModeObservation;
            return ExactModeObservation(authoritativeLayout);
        }

        public string ExportOfflineApply(UseExistingPackageSetPlan plan)
        {
            Calls.Add("export");
            ExportedPlan = plan;
            if (MutateAndFailDuringExport)
            {
                PostPrepareObservation = ChangedProjectHeadObservation();
                throw new InvalidOperationException("simulated Existing export failure");
            }
            PlanWasSorted = plan.Entries.Count == 2 && plan.Entries[0].Placement.Identity == "Framework.A" && plan.Entries[1].Placement.Identity == "Module.B";
            return "existing-offline-plan.json";
        }

        public void MarkOfflineApplyReady(string applyInstructionsPath)
        {
            Calls.Add("ready");
        }
    }
}
