﻿// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using GlobExpressions;
using JetBrains.Annotations;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;

// ReSharper disable ArrangeMethodOrOperatorBody

namespace Nuke.Common.IO
{
    /// <summary>
    /// Indicates the case sensitivity used for globbing.
    /// </summary>
    public enum GlobbingCaseSensitivity
    {
        /// <summary>
        /// Automatically determines whether to use case-sensitive or case-insensitive matching when globbing. This
        /// means using case-insensitive matching when running on Windows, and case-sensitive otherwise.
        /// </summary>
        Auto,
        /// <summary>
        /// Globbing patterns will be case-sensitive.
        /// </summary>
        CaseSensitive,
        /// <summary>
        /// Globbing patterns will be case-insensitive.
        /// </summary>
        CaseInsensitive
    }

    /// <summary>
    /// <p>Provides an abstraction for generating Windows/Unix/UNC-compliant
    /// file-system paths independently of the underlying operating system. Usages should be restricted to the moment
    /// of construction, i.e., avoid using it as part of an API.</p>
    /// <p>Casting a string with <c>(RelativePath)</c> will construct any intermediate part of a path using the specific separators
    /// for the currently running operating-system. By using <c>(WinRelativePath)</c> and <c>(UnixRelativePath)</c> other separators can be
    /// used intentionally. Casting with <c>(AbsolutePath)</c> ensures that the path is rooted as Windows/Unix/UNC-path. The operators
    /// <c>/</c> and <c>+</c> allow to append sub-directories.</p>
    /// <p>Resulting paths are automatically normalized if possible. So <c>C:\foo\..\bar\.</c> will become <c>C:\bar</c>.</p>
    /// </summary>
    /// <example>
    /// <code>
    /// string relativePath = (RelativePath) "foo" / "bar";
    /// // On Windows:   relativePath == "foo\bar"
    /// // On Unix:      relativePath == "foo/bar"
    /// </code>
    /// <code>
    /// string winPath = (WinPath) "foo" / "bar"; // "foo\bar";
    /// string unixPath = (UnixPath) "foo" / "bar";   // "foo/bar";
    /// </code>
    /// <code>
    /// string absolutePath1 = (AbsolutePath) RootDirectory / "foo" / "bar";
    /// string absolutePath2 = (AbsolutePath) "foo" / "bar"  // Throws exception
    /// </code>
    /// </example>
    [PublicAPI]
    public static class PathConstruction
    {
        internal static GlobbingCaseSensitivity GlobbingCaseSensitivity;

        private static GlobOptions GlobOptions
        {
            get
            {
                switch (GlobbingCaseSensitivity)
                {
                    case GlobbingCaseSensitivity.CaseSensitive:
                        return GlobOptions.None;
                    case GlobbingCaseSensitivity.CaseInsensitive:
                        return GlobOptions.CaseInsensitive;
                    default:
                        return EnvironmentInfo.IsWin ? GlobOptions.CaseInsensitive : GlobOptions.None;
                }
            }
        }

        // TODO: check usages
        [Pure]
        public static string GetRelativePath(string basePath, string destinationPath, bool normalize = true)
        {
            basePath = NormalizePath(basePath);
            destinationPath = NormalizePath(destinationPath);

            var separator = GetSeparator(basePath);
            ControlFlow.Assert(separator == GetSeparator(destinationPath), "Separators do not match.");
            ControlFlow.Assert(!IsWinRoot(basePath) || Path.GetPathRoot(basePath) == Path.GetPathRoot(destinationPath), "Root must be same.");

            var baseParts = basePath.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            var destinationParts = destinationPath.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);

            var sameParts = baseParts.Zip(destinationParts, (a, b) => new { Base = a, Destination = b })
                .Count(x => x.Base.EqualsOrdinalIgnoreCase(x.Destination));
            return Enumerable.Repeat("..", baseParts.Length - sameParts).ToList()
                .Concat(destinationParts.Skip(sameParts).ToList()).Join(separator);
        }

        [Pure]
        public static bool IsDescendantPath(string basePath, string destinationPath)
        {
            return NormalizePath(destinationPath).StartsWith(NormalizePath(basePath));
        }

        [Pure]
        public static IReadOnlyCollection<string> GlobFiles(string directory, params string[] patterns)
        {
            var directoryInfo = new DirectoryInfo(directory);
            return patterns.SelectMany(x => Glob.Files(directoryInfo, x, GlobOptions)).Select(x => x.FullName).ToList();
        }

        public static IReadOnlyCollection<AbsolutePath> GlobFiles(this AbsolutePath directory, params string[] patterns)
        {
            return GlobFiles((string) directory, patterns).Select(x => (AbsolutePath) x).ToList();
        }

        [Pure]
        public static IReadOnlyCollection<string> GlobDirectories(string directory, params string[] patterns)
        {
            var directoryInfo = new DirectoryInfo(directory);
            return patterns.SelectMany(x => Glob.Directories(directoryInfo, x, GlobOptions)).Select(x => x.FullName).ToList();
        }

