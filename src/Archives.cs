using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;

namespace Hepta
{
    internal static class Archives
    {
        const long Limit = 1024L * 1024 * 1024;
        static void NoLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive links are unsupported");
        }

        static void Copy(Stream input, Stream output, ref long total, CancellationToken token)
        {
            var buffer = new byte[65536];
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                total += n;
                if (total > Limit)
                    throw new InvalidDataException("Archive exceeds 1 GiB");
                output.Write(buffer, 0, n);
            }
        }

        public static string Pack(string[] files, CancellationToken token)
        {
            return Engine.Output(files[0], "zip", temp =>
            {
                long total = 0;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var todo = new Stack<Tuple<string, string>>(files.Reverse().Select(p => Tuple.Create(p, Path.GetFileName(p))));
                using (var output = File.OpenWrite(temp))
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    while (todo.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = todo.Pop();
                        string path = item.Item1, name = item.Item2;
                        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                            continue;
                        NoLink(path);
                        if (names.Count >= 10000 || !names.Add(name))
                            throw new InvalidDataException("Duplicate entry or archive entry limit");
                        if (Directory.Exists(path))
                        {
                            zip.CreateEntry(name + "/");
                            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                            {
                                if (todo.Count + names.Count >= 10000)
                                    throw new InvalidDataException("Archive entry limit");
                                todo.Push(Tuple.Create(child, name + "/" + Path.GetFileName(child)));
                            }
                        }
                        else
                        {
                            using (var input = File.OpenRead(path))
                            using (var entry = zip.CreateEntry(name, OutputOptions.Current.ZipLevel == 0 ? CompressionLevel.NoCompression : OutputOptions.Current.ZipLevel == 1 ? CompressionLevel.Fastest : CompressionLevel.Optimal).Open())
                                Copy(input, entry, ref total, token);
                        }
                    }
                }
            }, token);
        }

        public static string Extract(string input, CancellationToken token)
        {
            string parent = OutputOptions.Parent(input), stem = Path.GetFileNameWithoutExtension(input);
            string temp = Path.Combine(parent, ".hepta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                using (var zip = ZipFile.OpenRead(input))
                {
                    if (zip.Entries.Count > 10000)
                        throw new InvalidDataException("Archive entry limit");
                    foreach (var e in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        bool dir = e.FullName.EndsWith("/");
                        string name = dir ? e.FullName.Substring(0, e.FullName.Length - 1) : e.FullName;
                        if (name.Length == 0 || name.Contains("\\") || name.Length > 1024 || !seen.Add(name) || ((e.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (e.ExternalAttributes & 1024) != 0)
                            throw new InvalidDataException("Unsafe archive entry");
                        var parts = name.Split('/');
                        if (parts.Any(s => s.Length == 0 || s.TrimEnd(' ', '.') != s || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)", RegexOptions.IgnoreCase)))
                            throw new InvalidDataException("Unsafe archive path");
                        if (e.Length > Limit - total || (e.Length > 1048576 && e.Length / Math.Max(1, e.CompressedLength) > 200))
                            throw new InvalidDataException("Archive size limit");
                        string path = temp;
                        for (int i = 0; i < parts.Length - 1; i++)
                        {
                            path = Path.Combine(path, parts[i]);
                            Directory.CreateDirectory(path);
                            NoLink(path);
                        }

                        path = Path.Combine(path, parts.Last());
                        if (dir)
                        {
                            Directory.CreateDirectory(path);
                            NoLink(path);
                            continue;
                        }

                        using (var target = new FileStream(path, FileMode.CreateNew))
                        using (var src = e.Open())
                            Copy(src, target, ref total, token);
                    }
                }

                token.ThrowIfCancellationRequested();
                OutputOptions.CheckInputs();
                for (int i = 0;; i++)
                {
                    string output = Path.Combine(parent, stem + (i == 0 ? "" : " (" + i + ")"));
                    try
                    {
                        Directory.Move(temp, output);
                        return output;
                    }
                    catch (IOException)
                    {
                        if (!Directory.Exists(output) && !File.Exists(output))
                            throw;
                    }
                }
            }
            finally
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }
    }
}
