using System;
using System.Collections.Generic;

namespace CraftyRacoon.PackageManagement.Domain
{
    public sealed class ManagedPackageRequirement
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public ManagedPackageRequirement(
            string id,
            string displayName,
            string providerId,
            IReadOnlyDictionary<string, string>? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Requirement id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(providerId))
                throw new ArgumentException("Requirement provider id is required.", nameof(providerId));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            ProviderId = providerId;
            Parameters = parameters ?? EmptyParameters;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string ProviderId { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }

        public string GetParameterOrEmpty(string key)
        {
            return Parameters.TryGetValue(key, out string? value)
                ? value ?? string.Empty
                : string.Empty;
        }
    }
}