        public static IReadOnlyCollection<AbsolutePath> GlobDirectories(this AbsolutePath directory, params string[] patterns)
        {
            return GlobDirectories((string) directory, patterns).Select(x => (AbsolutePath) x).ToList();
        }

        private const char WinSeparator = '\\';
        private const char UncSeparator = '\\';
        private const char UnixSeparator = '/';

        private static bool IsSameDirectory([CanBeNull] string pathPart)
            => pathPart?.Length == 1 &&
               pathPart[index: 0] == '.';

        private static bool IsUpwardsDirectory([CanBeNull] string pathPart)
            => pathPart?.Length == 2 &&
               pathPart[index: 0] == '.' &&
               pathPart[index: 1] == '.';

        private static bool IsWinRoot([CanBeNull] string root)
            => root?.Length == 2 &&
               char.IsLetter(root[index: 0]) &&
               root[index: 1] == ':';

        private static bool IsUnixRoot([CanBeNull] string root)
            => root?.Length == 1 &&
               root[index: 0] == UnixSeparator;

        private static bool IsUncRoot([CanBeNull] string root)
            => root?.Length >= 3 &&
               root[index: 0] == UncSeparator &&
               root[index: 1] == UncSeparator &&
               root.Skip(count: 2).All(char.IsLetterOrDigit);

        private static string GetHeadPart([CanBeNull] string str, int count) => new string((str ?? string.Empty).Take(count).ToArray());

        private static bool HasUnixRoot([CanBeNull] string path) => IsUnixRoot(GetHeadPart(path, count: 1));
        private static bool HasUncRoot([CanBeNull] string path) => IsUncRoot(GetHeadPart(path, count: 3));
        private static bool HasWinRoot([CanBeNull] string path) => IsWinRoot(GetHeadPart(path, count: 2));

        public static bool HasPathRoot([CanBeNull] string path) => GetPathRoot(path) != null;

        [CanBeNull]
        public static string GetPathRoot([CanBeNull] string path)
        {
            if (path == null)
                return null;

            if (HasUnixRoot(path))
                return GetHeadPart(path, count: 1);

            if (HasWinRoot(path))
                return GetHeadPart(path, count: 2);

            if (HasUncRoot(path))
            {
                var separatorIndex = path.IndexOf(UncSeparator, startIndex: 2);
                return separatorIndex == -1 ? path : GetHeadPart(path, separatorIndex);
            }

            return null;
        }

        public static string Combine([CanBeNull] string left, string right, char? separator = null)
        {
            // TODO: better something like "SafeHandleRoots"?
            left = Trim(left);
            right = Trim(right);

            ControlFlow.Assert(!HasPathRoot(right), "Second path must not be rooted.");

            if (string.IsNullOrWhiteSpace(left))
                return right;
            if (string.IsNullOrWhiteSpace(right))
                return !IsWinRoot(left) ? left : $@"{left}\";

            AssertSeparatorChoice(left, separator);
            separator = separator ?? GetSeparator(left);

            if (IsWinRoot(left))
                return $@"{left}\{right}";
            if (IsUnixRoot(left))
                return $"{left}{right}";
            if (IsUncRoot(left))
                return $@"{left}\{right}";

            return $"{left}{separator}{right}";
        }

        // ReSharper disable once CyclomaticComplexity
        public static string NormalizePath([CanBeNull] string path, char? separator = null)
        {
            AssertSeparatorChoice(path, separator);

            path = path ?? string.Empty;
            separator = separator ?? GetSeparator(path);
            var root = GetPathRoot(path);

            var tail = root == null ? path : path.Substring(root.Length);
            var tailParts = tail.Split(new[] { WinSeparator, UncSeparator, UnixSeparator }, StringSplitOptions.RemoveEmptyEntries).ToList();
            for (var i = 0; i < tailParts.Count;)
            {
                var part = tailParts[i];
                if (IsUpwardsDirectory(part))
                {
                    if (tailParts.Take(i).All(IsUpwardsDirectory))
                    {
                        ControlFlow.Assert(i > 0 || root == null, $"Cannot normalize '{path}' beyond path root.");
                        i++;
                        continue;
                    }

                    tailParts.RemoveAt(i);
                    tailParts.RemoveAt(i - 1);
                    i--;
                    continue;
                }

                if (IsSameDirectory(part))
                {
                    tailParts.RemoveAt(i);
                    continue;
                }

                i++;
            }

            return Combine(root, tailParts.Join(separator.ToString()), separator);
        }

        private static char GetSeparator([CanBeNull] string path)
        {
            var root = GetPathRoot(path);
            if (root != null)
            {
                if (IsWinRoot(root))
                    return WinSeparator;

                if (IsUncRoot(root))
                    return UncSeparator;

                if (IsUnixRoot(root))
                    return UnixSeparator;
            }

            return Path.DirectorySeparatorChar;
        }

