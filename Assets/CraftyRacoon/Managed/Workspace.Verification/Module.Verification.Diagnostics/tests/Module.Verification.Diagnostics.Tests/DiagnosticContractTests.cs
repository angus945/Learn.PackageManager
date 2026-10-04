#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Module.Verification.Diagnostics.Tests
{
    public sealed class DiagnosticContractTests
    {
        [Fact]
        public void CoreReferencesOnlyTheStandardLibrary()
        {
            Assembly assembly = typeof(Diagnostic).Assembly;
            AssemblyName[] references = assembly.GetReferencedAssemblies();
            Assert.NotEmpty(references);
            foreach (AssemblyName reference in references)
            {
                Assert.Equal("netstandard", reference.Name);
            }
        }

        [Fact]
        public void IdentityUsesOrdinalProducerAndCodeWithoutLocalizedText()
        {
            DiagnosticDescriptor english = new DiagnosticDescriptor("module.runtime", "NR1001", DiagnosticSeverity.Error, "execution", "Missing value", "missing.value");
            DiagnosticDescriptor translated = new DiagnosticDescriptor("module.runtime", "NR1001", DiagnosticSeverity.Warning, "execution", "缺少值", "missing.value");
            DiagnosticDescriptor differentProducer = new DiagnosticDescriptor("module.language", "NR1001", DiagnosticSeverity.Error);
            DiagnosticDescriptor differentCase = new DiagnosticDescriptor("module.runtime", "nr1001", DiagnosticSeverity.Error);
            DiagnosticDescriptor differentCode = new DiagnosticDescriptor("module.runtime", "NR1002", DiagnosticSeverity.Error);

            Assert.Equal(english, translated);
            Assert.Equal(english.GetHashCode(), translated.GetHashCode());
            Assert.NotEqual(english, differentProducer);
            Assert.NotEqual(english, differentCase);
            Assert.NotEqual(english, differentCode);
        }

        [Fact]
        public void DiagnosticCopiesEverySuppliedCollectionAndRetainsSeverity()
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("module.runtime", "NR1001", DiagnosticSeverity.Error);
            DiagnosticTextSpan span = new DiagnosticTextSpan(4, 2, 1, 5);
            DiagnosticLocation location = new DiagnosticLocation("script:1", "revision:2", span, "parameters.actor", "scene.narrative");
            Dictionary<string, string> properties = new Dictionary<string, string> { ["parameterId"] = "actor" };
            List<DiagnosticLocation> related = new List<DiagnosticLocation> { location };
            List<string> arguments = new List<string> { "actor" };
            Diagnostic diagnostic = new Diagnostic(descriptor, "Missing actor", DiagnosticSeverity.Warning, location, properties, related, arguments);

            properties["parameterId"] = "camera";
            related.Clear();
            arguments[0] = "camera";

            Assert.Equal("module.runtime", diagnostic.Producer);
            Assert.Equal("NR1001", diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Same(location, diagnostic.Location);
            Assert.Equal("actor", diagnostic.Properties["parameterId"]);
            Assert.Same(location, Assert.Single(diagnostic.RelatedLocations));
            Assert.Equal("actor", Assert.Single(diagnostic.MessageArguments));
            DiagnosticLocation publishedLocation = Assert.IsType<DiagnosticLocation>(diagnostic.Location);
            Assert.Equal("script:1", publishedLocation.ResourceId);
            Assert.Equal("revision:2", publishedLocation.ResourceRevision);
            Assert.Equal("parameters.actor", publishedLocation.FieldPath);
            Assert.Equal("scene.narrative", publishedLocation.DisplayPath);
        }

        [Fact]
        public void DiagnosticCollectionsCannotBeMutatedThroughCollectionInterfaces()
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("producer", "CODE", DiagnosticSeverity.Info);
            DiagnosticLocation location = new DiagnosticLocation("resource");
            Dictionary<string, string> properties = new Dictionary<string, string> { ["field"] = "value" };
            DiagnosticLocation[] related = { location };
            string[] arguments = { "value" };
            Diagnostic diagnostic = new Diagnostic(descriptor, "message", properties: properties, relatedLocations: related, messageArguments: arguments);
            IDictionary<string, string> dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(diagnostic.Properties);
            IList<DiagnosticLocation> locations = Assert.IsAssignableFrom<IList<DiagnosticLocation>>(diagnostic.RelatedLocations);
            IList<string> values = Assert.IsAssignableFrom<IList<string>>(diagnostic.MessageArguments);

            Assert.Throws<NotSupportedException>(dictionary.Clear);
            Assert.Throws<NotSupportedException>(locations.Clear);
            Assert.Throws<NotSupportedException>(values.Clear);
        }

        [Fact]
        public void UnknownLocationDoesNotBecomeTheFirstCharacterOrLine()
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("producer", "CODE", DiagnosticSeverity.Error);
            Diagnostic unknown = new Diagnostic(descriptor, "message");
            DiagnosticLocation resourceOnly = new DiagnosticLocation("resource");
            DiagnosticTextSpan firstCharacter = new DiagnosticTextSpan(0, 1, 1, 1);
            DiagnosticTextSpan offsetOnly = new DiagnosticTextSpan(0, 0);

            Assert.Null(unknown.Location);
            Assert.Null(resourceOnly.TextSpan);
            Assert.Null(offsetOnly.Line);
            Assert.Null(offsetOnly.Column);
            Assert.Equal(0, firstCharacter.Start);
            Assert.Equal(1, firstCharacter.Line);
            Assert.Equal(1, firstCharacter.Column);
            Assert.Equal(1, firstCharacter.End);
        }

        [Fact]
        public void Utf16CoordinatesRetainChineseSurrogatePairsAndCrLfUnits()
        {
            string text = "中😀\r\n文";
            DiagnosticTextSpan emoji = new DiagnosticTextSpan(1, 2, 1, 2);
            DiagnosticTextSpan nextLine = new DiagnosticTextSpan(5, 1, 2, 1);
            string emojiText = text.Substring(emoji.Start, emoji.Length);
            string nextLineText = text.Substring(nextLine.Start, nextLine.Length);

            Assert.Equal("😀", emojiText);
            Assert.Equal(3, emoji.End);
            Assert.Equal("文", nextLineText);
            Assert.Equal(text.Length, nextLine.End);
        }

        [Theory]
        [InlineData(-1, 0, null, null)]
        [InlineData(0, -1, null, null)]
        [InlineData(int.MaxValue, 1, null, null)]
        [InlineData(0, 0, 0, 1)]
        [InlineData(0, 0, 1, 0)]
        public void TextSpanRejectsInvalidOrOverflowingCoordinates(int start, int length, int? line, int? column)
        {
            void Construct()
            {
                _ = new DiagnosticTextSpan(start, length, line, column);
            }

            Assert.Throws<ArgumentOutOfRangeException>(Construct);
        }

        [Fact]
        public void ReportCopiesContextAndDoesNotInventObservationMetadata()
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("producer", "CODE", DiagnosticSeverity.Warning);
            Diagnostic diagnostic = new Diagnostic(descriptor, "message");
            Dictionary<string, string> context = new Dictionary<string, string> { ["mode"] = "Live" };
            DateTimeOffset observedAt = new DateTimeOffset(2026, 9, 9, 12, 30, 0, TimeSpan.Zero);
            DiagnosticReport report = new DiagnosticReport(diagnostic, "occurrence", "operation", "correlation", "scope", 12, observedAt, context);
            DiagnosticReport minimal = new DiagnosticReport(diagnostic, "another-occurrence");
            context["mode"] = "Reconstruction";
            IDictionary<string, string> publishedContext = Assert.IsAssignableFrom<IDictionary<string, string>>(report.Context);

            Assert.Equal("Live", report.Context["mode"]);
            Assert.Throws<NotSupportedException>(publishedContext.Clear);
            Assert.Same(diagnostic, report.Diagnostic);
            Assert.Equal("operation", report.OperationId);
            Assert.Equal("correlation", report.CorrelationId);
            Assert.Equal("scope", report.ScopeId);
            Assert.Equal(12L, report.Generation);
            Assert.Equal(observedAt, report.ObservedAt);
            Assert.Null(minimal.ObservedAt);
            Assert.Null(minimal.Generation);
            Assert.Null(minimal.OperationId);
            Assert.Null(minimal.CorrelationId);
            Assert.Null(minimal.ScopeId);
            Assert.Empty(minimal.Context);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void EmptyProducerCodeAndOccurrenceCannotActAsIdentity(string? value)
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("producer", "CODE", DiagnosticSeverity.Error);
            Diagnostic diagnostic = new Diagnostic(descriptor, "message");

            void ConstructProducer()
            {
                _ = new DiagnosticDescriptor(value!, "CODE", DiagnosticSeverity.Error);
            }

            void ConstructCode()
            {
                _ = new DiagnosticDescriptor("producer", value!, DiagnosticSeverity.Error);
            }

            void ConstructReport()
            {
                _ = new DiagnosticReport(diagnostic, value!);
            }

            Assert.Throws<ArgumentException>(ConstructProducer);
            Assert.Throws<ArgumentException>(ConstructCode);
            Assert.Throws<ArgumentException>(ConstructReport);
        }
    }
}
