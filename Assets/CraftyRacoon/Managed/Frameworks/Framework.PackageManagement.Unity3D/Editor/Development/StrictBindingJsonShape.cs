using System;
using System.Collections.Generic;
using System.Globalization;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal static class StrictBindingJsonShape
    {
        internal static bool ValidateProject(string json, ICollection<string> diagnostics)
        {
            try
            {
                StrictJsonValue root = StrictJsonDocument.Parse(json);
                string[] members = { "schemaVersion", "repository", "trackingRef", "resolvedRevision", "managedProjectionRoot", "needsPublish" };
                if (!ValidateExactObject(root, "Project PackageSet binding", members, diagnostics)) return false;
                bool valid = true;
                bool schemaVersionValid = ValidateMember(root, "schemaVersion", StrictJsonValueKind.Number, true, diagnostics);
                valid = schemaVersionValid && valid;
                bool repositoryValid = ValidateMember(root, "repository", StrictJsonValueKind.String, false, diagnostics);
                valid = repositoryValid && valid;
                bool trackingRefValid = ValidateMember(root, "trackingRef", StrictJsonValueKind.String, false, diagnostics);
                valid = trackingRefValid && valid;
                bool resolvedRevisionValid = ValidateMember(root, "resolvedRevision", StrictJsonValueKind.String, false, diagnostics);
                valid = resolvedRevisionValid && valid;
                bool projectionRootValid = ValidateMember(root, "managedProjectionRoot", StrictJsonValueKind.String, false, diagnostics);
                valid = projectionRootValid && valid;
                bool needsPublishValid = ValidateMember(root, "needsPublish", StrictJsonValueKind.Boolean, false, diagnostics);
                valid = needsPublishValid && valid;
                return valid;
            }
            catch (Exception error)
            {
                string diagnostic = "Strict Project binding JSON is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
                return false;
            }
        }

        internal static bool ValidateRuntime(string json, ICollection<string> diagnostics)
        {
            try
            {
                StrictJsonValue root = StrictJsonDocument.Parse(json);
                string[] members = { "schemaVersion", "components" };
                if (!ValidateExactObject(root, "runtime binding", members, diagnostics)) return false;
                bool schemaVersionValid = ValidateMember(root, "schemaVersion", StrictJsonValueKind.Number, true, diagnostics);
                bool valid = schemaVersionValid;
                bool componentsValid = ValidateMember(root, "components", StrictJsonValueKind.Array, false, diagnostics);
                valid = componentsValid && valid;
                StrictJsonValue? components;
                if (!root.Members.TryGetValue("components", out components) || components == null || components.Kind != StrictJsonValueKind.Array) return false;
                string[] componentMembers = { "id", "repository", "path" };
                for (int index = 0; index < components.Items.Count; index++)
                {
                    StrictJsonValue component = components.Items[index];
                    string context = "runtime binding component " + index;
                    bool componentObject = ValidateExactObject(component, context, componentMembers, diagnostics);
                    if (!componentObject)
                    {
                        valid = false;
                        continue;
                    }
                    bool identityValid = ValidateMember(component, "id", StrictJsonValueKind.String, false, diagnostics);
                    valid = identityValid && valid;
                    bool repositoryValid = ValidateMember(component, "repository", StrictJsonValueKind.String, false, diagnostics);
                    valid = repositoryValid && valid;
                    bool pathValid = ValidateMember(component, "path", StrictJsonValueKind.String, false, diagnostics);
                    valid = pathValid && valid;
                }
                return valid;
            }
            catch (Exception error)
            {
                string diagnostic = "Strict runtime binding JSON is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
                return false;
            }
        }

        internal static bool ValidatePackageSet(string json, ICollection<string> diagnostics)
        {
            try
            {
                StrictJsonValue root = StrictJsonDocument.Parse(json);
                string[] members = { "schemaVersion", "identity", "target", "sourceRoot", "managedProjectionRoot", "entries" };
                if (!ValidateExactObject(root, "PackageSet manifest", members, diagnostics)) return false;
                bool valid = true;
                bool schemaVersionValid = ValidateMember(root, "schemaVersion", StrictJsonValueKind.Number, true, diagnostics);
                valid = schemaVersionValid && valid;
                bool identityValid = ValidateMember(root, "identity", StrictJsonValueKind.String, false, diagnostics);
                valid = identityValid && valid;
                bool targetValid = ValidateMember(root, "target", StrictJsonValueKind.Object, false, diagnostics);
                valid = targetValid && valid;
                bool sourceRootValid = ValidateMember(root, "sourceRoot", StrictJsonValueKind.String, false, diagnostics);
                valid = sourceRootValid && valid;
                bool projectionRootValid = ValidateMember(root, "managedProjectionRoot", StrictJsonValueKind.String, false, diagnostics);
                valid = projectionRootValid && valid;
                bool entriesValid = ValidateMember(root, "entries", StrictJsonValueKind.Array, false, diagnostics);
                valid = entriesValid && valid;
                StrictJsonValue? target;
                if (root.Members.TryGetValue("target", out target) && target != null && target.Kind == StrictJsonValueKind.Object)
                {
                    string[] targetMembers = { "identity", "repository" };
                    bool targetObject = ValidateExactObject(target, "PackageSet target", targetMembers, diagnostics);
                    valid = targetObject && valid;
                    if (targetObject)
                    {
                        bool targetIdentityValid = ValidateMember(target, "identity", StrictJsonValueKind.String, false, diagnostics);
                        valid = targetIdentityValid && valid;
                        bool targetRepositoryValid = ValidateMember(target, "repository", StrictJsonValueKind.String, false, diagnostics);
                        valid = targetRepositoryValid && valid;
                    }
                }
                StrictJsonValue? entries;
                if (!root.Members.TryGetValue("entries", out entries) || entries == null || entries.Kind != StrictJsonValueKind.Array) return false;
                string[] entryMembers = { "identity", "sourcePath", "projectionPath", "kind", "bootstrapRuntime" };
                for (int index = 0; index < entries.Items.Count; index++)
                {
                    StrictJsonValue entry = entries.Items[index];
                    string context = "PackageSet entry " + index;
                    bool entryObject = ValidateExactObject(entry, context, entryMembers, diagnostics);
                    if (!entryObject)
                    {
                        valid = false;
                        continue;
                    }
                    bool entryIdentityValid = ValidateMember(entry, "identity", StrictJsonValueKind.String, false, diagnostics);
                    valid = entryIdentityValid && valid;
                    bool sourcePathValid = ValidateMember(entry, "sourcePath", StrictJsonValueKind.String, false, diagnostics);
                    valid = sourcePathValid && valid;
                    bool projectionPathValid = ValidateMember(entry, "projectionPath", StrictJsonValueKind.String, false, diagnostics);
                    valid = projectionPathValid && valid;
                    bool kindValid = ValidateMember(entry, "kind", StrictJsonValueKind.String, false, diagnostics);
                    valid = kindValid && valid;
                    bool bootstrapRuntimeValid = ValidateMember(entry, "bootstrapRuntime", StrictJsonValueKind.Boolean, false, diagnostics);
                    valid = bootstrapRuntimeValid && valid;
                }
                return valid;
            }
            catch (Exception error)
            {
                string diagnostic = "Strict PackageSet manifest JSON is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
                return false;
            }
        }

        internal static bool ValidateLocalCheckout(string json, ICollection<string> diagnostics)
        {
            try
            {
                StrictJsonValue root = StrictJsonDocument.Parse(json);
                string[] members = { "schemaVersion", "checkoutPath" };
                if (!ValidateExactObject(root, "local PackageSet checkout", members, diagnostics)) return false;
                bool schemaVersionValid = ValidateMember(root, "schemaVersion", StrictJsonValueKind.Number, true, diagnostics);
                bool valid = schemaVersionValid;
                bool checkoutPathValid = ValidateMember(root, "checkoutPath", StrictJsonValueKind.String, false, diagnostics);
                valid = checkoutPathValid && valid;
                return valid;
            }
            catch (Exception error)
            {
                string diagnostic = "Strict local PackageSet checkout JSON is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
                return false;
            }
        }

        private static bool ValidateExactObject(StrictJsonValue value, string context, IReadOnlyList<string> requiredMembers, ICollection<string> diagnostics)
        {
            if (value.Kind != StrictJsonValueKind.Object)
            {
                diagnostics.Add(context + " must be a JSON object.");
                return false;
            }
            HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string requiredMember in requiredMembers) allowed.Add(requiredMember);
            bool valid = true;
            foreach (string member in value.Members.Keys)
            {
                if (allowed.Contains(member)) continue;
                string diagnostic = context + " contains unknown member: " + member + ".";
                diagnostics.Add(diagnostic);
                valid = false;
            }
            foreach (string requiredMember in requiredMembers)
            {
                if (value.Members.ContainsKey(requiredMember)) continue;
                string diagnostic = context + " is missing required member: " + requiredMember + ".";
                diagnostics.Add(diagnostic);
                valid = false;
            }
            return valid;
        }

        private static bool ValidateMember(StrictJsonValue value, string name, StrictJsonValueKind expectedKind, bool integer, ICollection<string> diagnostics)
        {
            StrictJsonValue? member;
            if (!value.Members.TryGetValue(name, out member) || member == null) return false;
            if (member.Kind != expectedKind)
            {
                string diagnostic = name + " has the wrong JSON type.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (!integer) return true;
            int parsed;
            bool validInteger = int.TryParse(member.Text, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
            if (validInteger) return true;
            string integerDiagnostic = name + " must be a JSON integer.";
            diagnostics.Add(integerDiagnostic);
            return false;
        }
    }
}
