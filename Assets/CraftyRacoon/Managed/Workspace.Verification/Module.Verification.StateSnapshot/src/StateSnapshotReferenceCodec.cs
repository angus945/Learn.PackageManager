#nullable enable

using System;
using System.Globalization;

namespace Module.Verification.StateSnapshot
{
    public static class StateSnapshotReferenceCodec
    {
        public static string Encode(StateSnapshotReference reference)
        {
            string captureId = reference.CaptureId.ToString(CultureInfo.InvariantCulture);
            return $"{reference.ChannelId:N}:{captureId}";
        }

        public static bool TryDecode(string? value, out StateSnapshotReference reference)
        {
            reference = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(':');

            if (parts.Length != 2)
            {
                return false;
            }

            if (!Guid.TryParseExact(parts[0], "N", out Guid channelId))
            {
                return false;
            }

            if (channelId == Guid.Empty)
            {
                return false;
            }

            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long captureId))
            {
                return false;
            }

            if (captureId < 1)
            {
                return false;
            }

            reference = new StateSnapshotReference(channelId, captureId);
            return true;
        }
    }
}