        private static void AssertSeparatorChoice([CanBeNull] string path, char? separator)
        {
            if (separator == null)
                return;

            var root = GetPathRoot(path);
            if (root == null)
                return;

            ControlFlow.Assert(!IsWinRoot(root) || separator == WinSeparator, $"For Windows-rooted paths the separator must be '{WinSeparator}'.");
            ControlFlow.Assert(!IsUncRoot(root) || separator == UncSeparator, $"For UNC-rooted paths the separator must be '{UncSeparator}'.");
            ControlFlow.Assert(!IsUnixRoot(root) || separator == UnixSeparator, $"For Unix-rooted paths the separator must be '{UnixSeparator}'.");
        }

        [ContractAnnotation("null => null; notnull => notnull")]
        private static string Trim([CanBeNull] string path)
        {
            if (path == null)
                return null;

            return IsUnixRoot(path) // TODO: "//" ?
                ? path
                : path.TrimEnd(WinSeparator, UnixSeparator, UncSeparator);
        }

        [Serializable]
        [DebuggerDisplay("{" + nameof(_path) + "}")]
        public class RelativePath
        {
            private readonly string _path;
            private readonly char? _separator;

            protected RelativePath(string path, char? separator = null)
            {
                _path = path;
                _separator = separator;
            }

            public static explicit operator RelativePath([CanBeNull] string path)
            {
                if (path is null)
                    return null;

                return new RelativePath(NormalizePath(path));
            }

            public static implicit operator string([CanBeNull] RelativePath path)
            {
                return path?._path;
            }

            public static RelativePath operator /(RelativePath left, [CanBeNull] string right)
            {
                var separator = left.NotNull("left != null")._separator;
                return new RelativePath(NormalizePath(Combine(left, (RelativePath) right, separator), separator), separator);
            }

            public override string ToString()
            {
                return _path;
            }
        }

        [Serializable]
        public class UnixRelativePath : RelativePath
        {
            protected UnixRelativePath(string path, char? separator)
                : base(path, separator)
            {
            }

            public static explicit operator UnixRelativePath([CanBeNull] string path)
            {
                return new UnixRelativePath(NormalizePath(path, UnixSeparator), UnixSeparator);
            }
        }

        [Serializable]
        public class WinRelativePath : RelativePath
        {
            protected WinRelativePath(string path, char? separator)
                : base(path, separator)
            {
            }

            public static explicit operator WinRelativePath([CanBeNull] string path)
            {
                return new WinRelativePath(NormalizePath(path, WinSeparator), WinSeparator);
            }
        }

        [Serializable]
        [TypeConverter(typeof(TypeConverter))]
        [DebuggerDisplay("{" + nameof(_path) + "}")]
        public class AbsolutePath
        {
            public class TypeConverter : System.ComponentModel.TypeConverter
            {
                public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
                {
                    return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
                }

                public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
                {
                    if (value is string stringValue)
                    {
                        return (AbsolutePath) (HasPathRoot(stringValue)
                            ? stringValue
                            : Combine(EnvironmentInfo.WorkingDirectory, stringValue));
                    }

                    if (value is null)
                        return null;

                    return base.ConvertFrom(context, culture, value);
                }
            }

            private readonly string _path;

            private AbsolutePath(string path)
            {
                _path = NormalizePath(path);
            }

            public static explicit operator AbsolutePath([CanBeNull] string path)
            {
                if (path is null)
                    return null;

                ControlFlow.Assert(HasPathRoot(path), $"Path '{path}' must be rooted.");
                return new AbsolutePath(path);
            }

            public static implicit operator string([CanBeNull] AbsolutePath path)
            {
                return path?.ToString();
            }

            public AbsolutePath Parent =>
                !IsWinRoot(_path.TrimEnd(WinSeparator)) && !IsUncRoot(_path) && !IsUnixRoot(_path)
                    ? this / ".."
                    : null;

            public static AbsolutePath operator /(AbsolutePath left, [CanBeNull] string right)
            {
                return new AbsolutePath(Combine(left.NotNull("left != null"), right));
            }

            protected bool Equals(AbsolutePath other)
            {
                var stringComparison = HasWinRoot(_path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(_path, other._path, stringComparison);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != GetType())
                    return false;
                return Equals((AbsolutePath) obj);
            }

            public override int GetHashCode()
            {
                return _path?.GetHashCode() ?? 0;
            }

            public override string ToString()
            {
                return _path;
            }
        }
    }

    /// <summary>
    /// Allows to configure the case-sensitivity used for globbing operations in <see cref="PathConstruction"/>.
    /// </summary>
    [PublicAPI]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GlobbingOptionsAttribute : Attribute, IPreLogoBuildExtension
    {
        private readonly GlobbingCaseSensitivity _caseSensitivity;

        public GlobbingOptionsAttribute(GlobbingCaseSensitivity caseSensitivity)
        {
            _caseSensitivity = caseSensitivity;
        }

        public void PreLogo(NukeBuild build, IReadOnlyCollection<ExecutableTarget> executableTargets)
        {
            PathConstruction.GlobbingCaseSensitivity = _caseSensitivity;
        }
    }
}
