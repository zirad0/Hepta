using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace Hepta
{
    internal static class Engine
    {
        public static readonly string[] Images =
        {
            "png",
            "jpg",
            "bmp",
            "tiff",
            "gif"
        };
        public static readonly string[] Audio =
        {
            "mp3",
            "wav",
            "m4a",
            "flac",
            "wma"
        };
        public static readonly string[] Video =
        {
            "mp4",
            "wmv",
            "avi"
        };
        public static readonly string[] Targets =
        {
            "png",
            "jpg",
            "bmp",
            "tiff",
            "ico",
            "webp",
            "svg",
            "pdf",
            "gif-frames",
            "html",
            "vtt",
            "srt",
            "csv",
            "tsv",
            "xlsx",
            "json",
            "yaml",
            "txt",
            "mp3",
            "m4a",
            "wav",
            "flac",
            "wma",
            "mp4",
            "wmv",
            "avi",
            "media-join",
            "audio-join",
            "pdf-merge",
            "pdf-split",
            "zip",
            "tar",
            "7z",
            "gz",
            "extract",
            "txt-merge",
            "names",
            "paths"
        };
        public static string Ext(string path)
        {
            string e = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return e == "jpeg" ? "jpg" : e == "tif" ? "tiff" : e;
        }

        public static bool Accept(string path, string target)
        {
            string ext = Ext(path);
            if (target == "paths" || target == "names")
                return File.Exists(path) || Directory.Exists(path);
            if ((target == "tar" || target == "7z") && MoreArchives.Available)
                return File.Exists(path) || Directory.Exists(path);
            if (target == "zip")
                return File.Exists(path) || Directory.Exists(path);
            if (target == "extract")
                return File.Exists(path) && (ext == "zip" || ext == "gz" || (MoreArchives.Available && (ext == "tar" || ext == "7z" || ext == "rar")));
            if (!File.Exists(path))
                return false;
            if (target == "gz")
                return ext.Length > 0 && ext != "gz";
            if (target == "media-join")
                return Video.Contains(ext);
            if (target == "audio-join")
                return Audio.Contains(ext);
            if (Video.Contains(ext))
                return target != ext && (Video.Contains(target) || Audio.Contains(target));
            if (target == "pdf-merge")
                return Images.Contains(ext) || ext == "pdf" || ext == "svg" || PlatformImages.CanRead(ext);
            if (target == "pdf-split")
                return ext == "pdf";
            if (ext == "pdf")
                return target == "png" || target == "jpg" || target == "txt";
            if (ext == "json")
                return target == "yaml";
            if (ext == "yaml" || ext == "yml")
                return target == "json";
            if (Audio.Contains(ext))
                return target != ext && Audio.Contains(target);
            if (ext == "gif" && (target == "gif-frames" || target == "mp4"))
                return true;
            if (ext == "ico")
                return target == "png";
            if (ext == "webp" || ext == "avif")
                return PlatformImages.CanRead(ext) && target != ext && new[]
                {
                    "png",
                    "bmp",
                    "tiff",
                    "jpg",
                    "svg",
                    "ico",
                    "pdf",
                    "webp"
                }.Contains(target);
            if (target == "txt-merge")
                return ext == "txt";
            if (Images.Contains(ext))
                return target != ext && new[]
                {
                    "png",
                    "jpg",
                    "tiff",
                    "bmp",
                    "svg",
                    "pdf",
                    "ico",
                    "webp"
                }.Contains(target);
            if (ext == "svg")
                return target == "png" || target == "jpg" || target == "pdf";
            if (ext == "txt" || ext == "md" || ext == "markdown")
                return target == "html" || target == "pdf";
            if (ext == "srt")
                return target == "vtt";
            if (ext == "vtt")
                return target == "srt";
            if (ext == "xls" || ext == "xlsx" || ext == "ods")
                return target == "csv" || target == "tsv";
            return (ext == "csv" || ext == "tsv") && target != ext && new[]
            {
                "csv",
                "tsv",
                "xlsx",
                "json"
            }.Contains(target);
        }

        public static string Output(string path, string target, Action<string> write, CancellationToken token)
        {
            string directory = OutputOptions.Parent(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string tmp = Path.Combine(directory, ".hepta-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            try
            {
                token.ThrowIfCancellationRequested();
                write(tmp);
                OutputOptions.CheckInputs();
                if (new FileInfo(tmp).Length == 0)
                    throw new InvalidDataException("Empty output");
                for (int i = 0;; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string final = Path.Combine(directory, stem + (i == 0 ? "" : " (" + i + ")") + "." + target);
                    try
                    {
                        File.Move(tmp, final);
                        return final;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(final) && !Directory.Exists(final))
                            throw;
                    }
                }
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }

        internal static string ReadText(string p)
        {
            if (new FileInfo(p).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Text > 16 MiB");
            using (var reader = new StreamReader(p, new UTF8Encoding(false, true), true))
                return reader.ReadToEnd();
        }

        public static string Run(string[] files, string target, int quality, CancellationToken token, MediaOptions options = null, IProgress<double> progress = null)
        {
            if (files.Length == 0 || files.Any(p => !Accept(p, target)))
                throw new InvalidDataException("Unsupported selection");
            if (target == "media-join" || target == "audio-join")
            {
                if (files.Length < 2)
                    throw new InvalidDataException("Minimum 2");
                bool video = target == "media-join";
                return Output(files[0], video ? "mp4" : "m4a", p => NativeMedia.Join(files, p, video, token).GetAwaiter().GetResult(), token);
            }

            if (target == "pdf-merge")
                return Documents.Merge(files, quality, token);
            if (Ext(files[0]) == "pdf" && (target == "png" || target == "jpg"))
                return Documents.RenderAll(files[0], target, token);
            if (target == "gif-frames")
                return ImageTools.Frames(files[0], token);
            if (target == "mp4" && Ext(files[0]) == "gif")
                return Output(files[0], target, p => ImageTools.Movie(files[0], p, token).GetAwaiter().GetResult(), token);
            if (Video.Contains(target) || Audio.Contains(target))
                return Output(files[0], target, p => NativeMedia.Transcode(files[0], p, target, token, options, progress).GetAwaiter().GetResult(), token);
            if (target == "zip")
                return Archives.Pack(files, token);
            if (target == "tar" || target == "7z")
                return MoreArchives.Pack(files, target, token);
            if (target == "gz")
                return MoreArchives.Gzip(files[0], true, token);
            if (target == "extract" && files.Length == 1)
            {
                var ext = Ext(files[0]);
                return ext == "zip" ? Archives.Extract(files[0], token) : ext == "gz" ? MoreArchives.Gzip(files[0], false, token) : MoreArchives.Extract(files[0], token);
            }

            if (target == "txt-merge")
            {
                if (files.Length < 2)
                    throw new ArgumentException("Minimum 2");
                return Output(files[0], "txt", p =>
                {
                    using (var w = new StreamWriter(p))
                    {
                        foreach (var f in files)
                        {
                            token.ThrowIfCancellationRequested();
                            w.WriteLine(ReadText(f));
                        }
                    }
                }, token);
            }

            if (files.Length != 1)
                throw new ArgumentException("Single input");
            string file = files[0];
            return Output(file, target, p =>
            {
                if (Ext(file) == "webp" || Ext(file) == "avif")
                    PlatformImages.Convert(file, p, target, quality, token);
                else if (Ext(file) == "ico")
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        var largest = decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).First();
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(largest);
                        using (var outFile = File.Create(p))
                            enc.Save(outFile);
                    }
                }
                else if (target == "txt")
                    File.WriteAllText(p, PdfText.Read(file, token));
                else if (target == "yaml" || Ext(file) == "yaml" || Ext(file) == "yml")
                    File.WriteAllText(p, DataText.Transform(ReadText(file), target == "yaml"));
                else if (Ext(file) == "svg")
                    Svg.Render(file, p, target, quality, token);
                else if (Images.Contains(Ext(file)))
                {
                    using (var img = Image.FromFile(file, true))
                    {
                        if ((long)img.Width * img.Height > 32000000 || img.FrameDimensionsList.Any(d => img.GetFrameCount(new FrameDimension(d)) != 1))
                            throw new InvalidDataException("Multi-frame or oversized image");
                        if (target == "webp")
                        {
                            Webp.Save(img, p, token);
                            return;
                        }

                        if (target == "ico")
                        {
                            ImageTools.Icon(img, p);
                            return;
                        }

                        if (target == "svg")
                        {
                            Tracing.Write(img, p, quality, token);
                            return;
                        }

                        if (target == "pdf")
                        {
                            Pdf(img, p, quality);
                            return;
                        }

                        if (target == "jpg")
                        {
                            using (var stream = File.Create(p))
                                Jpeg(img, stream, quality);
                        }
                        else
                            img.Save(p, target == "png" ? ImageFormat.Png : target == "bmp" ? ImageFormat.Bmp : ImageFormat.Tiff);
                        using (var check = Image.FromFile(p))
                        {
                            if (check.Width != img.Width || check.Height != img.Height)
                                throw new InvalidDataException();
                        }
                    }
                }
                else if (target == "pdf")
                    TextDocuments.Pdf(ReadText(file), p, Ext(file) != "txt", token);
                else if (target == "html" && Ext(file) != "txt")
                    File.WriteAllText(p, TextDocuments.Markdown(ReadText(file)));
                else if (target == "html")
                    File.WriteAllText(p, "<!doctype html><meta charset=utf-8><pre>" + System.Net.WebUtility.HtmlEncode(ReadText(file)) + "</pre>");
                else if (target == "srt" || target == "vtt")
                    File.WriteAllText(p, Subtitle(ReadText(file), target));
                else
                    Table(file, p, target, token);
            }, token);
        }

        internal static void Jpeg(Image img, Stream stream, int quality)
        {
            using (var flat = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(flat))
            using (var parameters = new EncoderParameters(1))
            {
                g.Clear(Color.White);
                g.DrawImageUnscaled(img, 0, 0);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(1, Math.Min(100, quality)));
                flat.Save(stream, ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/jpeg"), parameters);
            }
        }

        internal static void Pdf(Image img, string p, int quality)
        {
            using (var data = new MemoryStream())
            using (var s = File.Create(p))
            {
                Jpeg(img, data, quality);
                Action<string> put = t =>
                {
                    var b = Encoding.ASCII.GetBytes(t);
                    s.Write(b, 0, b.Length);
                };
                var offsets = new List<long>();
                Action<int> obj = n =>
                {
                    offsets.Add(s.Position);
                    put(n + " 0 obj\n");
                };
                put("%PDF-1.4\n");
                obj(1);
                put("<</Type/Catalog/Pages 2 0 R>>endobj\n");
                obj(2);
                put("<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
                string size = img.Width + " " + img.Height;
                obj(3);
                put("<</Type/Page/Parent 2 0 R/MediaBox[0 0 " + size + "]/Resources<</XObject<</Im 4 0 R>>>>/Contents 5 0 R>>endobj\n");
                obj(4);
                put("<</Type/XObject/Subtype/Image/Width " + img.Width + "/Height " + img.Height + "/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length " + data.Length + ">>stream\n");
                data.WriteTo(s);
                put("\nendstream\nendobj\n");
                string command = "q " + img.Width + " 0 0 " + img.Height + " 0 0 cm /Im Do Q\n";
                obj(5);
                put("<</Length " + command.Length + ">>stream\n" + command + "endstream\nendobj\n");
                long xref = s.Position;
                put("xref\n0 6\n0000000000 65535 f \n");
                foreach (var pos in offsets)
                    put(pos.ToString("D10") + " 00000 n \n");
                put("trailer<</Size 6/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }

        static string Subtitle(string content, string target)
        {
            string text = content.Replace("\r\n", "\n").Trim();
            if (target == "srt")
            {
                if (!text.StartsWith("WEBVTT\n", StringComparison.Ordinal))
                    throw new InvalidDataException("WEBVTT");
                text = text.Substring(7).TrimStart();
            }

            var blocks = Regex.Split(text, @"\n[ \t]*\n");
            var result = new StringBuilder(target == "vtt" ? "WEBVTT\n\n" : "");
            int index = 0;
            foreach (string block in blocks)
            {
                var lines = block.Split('\n');
                int at = Array.FindIndex(lines, l => l.Contains(" --> "));
                if (at < 0 || at > 1 || at == lines.Length - 1 || !Regex.IsMatch(lines[at], @"^\d{2,}:[0-5]\d:[0-5]\d[.,]\d{3} --> \d{2,}:[0-5]\d:[0-5]\d[.,]\d{3}$"))
                    throw new InvalidDataException("Unsupported cue");
                if (target == "srt")
                    result.AppendLine((++index).ToString());
                result.AppendLine(target == "vtt" ? lines[at].Replace(',', '.') : lines[at].Replace('.', ','));
                foreach (string line in lines.Skip(at + 1))
                    result.AppendLine(line);
                result.AppendLine();
            }

            return result.ToString();
        }

        static void Table(string file, string output, string target, CancellationToken token)
        {
            var records = new List<string[]>();
            if (Ext(file) == "xls")
                records = LegacyWorkbook.Read(file, token);
            else if (Ext(file) == "xlsx" || Ext(file) == "ods")
                records = Workbook.Read(file, token);
            else
                records = Delimited.Read(ReadText(file), Ext(file) == "tsv" ? (char)9 : (char)44, token);
            if (target == "xlsx")
            {
                Workbook.Save(output, records, token);
                return;
            }

            if (target == "json")
            {
                File.WriteAllText(output, new JavaScriptSerializer { MaxJsonLength = 64000000 }.Serialize(records));
                return;
            }

            using (var w = new StreamWriter(output, false, new UTF8Encoding(true)))
            {
                foreach (var row in records)
                {
                    if (row.Any(c => Regex.IsMatch(c, @"^\s*[=+\-@]") || c.StartsWith("\t") || c.StartsWith("\r")))
                        throw new InvalidDataException("Spreadsheet formula-like cell");
                    w.WriteLine(string.Join(target == "tsv" ? "\t" : ",", row.Select(c => "\"" + c.Replace("\"", "\"\"") + "\"")));
                }
            }
        }
    }
}
