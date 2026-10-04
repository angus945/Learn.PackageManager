using System.Collections.Generic;
using Xunit;

namespace Module.Verification.Evidence.Tests
{
    public sealed class EvidenceContractTests
    {
        [Theory]
        [InlineData(EvidenceKind.FactStream)]
        [InlineData(EvidenceKind.StateSnapshot)]
        [InlineData(EvidenceKind.Evaluation)]
        [InlineData(EvidenceKind.Trace)]
        [InlineData(EvidenceKind.Diagnostic)]
        [InlineData(EvidenceKind.ReplayRecording)]
        public void BundleCanReferenceArtifactWithoutPayloadDuplication(EvidenceKind kind)
        {
            EvidenceReference reference = new EvidenceReference("artifact", kind.ToString());
            EvidenceEntry entry = new EvidenceEntry(kind, "artifact", reference, 0);
            EvidenceBuilder builder = new EvidenceBuilder(new EvidenceManifest("run", "case"));
            builder.TryAdd(entry);

            EvidenceBundle bundle = builder.Build();

            Assert.Equal(reference, bundle.Entries[0].Reference);
            Assert.Equal(kind, bundle.Entries[0].Kind);
        }

        [Fact]
        public void ManifestPreservesReferenceIdentityAndMetadataIsCopied()
        {
            Dictionary<string, string> metadata = new Dictionary<string, string>();
            metadata.Add("format", "json");
            EvidenceReference reference = new EvidenceReference("memory", "one");
            EvidenceEntry entry = new EvidenceEntry(EvidenceKind.Trace, "trace", reference, 0, metadata: metadata);
            metadata["format"] = "changed";

            Assert.Equal(new EvidenceReference("memory", "one"), entry.Reference);
            Assert.Equal("json", entry.Metadata["format"]);
        }
    }
}
