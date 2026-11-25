using System;
using System.IO;
using System.Threading;

namespace Hepta
{
    public class OutputOptions
    {
        public string DirectoryName = "";
        public int PdfDpi = 96, ZipLevel = 2;
        public OutputOptions Copy()
        {
            return (OutputOptions)MemberwiseClone();
        }

        public bool Valid()
        {
            return DirectoryName != null && (DirectoryName.Length == 0 || (Path.IsPathRooted(DirectoryName) && !DirectoryName.StartsWith(@"\\") && DirectoryName.IndexOfAny(Path.GetInvalidPathChars()) < 0)) && PdfDpi >= 72 && PdfDpi <= 300 && ZipLevel >= 0 && ZipLevel <= 2;
        }

        static readonly AsyncLocal<OutputOptions> Active = new AsyncLocal<OutputOptions>();
        public static OutputOptions Current
        {
            get
            {
                return Active.Value ?? new OutputOptions();
            }
        }

        public static T With<T>(OutputOptions value, Func<T> fn)
        {
            var previous = Active.Value;
            try
            {
                Active.Value = value;
                if (!Current.Valid())
                    throw new InvalidDataException("Output settings");
                return fn();
            }
            finally
            {
                Active.Value = previous;
            }
        }

        static readonly AsyncLocal<Action> VerifyInput = new AsyncLocal<Action>();
        public static T Watch<T>(string[] paths, Func<T> work)
        {
            var snapshots = new System.Collections.Generic.List<Tuple<string, long, DateTime>>();
            foreach (string path in paths)
            {
                var info = new FileInfo(path);
                if (info.Exists)
                    snapshots.Add(Tuple.Create(path, info.Length, info.LastWriteTimeUtc));
            }

            var previous = VerifyInput.Value;
            try
            {
                VerifyInput.Value = () =>
                {
                    foreach (var before in snapshots)
                    {
                        var now = new FileInfo(before.Item1);
                        if (!now.Exists || now.Length != before.Item2 || now.LastWriteTimeUtc != before.Item3)
                            throw new IOException("Source changed during processing: " + Path.GetFileName(before.Item1));
                    }
                };
                return work();
            }
            finally
            {
                VerifyInput.Value = previous;
            }
        }

        public static void CheckInputs()
        {
            var action = VerifyInput.Value;
            if (action != null)
                action();
        }

        public static string Parent(string file)
        {
            var path = Current.DirectoryName;
            if (path.Length == 0)
                return Path.GetDirectoryName(Path.GetFullPath(file));
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);
            return path;
        }
    }
}
