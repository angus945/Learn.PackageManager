#nullable enable

using System;
using Xunit;

namespace Module.Verification.StateSnapshot.Tests
{
    public sealed class StateSnapshotReferenceCodecTests
    {
        [Fact]
        public void EncodeUsesCanonicalFormat()
        {
            Guid channelId = Guid.ParseExact("00112233445566778899aabbccddeeff", "N");
            StateSnapshotReference reference = new StateSnapshotReference(channelId, 42);

            string encoded = StateSnapshotReferenceCodec.Encode(reference);

            Assert.Equal("00112233445566778899aabbccddeeff:42", encoded);
        }

        [Fact]
        public void TryDecodeRestoresReference()
        {
            Guid channelId = Guid.ParseExact("00112233445566778899aabbccddeeff", "N");
            StateSnapshotReference expected = new StateSnapshotReference(channelId, 42);

            bool decoded = StateSnapshotReferenceCodec.TryDecode("00112233445566778899aabbccddeeff:42", out StateSnapshotReference actual);

            Assert.True(decoded);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("not-a-reference")]
        [InlineData("00112233445566778899aabbccddeeff")]
        [InlineData("00112233445566778899aabbccddeeff:42:extra")]
        [InlineData("00112233-4455-6677-8899-aabbccddeeff:42")]
        [InlineData("00000000000000000000000000000000:42")]
        [InlineData("00112233445566778899aabbccddeeff:0")]
        [InlineData("00112233445566778899aabbccddeeff:-1")]
        [InlineData("00112233445566778899aabbccddeeff:+42")]
        [InlineData("00112233445566778899aabbccddeeff: 42")]
        public void TryDecodeRejectsNonCanonicalOrInvalidValues(string? value)
        {
            bool decoded = StateSnapshotReferenceCodec.TryDecode(value, out StateSnapshotReference reference);

            Assert.False(decoded);
            Assert.Equal(default(StateSnapshotReference), reference);
        }
    }
}
