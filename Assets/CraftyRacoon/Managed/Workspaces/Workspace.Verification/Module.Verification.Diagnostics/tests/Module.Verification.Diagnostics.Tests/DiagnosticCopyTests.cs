using System.Collections.Generic;
using Xunit;

namespace Module.Verification.Diagnostics.Tests
{
    public sealed class DiagnosticCopyTests
    {
        [Theory]
        [InlineData(DiagnosticSeverity.Info)]
        [InlineData(DiagnosticSeverity.Warning)]
        [InlineData(DiagnosticSeverity.Error)]
        public void FactoriesKeepRequestedSeverityAndCopyProperties(DiagnosticSeverity severity)
        {
            Dictionary<string, string> properties = new Dictionary<string, string> { ["parameter"] = "actor" };
            DiagnosticTextSpan span = new DiagnosticTextSpan(4, 2, 1, 5);
            DiagnosticLocation location = new DiagnosticLocation("source", "revision", span, "arguments.actor", "scene.narrative");
            Diagnostic created = Diagnostic.Create("producer", "TEST", severity, "message", location, properties);
            Diagnostic shortcut;
            if (severity == DiagnosticSeverity.Info) shortcut = Diagnostic.Info("producer", "TEST", "message", location, properties);
            else if (severity == DiagnosticSeverity.Warning) shortcut = Diagnostic.Warning("producer", "TEST", "message", location, properties);
            else shortcut = Diagnostic.Error("producer", "TEST", "message", location, properties);
            properties["parameter"] = "changed";

            Assert.Equal(created.Descriptor, shortcut.Descriptor);
            Assert.Equal("producer", shortcut.Producer);
            Assert.Equal("TEST", shortcut.Code);
            Assert.Equal(severity, shortcut.Severity);
            Assert.Equal(severity, shortcut.Descriptor.DefaultSeverity);
            Assert.Same(location, shortcut.Location);
            Assert.Equal("actor", created.Properties["parameter"]);
            Assert.Equal("actor", shortcut.Properties["parameter"]);
            Assert.Empty(shortcut.RelatedLocations);
            Assert.Empty(shortcut.MessageArguments);
        }

        [Fact]
        public void WithPropertiesCopiesNewValuesAndKeepsCompleteOriginalContext()
        {
            Diagnostic source = CreateSource();
            Dictionary<string, string> properties = new Dictionary<string, string>(source.Properties) { ["entry"] = "start" };
            Diagnostic changed = source.WithProperties(properties);
            properties["entry"] = "mutated";
            properties.Clear();

            Assert.NotSame(source, changed);
            AssertCompleteIdentity(source, changed);
            Assert.Same(source.Location, changed.Location);
            Assert.Equal("actor", changed.Properties["parameter"]);
            Assert.Equal("start", changed.Properties["entry"]);
            Assert.False(source.Properties.ContainsKey("entry"));
        }

        [Fact]
        public void WithLocationReplacesOnlyPrimaryLocationAndCanRemoveIt()
        {
            Diagnostic source = CreateSource();
            DiagnosticLocation location = new DiagnosticLocation("replacement", "new-revision", fieldPath: "new.field", displayPath: "new.path");
            Diagnostic changed = source.WithLocation(location);
            Diagnostic unlocated = changed.WithLocation(null);

            AssertCompleteIdentity(source, changed);
            AssertCompleteIdentity(source, unlocated);
            Assert.Same(location, changed.Location);
            Assert.Null(unlocated.Location);
            Assert.Equal("actor", changed.Properties["parameter"]);
            Assert.Equal("actor", unlocated.Properties["parameter"]);
            Assert.Equal("source", source.Location?.ResourceId);
        }

        [Fact]
        public void WithResourcePreservesSourceCoordinatesAndOptionalDisplayPath()
        {
            Diagnostic source = CreateSource();
            Diagnostic changed = source.WithResource("new-source", "new-revision");
            Diagnostic renamed = changed.WithResource("new-source", "new-revision", "renamed.narrative");

            AssertCompleteIdentity(source, changed);
            AssertCompleteIdentity(source, renamed);
            Assert.Equal("new-source", changed.Location?.ResourceId);
            Assert.Equal("new-revision", changed.Location?.ResourceRevision);
            Assert.Same(source.Location?.TextSpan, changed.Location?.TextSpan);
            Assert.Equal(source.Location?.FieldPath, changed.Location?.FieldPath);
            Assert.Equal("scene.narrative", changed.Location?.DisplayPath);
            Assert.Equal("renamed.narrative", renamed.Location?.DisplayPath);
            Assert.Equal("actor", changed.Properties["parameter"]);
            Assert.Equal("source", source.Location?.ResourceId);
        }

        [Fact]
        public void AddingResourceContextDoesNotInventUnknownSourceCoordinates()
        {
            Diagnostic source = Diagnostic.Error("producer", "TEST", "message");
            Diagnostic changed = source.WithResource("source", "revision", "scene.narrative");

            Assert.Null(source.Location);
            Assert.Equal("source", changed.Location?.ResourceId);
            Assert.Equal("revision", changed.Location?.ResourceRevision);
            Assert.Equal("scene.narrative", changed.Location?.DisplayPath);
            Assert.Null(changed.Location?.TextSpan);
            Assert.Null(changed.Location?.FieldPath);
        }

        private static Diagnostic CreateSource()
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("producer", "TEST", DiagnosticSeverity.Info, "category", "message {0}", "message.key");
            DiagnosticTextSpan span = new DiagnosticTextSpan(4, 2, 1, 5);
            DiagnosticLocation location = new DiagnosticLocation("source", "revision", span, "arguments.actor", "scene.narrative");
            Dictionary<string, string> properties = new Dictionary<string, string> { ["parameter"] = "actor" };
            DiagnosticLocation[] related = { new DiagnosticLocation("other-source", "other-revision", span, "declaration.actor", "definition.json") };
            string[] messageArguments = { "actor" };
            return new Diagnostic(descriptor, "message actor", DiagnosticSeverity.Warning, location, properties, related, messageArguments);
        }

        private static void AssertCompleteIdentity(Diagnostic source, Diagnostic changed)
        {
            Assert.Same(source.Descriptor, changed.Descriptor);
            Assert.Equal(source.Producer, changed.Producer);
            Assert.Equal(source.Code, changed.Code);
            Assert.Equal(source.Message, changed.Message);
            Assert.Equal(DiagnosticSeverity.Warning, changed.Severity);
            Assert.Equal(DiagnosticSeverity.Info, changed.Descriptor.DefaultSeverity);
            Assert.Equal("category", changed.Descriptor.Category);
            Assert.Equal("message.key", changed.Descriptor.MessageKey);
            Assert.Equal("message {0}", changed.Descriptor.DefaultMessageTemplate);
            Assert.Equal(source.MessageArguments, changed.MessageArguments);
            DiagnosticLocation related = Assert.Single(changed.RelatedLocations);
            Assert.Same(source.RelatedLocations[0], related);
            Assert.Equal("declaration.actor", related.FieldPath);
            Assert.Equal("definition.json", related.DisplayPath);
        }
    }
}
