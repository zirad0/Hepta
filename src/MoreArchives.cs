using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hepta
{
    internal static class MoreArchives
    {
        const string Lib = "archiveint.dll";
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_read_new();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_free(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_support_format_tar(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_support_format_7zip(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int archive_read_open_filename_w(IntPtr a, string filename, UIntPtr block);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_next_header(IntPtr a, out IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_entry_pathname_w(IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern uint archive_entry_filetype(IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_entry_hardlink_w(IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_entry_symlink_w(IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern long archive_entry_size(IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_read_data(IntPtr a, [Out] byte[] buffer, UIntPtr size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_support_format_rar(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_read_support_format_rar5(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_write_new();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_write_set_format_7zip(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_write_set_format_pax_restricted(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int archive_write_open_filename_w(IntPtr a, string path);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_write_header(IntPtr a, IntPtr entry);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_write_data(IntPtr a, byte[] buf, UIntPtr size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_write_close(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern int archive_write_free(IntPtr a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr archive_entry_new();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern void archive_entry_free(IntPtr e);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern void archive_entry_copy_pathname_w(IntPtr e, string path);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern void archive_entry_set_size(IntPtr e, long size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern void archive_entry_set_filetype(IntPtr e, uint mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern void archive_entry_set_perm(IntPtr e, int perm);
        public static string Pack(string[] files, string ext, CancellationToken token)
        {
            return Engine.Output(files[0], ext, output =>
            {
                var arc = archive_write_new();
                if (arc == IntPtr.Zero)
                    throw new OutOfMemoryException();
                try
                {
                    if ((ext == "tar" ? archive_write_set_format_pax_restricted(arc) : archive_write_set_format_7zip(arc)) != 0 || archive_write_open_filename_w(arc, output) != 0)
                        throw new InvalidDataException("Archive writer");
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    long total = 0;
                    var todo = new Stack<Tuple<string, string>>();
                    foreach (string path in files)
                        todo.Push(Tuple.Create(path, Path.GetFileName(path)));
                    var buf = new byte[65536];
                    while (todo.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = todo.Pop();
                        string path = item.Item1, name = item.Item2;
                        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (seen.Count >= 10000 || !seen.Add(name) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("Duplicate or unsupported archive entry");
                        bool dir = Directory.Exists(path);
                        var entry = archive_entry_new();
                        if (entry == IntPtr.Zero)
                            throw new OutOfMemoryException();
                        try
                        {
                            archive_entry_copy_pathname_w(entry, name);
                            archive_entry_set_filetype(entry, dir ? 16384u : 32768u);
                            archive_entry_set_perm(entry, dir ? 493 : 420);
                            if (dir)
                            {
                                archive_entry_set_size(entry, 0);
                                if (archive_write_header(arc, entry) != 0)
                                    throw new IOException("Archive directory");
                                foreach (var f in Directory.EnumerateFileSystemEntries(path))
                                {
                                    if (todo.Count + seen.Count >= 10000)
                                        throw new InvalidDataException("Archive entry limit");
                                    todo.Push(Tuple.Create(f, name + "/" + Path.GetFileName(f)));
                                }
                            }
                            else
                                using (var input = File.OpenRead(path))
                                {
                                    total += input.Length;
                                    if (total > 1073741824)
                                        throw new InvalidDataException("Archive size limit");
                                    archive_entry_set_size(entry, input.Length);
                                    if (archive_write_header(arc, entry) != 0)
                                        throw new IOException("Archive header");
                                    int n;
                                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        if (archive_write_data(arc, buf, new UIntPtr((uint)n)).ToInt64() != n)
                                            throw new IOException("Archive write");
                                    }
                                }
                        }
                        finally
                        {
                            archive_entry_free(entry);
                        }
                    }

                    if (archive_write_close(arc) != 0)
                        throw new IOException("Archive finalization");
                }
                finally
                {
                    archive_write_free(arc);
                }
            }, token);
        }

        public static bool Available
        {
            get
            {
                return Environment.OSVersion.Platform == PlatformID.Win32NT && File.Exists(Path.Combine(Environment.SystemDirectory, Lib));
            }
        }

        public static string Gzip(string input, bool pack, CancellationToken token)
        {
            string ext = Path.GetExtension(Path.GetFileNameWithoutExtension(input)).TrimStart('.');
            if (ext.Length == 0)
                ext = "bin";
            return Engine.Output(input, pack ? Engine.Ext(input) + ".gz" : ext, output =>
            {
                using (var src = File.OpenRead(input))
                using (var dest = File.Create(output))
                using (var gz = pack ? new GZipStream(dest, CompressionMode.Compress) : new GZipStream(src, CompressionMode.Decompress))
                {
                    Stream from = pack ? (Stream)src : gz, to = pack ? (Stream)gz : dest;
                    var buffer = new byte[65536];
                    int n;
                    long total = 0;
                    while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        total += n;
                        if (total > 1073741824 || (!pack && total > Math.Max(1048576L, src.Length * 200)))
                            throw new InvalidDataException("GZ limit");
                        to.Write(buffer, 0, n);
                    }
                }
            }, token);
        }

        public static string Extract(string input, CancellationToken token)
        {
            return Documents.Folder(input, dir =>
            {
                var a = archive_read_new();
                if (a == IntPtr.Zero)
                    throw new OutOfMemoryException();
                try
                {
                    string ext = Engine.Ext(input);
                    int supported = ext == "tar" ? archive_read_support_format_tar(a) : ext == "rar" ? Math.Min(archive_read_support_format_rar(a), archive_read_support_format_rar5(a)) : archive_read_support_format_7zip(a);
                    if (supported != 0 || archive_read_open_filename_w(a, input, new UIntPtr(65536)) != 0)
                        throw new InvalidDataException("Archive format");
                    long total = 0, limit = Math.Min(1073741824, Math.Max(1048576L, new FileInfo(input).Length * 200));
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var buffer = new byte[65536];
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        IntPtr entry;
                        int status = archive_read_next_header(a, out entry);
                        if (status == 1)
                            break;
                        if (status != 0)
                            throw new InvalidDataException("Archive header");
                        string name = Marshal.PtrToStringUni(archive_entry_pathname_w(entry));
                        uint type = archive_entry_filetype(entry);
                        bool folder = type == 16384;
                        if (name == null || name.Length > 1024 || (!folder && type != 32768) || archive_entry_hardlink_w(entry) != IntPtr.Zero || archive_entry_symlink_w(entry) != IntPtr.Zero)
                            throw new InvalidDataException("Unsupported archive entry");
                        name = folder ? name.TrimEnd('/') : name;
                        if (name.StartsWith("./"))
                            name = name.Substring(2);
                        if (folder && name == ".")
                            continue;
                        if (seen.Count >= 10000 || !seen.Add(name) || name.Contains("\\"))
                            throw new InvalidDataException("Archive path");
                        string outPath = dir;
                        foreach (string part in name.Split('/'))
                        {
                            if (part.Length == 0 || part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)", RegexOptions.IgnoreCase))
                                throw new InvalidDataException("Archive path");
                            outPath = Path.Combine(outPath, part);
                            if (Directory.Exists(outPath) && (File.GetAttributes(outPath) & FileAttributes.ReparsePoint) != 0)
                                throw new InvalidDataException("Reparse point");
                        }

                        if (folder)
                        {
                            Directory.CreateDirectory(outPath);
                            continue;
                        }

                        if (archive_entry_size(entry) > limit - total)
                            throw new InvalidDataException("Archive size limit");
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                        using (var f = new FileStream(outPath, FileMode.CreateNew))
                        {
                            while (true)
                            {
                                token.ThrowIfCancellationRequested();
                                long n = archive_read_data(a, buffer, new UIntPtr((uint)buffer.Length)).ToInt64();
                                if (n == 0)
                                    break;
                                if (n < 0 || n > buffer.Length || (total += n) > limit)
                                    throw new InvalidDataException("Archive data limit");
                                f.Write(buffer, 0, (int)n);
                            }
                        }
                    }
                }
                finally
                {
                    archive_read_free(a);
                }
            }, token);
        }
    }
}
