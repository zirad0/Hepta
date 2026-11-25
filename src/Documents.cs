using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Drawing;

namespace Hepta
{
    internal static class Documents
    {
        public static int[] Cuts(string input, int pages)
        {
            int[] cuts = input.Split(',').Select(s =>
            {
                int n;
                if (!int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1 || n >= pages)
                    throw new ArgumentException("1 … " + (pages - 1));
                return n;
            }).Distinct().OrderBy(n => n).ToArray();
            return cuts;
        }

        public static string Folder(string input, Action<string> produce, CancellationToken token)
        {
            string parent = OutputOptions.Parent(input), name = Path.GetFileNameWithoutExtension(input);
            string temp = Path.Combine(parent, ".hepta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                produce(temp);
                OutputOptions.CheckInputs();
                token.ThrowIfCancellationRequested();
                for (int n = 0;; n++)
                {
                    string dest = Path.Combine(parent, name + (n == 0 ? "" : " (" + n + ")"));
                    try
                    {
                        Directory.Move(temp, dest);
                        return dest;
                    }
                    catch (IOException)
                    {
                        if (!Directory.Exists(dest) && !File.Exists(dest))
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

        public static string RenderAll(string file, string ext, CancellationToken token)
        {
            return PdfPages.RenderAll(file, ext, token);
        }

        public static string Merge(string[] files, int quality, CancellationToken token)
        {
            if (files.Length < 2)
                throw new InvalidDataException("Minimum 2");
            return Engine.Output(files[0], "pdf", outFile => WriteMerged(files, outFile, quality, token), token);
        }

        internal static void WriteMerged(string[] files, string outFile, int quality, CancellationToken token)
        {
            PdfPages.Merge(files, outFile, quality, token);
        }

        public static string Split(string file, string points, int quality, CancellationToken token)
        {
            return PdfPages.Split(file, points, token);
        }
    }
}
