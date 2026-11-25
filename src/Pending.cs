using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hepta
{
    internal class Pending
    {
        public string Id, Destination, Extension, Hash;
        public string[] Included, Failures;
        public string Stage()
        {
            if (!Regex.IsMatch(Id ?? "", @"^[0-9a-f]{32}$") || !new[]
            {
                "pdf",
                "txt",
                "mp4",
                "m4a"
            }.Contains(Extension))
                throw new InvalidDataException("Pending job identity");
            return Path.Combine(Preferences.DirectoryPath, "Pending", Id, "output." + Extension);
        }

        static string Digest(string file)
        {
            using (var s = File.OpenRead(file))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(s));
        }

        public string Keep()
        {
            string source = Stage();
            if (Digest(source) != Hash)
                throw new InvalidDataException("Pending output changed");
            string output = Engine.Output(Destination, Extension, p => File.Copy(source, p, true), CancellationToken.None);
            File.Delete(source);
            return output;
        }

        public void Discard()
        {
            string source = Stage();
            if (Digest(source) != Hash)
                throw new InvalidDataException("Pending output changed");
            File.Delete(source);
        }

        public static bool IsMerge(string id)
        {
            return id == "pdf-merge" || id == "audio-join" || id == "media-join" || id == "txt-merge";
        }

        public static string Run(string[] files, string id, int quality, string outputBase, CancellationToken token, out Pending pending)
        {
            pending = null;
            if (files.Length < 2)
                throw new InvalidDataException("Minimum 2");
            var good = new List<string>();
            var bad = new List<string>();
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!Engine.Accept(file, id))
                        throw new InvalidDataException("Input format");
                    if (id == "txt-merge")
                        Engine.ReadText(file);
                    else if (id == "pdf-merge")
                    {
                        PdfPages.Validate(file, quality);
                    }
                    else
                    {
                        var sf = NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(file), token).GetAwaiter().GetResult();
                        if (id == "audio-join")
                            NativeMedia.Result(Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(sf), token).GetAwaiter().GetResult();
                        else
                            NativeMedia.Result(Windows.Media.Editing.MediaClip.CreateFromFileAsync(sf), token).GetAwaiter().GetResult();
                    }

                    good.Add(file);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    bad.Add(Path.GetFileName(file) + " · " + ex.Message);
                }
            }

            if (good.Count == 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, bad));
            string ext = id == "pdf-merge" ? "pdf" : id == "txt-merge" ? "txt" : id == "audio-join" ? "m4a" : "mp4";
            string destination = Path.Combine(OutputOptions.Parent(files[0]), outputBase + "." + ext);
            Action<string> produce = p =>
            {
                if (id == "pdf-merge")
                    Documents.WriteMerged(good.ToArray(), p, quality, token);
                else if (id == "txt-merge")
                {
                    using (var w = new StreamWriter(p))
                        foreach (var f in good)
                        {
                            token.ThrowIfCancellationRequested();
                            w.WriteLine(Engine.ReadText(f));
                        }
                }
                else
                    NativeMedia.Join(good.ToArray(), p, id == "media-join", token).GetAwaiter().GetResult();
            };
            if (bad.Count == 0)
                return Engine.Output(destination, ext, produce, token);
            var record = new Pending
            {
                Id = Guid.NewGuid().ToString("N"),
                Extension = ext,
                Destination = destination,
                Included = good.Select(Path.GetFileName).ToArray(),
                Failures = bad.ToArray()
            };
            string stage = record.Stage();
            Directory.CreateDirectory(Path.GetDirectoryName(stage));
            try
            {
                produce(stage);
                OutputOptions.CheckInputs();
                token.ThrowIfCancellationRequested();
                record.Hash = Digest(stage);
                pending = record;
                return null;
            }
            catch
            {
                if (File.Exists(stage))
                    File.Delete(stage);
                throw;
            }
        }
    }
}
