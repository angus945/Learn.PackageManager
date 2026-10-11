using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal static class DevelopmentBootstrapIO
    {
        internal static string Run(string executable, string workingDirectory, IEnumerable<string> arguments,
            CancellationToken cancellation = default, IDictionary<string, string>? variables = null)
        {
            IEnumerable<string> actual = arguments;
            string? gitCommand = executable == "git" ? arguments.FirstOrDefault() : null;
            if (executable == "git" && gitCommand != "check-ignore") actual = new[] { "--literal-pathspecs" }.Concat(arguments);
            ProcessStartInfo start = new ProcessStartInfo(executable, string.Join(" ", actual.Select(Quote)))
            {
                WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            if (executable == "git") start.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            if (variables != null) foreach (var variable in variables) start.EnvironmentVariables[variable.Key] = variable.Value;
            using (var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + executable))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                var timeout = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (cancellation.IsCancellationRequested || timeout.Elapsed.TotalMinutes > 5)
                    {
                        process.Kill();
                        cancellation.ThrowIfCancellationRequested();
                        throw new TimeoutException(executable + " exceeded five minutes.");
                    }
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException(executable + " failed (" + process.ExitCode + "): " + stderr);
                return stdout.TrimEnd('\r', '\n');
            }
        }
        internal static string Git(string root, params string[] arguments)
        {
            string result = Run("git", root, arguments);
            // Origin reads in this adapter feed portable manifest/binding records. Never persist credentials or a local path.
            if (arguments.SequenceEqual(new[] { "remote", "get-url", "origin" })) RequireRemote(result);
            return result;
        }
        internal static string Quote(string value)
        {
            if (value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new ArgumentException("Invalid process argument.");
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        internal static string RemoteIdentity(string value)
        {
            string result = value.Trim().TrimEnd('/');
            if (!result.Contains(":") && !result.Contains("@") && result.Count(c => c == '/') == 1) result = "https://github.com/" + result;
            if (result.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) result = "https://github.com/" + result.Substring(15);
            if (result.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) result = result.Substring(0, result.Length - 4);
            return result;
        }
        internal static void RequireRemote(string value)
        {
            if (value.Any(char.IsControl) || value.StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException("Invalid repository URL.");
            if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase) && value.Length > 15 &&
                value.IndexOf('?') < 0 && value.IndexOf('#') < 0) return;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != "https" && uri.Scheme != "ssh") ||
                (!string.IsNullOrEmpty(uri.UserInfo) && uri.Scheme == "https") ||
                uri.UserInfo.Contains(":") || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("Use a portable HTTPS/SSH repository URL without embedded credentials, query or fragment.");
        }
        internal static string ResolveTrackingRef(
            string workingDirectory,
            string repository,
            string trackingRef,
            CancellationToken cancellation = default)
        {
            RequireRemote(repository);
            if (string.IsNullOrWhiteSpace(trackingRef))
                throw new InvalidOperationException("PackageSet tracking ref is empty.");

            string fullRef = trackingRef.StartsWith("refs/", StringComparison.Ordinal)
                ? trackingRef
                : "refs/heads/" + trackingRef;

            string output = Run(
                "git",
                workingDirectory,
                new[] { "ls-remote", "--exit-code", "--refs", repository, fullRef },
                cancellation);

            string[] lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length != 1)
                throw new InvalidOperationException(
                    "Tracking ref did not resolve to exactly one remote ref: " + fullRef);

            int separator = lines[0].IndexOfAny(new[] { '\t', ' ' });
            if (separator <= 0)
                throw new InvalidOperationException("Unexpected git ls-remote output.");

            string revision = lines[0].Substring(0, separator);
            if ((revision.Length != 40 && revision.Length != 64) ||
                revision.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidOperationException(
                    "Tracking ref resolved to an invalid Git object id.");
            }

            return revision;
        }

        internal static bool Exists(string path)
        { try { File.GetAttributes(path); return true; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
        internal static bool Reparse(string path) => Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        internal static string Full(string value)
        {
            string path = Path.GetFullPath(value);
            return path == Path.GetPathRoot(path) ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        internal static bool Within(string root, string candidate) => Full(candidate).StartsWith(Full(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        internal static void NoReparseAncestors(string path, bool allowLeaf = false)
        {
            DirectoryInfo? current = new DirectoryInfo(allowLeaf ? Path.GetDirectoryName(Full(path))! : Full(path));
            while (current != null)
            {
                if (Reparse(current.FullName)) throw new InvalidOperationException("Reparse-point ancestor is not allowed: " + current.FullName);
                current = current.Parent;
            }
        }
        internal static string Under(string root, string relative)
        {
            string path = Full(Path.Combine(root, PackageRelativePath.Normalize(relative)));
            if (!Within(root, path)) throw new InvalidOperationException("Path escapes declared root.");
            NoReparseAncestors(path, true);
            return path;
        }
        internal static string Relative(string root, string path)
        {
            if (!Within(root, path)) throw new InvalidOperationException("Path is outside the root: " + path);
            return Full(path).Substring(Full(root).TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
        }
        internal static string JunctionTarget(string path)
        {
            if (!Reparse(path)) return string.Empty;
            const string command = "$ErrorActionPreference='Stop'; $p=Get-Item -LiteralPath $env:PM_BACKING_PATH -Force; if($p.LinkType -ne 'Junction'){throw 'Not an NTFS Junction'}; [IO.Path]::GetFullPath([string]$p.Target[0])";
            return Full(Run("powershell.exe", Path.GetDirectoryName(path)!, new[] { "-NoProfile", "-NonInteractive", "-Command", command },
                variables: new Dictionary<string, string> { { "PM_BACKING_PATH", path } }));
        }
        internal static string HashFile(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return Hex(hash.ComputeHash(stream));
        }
        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        internal static SortedDictionary<string, string> Files(string root)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            void Visit(string directory)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    string relative = Relative(root, entry);
                    if (relative == ".git" || relative == ".packageset-provenance.json") continue;
                    if (Reparse(entry)) throw new InvalidOperationException("Nested reparse point: " + entry);
                    PackageRelativePath.Normalize(relative);
                    if (Directory.Exists(entry)) Visit(entry); else result.Add(relative, entry);
                }
            }
            Visit(root);
            return result;
        }
        internal static string HashTree(string root)
        {
            string record = string.Concat(Files(root)
                .Where(p => !p.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Key + "\n" + HashFile(p.Value) + "\n"));
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(record)));
        }
        internal static void CopyTree(string source, string destination)
        {
            // Extraction must not lose the parent Project's ignore policy and then accidentally commit ignored content.
            string parentRepository = Full(Git(source, "rev-parse", "--show-toplevel"));
            string selection = Relative(parentRepository, source);
            foreach (string ignored in Git(parentRepository, "ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--", selection)
                .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                if (!ignored.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Resolve Project-ignored non-metadata before extracting this source: " + ignored);
            foreach (var file in Files(source))
            {
                string output = Path.Combine(destination, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(file.Value, output, false);
            }
        }
        internal static string Gitlink(string root, string relative)
        {
            string line = Git(root, "ls-tree", "HEAD", "--", relative);
            return line.StartsWith("160000 commit ", StringComparison.Ordinal) ? line.Substring(14, line.IndexOf('\t') - 14) : string.Empty;
        }
        internal static Dictionary<string, string> GitTree(string root, string? prefix = null)
        {
            string output = prefix == null ? Git(root, "ls-tree", "-r", "-z", "HEAD") : Git(root, "ls-tree", "-r", "-z", "HEAD", "--", prefix);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = line.IndexOf('\t');
                if (tab < 0 || (!line.StartsWith("100644 blob ", StringComparison.Ordinal) && !line.StartsWith("100755 blob ", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Nested submodules/symlinks require an explicit projection strategy.");
                string path = line.Substring(tab + 1);
                if (prefix != null) path = path.Substring(prefix.Length + 1);
                result.Add(path, line.Substring(0, 6) + " " + line.Substring(12, tab - 12));
            }
            return result;
        }
    }
}
