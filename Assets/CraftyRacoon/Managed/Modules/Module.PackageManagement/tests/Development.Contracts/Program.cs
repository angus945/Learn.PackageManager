using System;
using CraftyRacoon.PackageManagement.Development;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string name)
    {
        checks += 1;
        if (!condition) throw new Exception(name);
    }

    private static void RejectNormalize(string path)
    {
        try
        {
            PackageRelativePath.Normalize(path);
        }
        catch (ArgumentException)
        {
            checks += 1;
            return;
        }

        throw new Exception("unsafe path accepted: " + path);
    }

    private static void RejectLayout(PackagePlacement[] placements, string name)
    {
        try
        {
            PackageDirectoryLayout layout = new PackageDirectoryLayout("Sources", "Assets/Managed", placements);
            GC.KeepAlive(layout);
        }
        catch (ArgumentException)
        {
            checks += 1;
            return;
        }

        throw new Exception(name);
    }

    private static PackagePlacement Placement(string id, string source, string original, string projection)
    {
        return new PackagePlacement(id, source, original, projection, DevelopmentSourceKind.GitSubmodule);
    }

    private static DevelopmentSourceObservation Observation(DevelopmentProjectionState projectionState, bool sourceAvailable, string sourceHead, string exactPin, bool sourceDirty, bool contentMatchesExactPin, bool projectHasIndependentChanges, bool metadataOwnershipVerified, bool pathOwnershipVerified, bool indexAlignmentVerified, string evidenceToken)
    {
        return new DevelopmentSourceObservation(sourceAvailable, sourceHead, exactPin, sourceDirty, contentMatchesExactPin, projectHasIndependentChanges, metadataOwnershipVerified, pathOwnershipVerified, indexAlignmentVerified, projectionState, evidenceToken);
    }

    private static DevelopmentSourceObservation AlignedObservation(DevelopmentProjectionState projectionState)
    {
        return Observation(projectionState, true, "a", "a", false, true, false, true, true, true, "content-evidence");
    }

    public static void Main()
    {
        string[] unsafePaths = new string[] { "", "../A", "A/../B", "/tmp/A", "C:/A", "A//B", "A/./B", "A/", "A/.git/B", "A/CON", "A/NUL.cs", "A/COM1.cs", "A/x.", "A/x ", "A/x:y", "A/x\ny" };
        foreach (string path in unsafePaths) RejectNormalize(path);

        Check(PackageRelativePath.Normalize(@"Sources\Module.X") == "Sources/Module.X", "slash normalization");
        Check(PackageRelativePath.Normalize("Sources/模組") == "Sources/模組", "Unicode paths");

        PackagePlacement placementA = Placement("Module.A", "Sources/A", "Assets/Old/A", "Assets/Managed/A");
        PackageDirectoryLayout validLayout = new PackageDirectoryLayout("Sources", "Assets/Managed", new PackagePlacement[] { placementA });
        Check(validLayout.Placements.Count == 1, "valid relocation");
        RejectLayout(new PackagePlacement[] { placementA, Placement("Module.A", "Sources/B", "Assets/Old/B", "Assets/Managed/B") }, "duplicate identity");
        RejectLayout(new PackagePlacement[] { placementA, Placement("Module.B", "Sources/a/B", "Assets/Old/B", "Assets/Managed/B") }, "overlapping sources");
        RejectLayout(new PackagePlacement[] { placementA, Placement("Module.B", "Sources/B", "Assets/Old/B", "Assets/Managed/a/B") }, "overlapping projections");
        RejectLayout(new PackagePlacement[] { placementA, Placement("Module.B", "Sources/B", "Assets/Managed/A", "Assets/Managed/B") }, "cross-entry move collision");
        RejectLayout(new PackagePlacement[] { Placement("Module.X", "Else/X", "Assets/X", "Assets/Managed/X") }, "source escape");
        RejectLayout(new PackagePlacement[] { Placement("Module.X", "Sources/X", "Assets/X", "Assets/Art/X") }, "projection escape");
        RejectLayout(new PackagePlacement[] { Placement("Module.X", "Sources/X", "Assets/Managed", "Assets/Managed/X") }, "move into own child");

        DevelopmentSourceKind gitSubmodule = DevelopmentSourceKind.GitSubmodule;
        DevelopmentBackingAssessment absent = DevelopmentBackingPolicy.Evaluate(gitSubmodule, AlignedObservation(DevelopmentProjectionState.Absent));
        Check(absent.Decision == DevelopmentBackingDecision.ReadyForOfflineApply, "absent projection can be materialized");
        DevelopmentBackingAssessment physicalSnapshot = DevelopmentBackingPolicy.Evaluate(gitSubmodule, AlignedObservation(DevelopmentProjectionState.PhysicalSnapshot));
        Check(physicalSnapshot.Decision == DevelopmentBackingDecision.ReadyForOfflineApply, "aligned physical snapshot is ready");
        DevelopmentBackingAssessment bootstrapGitlink = DevelopmentBackingPolicy.Evaluate(gitSubmodule, AlignedObservation(DevelopmentProjectionState.BootstrapGitlink));
        Check(bootstrapGitlink.Decision == DevelopmentBackingDecision.ReadyForOfflineApply, "aligned bootstrap Gitlink is ready");
        DevelopmentBackingAssessment correctJunction = DevelopmentBackingPolicy.Evaluate(gitSubmodule, AlignedObservation(DevelopmentProjectionState.CorrectJunction));
        Check(correctJunction.Decision == DevelopmentBackingDecision.AlreadyConfigured, "correct Junction is idempotent");
        DevelopmentBackingAssessment conflict = DevelopmentBackingPolicy.Evaluate(gitSubmodule, AlignedObservation(DevelopmentProjectionState.Conflict));
        Check(conflict.Decision == DevelopmentBackingDecision.Conflict, "conflicting projection is rejected");

        DevelopmentSourceObservation dirtyObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", true, true, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment dirtyAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, dirtyObservation);
        Check(dirtyAssessment.Decision == DevelopmentBackingDecision.Blocked, "dirty source is blocked");
        DevelopmentSourceObservation pinMismatchObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "b", false, true, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment pinMismatchAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, pinMismatchObservation);
        Check(pinMismatchAssessment.Decision == DevelopmentBackingDecision.Blocked, "pin mismatch is blocked");
        DevelopmentSourceObservation contentMismatchObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", false, false, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment contentMismatchAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, contentMismatchObservation);
        Check(contentMismatchAssessment.Decision == DevelopmentBackingDecision.Blocked, "content mismatch is blocked");
        DevelopmentSourceObservation projectOnlyObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", false, true, true, true, true, true, "content-evidence");
        DevelopmentBackingAssessment projectOnlyAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, projectOnlyObservation);
        Check(projectOnlyAssessment.Decision == DevelopmentBackingDecision.Conflict, "Project-only changes conflict");
        DevelopmentSourceObservation metadataOwnershipObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", false, true, false, false, true, true, "content-evidence");
        DevelopmentBackingAssessment metadataOwnershipAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, metadataOwnershipObservation);
        Check(metadataOwnershipAssessment.Decision == DevelopmentBackingDecision.Conflict, "invalid metadata ownership conflicts");
        DevelopmentSourceObservation pathOwnershipObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", false, true, false, true, false, true, "content-evidence");
        DevelopmentBackingAssessment pathOwnershipAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, pathOwnershipObservation);
        Check(pathOwnershipAssessment.Decision == DevelopmentBackingDecision.Conflict, "overlapping Project path ownership conflicts");
        DevelopmentSourceObservation indexMismatchObservation = Observation(DevelopmentProjectionState.CorrectJunction, true, "a", "a", false, true, false, true, true, false, "content-evidence");
        DevelopmentBackingAssessment indexMismatchAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, indexMismatchObservation);
        Check(indexMismatchAssessment.Decision == DevelopmentBackingDecision.Conflict, "unaligned Git index conflicts");
        DevelopmentProjectionState[] indexStates = new DevelopmentProjectionState[] { DevelopmentProjectionState.Absent, DevelopmentProjectionState.PhysicalSnapshot, DevelopmentProjectionState.BootstrapGitlink, DevelopmentProjectionState.CorrectJunction };
        foreach (DevelopmentProjectionState indexState in indexStates)
        {
            DevelopmentSourceObservation unalignedObservation = Observation(indexState, true, "a", "a", false, true, false, true, true, false, "unaligned-index-evidence");
            DevelopmentBackingAssessment unalignedAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, unalignedObservation);
            Check(unalignedAssessment.Decision == DevelopmentBackingDecision.Conflict, indexState + " requires verified Project index alignment");
        }
        DevelopmentSourceObservation missingEvidenceObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "a", "a", false, true, false, true, true, true, string.Empty);
        DevelopmentBackingAssessment missingEvidenceAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, missingEvidenceObservation);
        Check(missingEvidenceAssessment.Decision == DevelopmentBackingDecision.Blocked, "missing evidence is blocked");
        DevelopmentSourceObservation missingSourceObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, false, "a", "a", false, true, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment missingSourceAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, missingSourceObservation);
        Check(missingSourceAssessment.Decision == DevelopmentBackingDecision.Blocked, "missing source is blocked");
        DevelopmentSourceObservation staleJunctionObservation = Observation(DevelopmentProjectionState.CorrectJunction, true, "a", "b", false, true, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment staleJunctionAssessment = DevelopmentBackingPolicy.Evaluate(gitSubmodule, staleJunctionObservation);
        Check(staleJunctionAssessment.Decision == DevelopmentBackingDecision.Blocked, "stale Junction is not already configured");

        DevelopmentSourceObservation localDraftObservation = Observation(DevelopmentProjectionState.PhysicalSnapshot, true, "draft-a", string.Empty, false, true, false, true, true, true, "content-evidence");
        DevelopmentBackingAssessment localDraft = DevelopmentBackingPolicy.Evaluate(DevelopmentSourceKind.LocalDraft, localDraftObservation);
        Check(localDraft.Decision == DevelopmentBackingDecision.ReadyForOfflineApply && localDraft.NeedsPublish, "physical LocalDraft requires publishing");
        DevelopmentBackingAssessment absentLocalDraft = DevelopmentBackingPolicy.Evaluate(DevelopmentSourceKind.LocalDraft, AlignedObservation(DevelopmentProjectionState.Absent));
        Check(absentLocalDraft.Decision == DevelopmentBackingDecision.Blocked && !absentLocalDraft.NeedsPublish, "absent LocalDraft is unsupported");

        Console.WriteLine("PASS: " + checks + " development contracts");
    }
}
