using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Hepta
{
    internal static class Tests
    {
        static int pass, fail, skipped;
        static string root;
        static string P(string name)
        {
            return Path.Combine(root, name);
        }

        static void Test(string id, Action action)
        {
            try
            {
                action();
                pass++;
                Console.WriteLine("PASS " + id);
            }
            catch (UnauthorizedAccessException e)
            {
                if (id != "global shortcut collision")
                    throw;
                skipped++;
                Console.WriteLine("SKIP " + id + " " + e.Message);
            }
            catch (Exception e)
            {
                fail++;
                Console.WriteLine("FAIL " + id + " " + e);
            }
        }

        static void Require(bool ok)
        {
            if (!ok)
                throw new Exception("assertion");
        }

        static void Reject(Action a)
        {
            try
            {
                a();
            }
            catch
            {
                return;
            }

            throw new Exception("Accepted invalid input");
        }

        static string Convert(string p, string t)
        {
            return Engine.Run(new[] { p }, t, 90, CancellationToken.None, new MediaOptions());
        }

        static void CheckPdf(string path)
        {
            var text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));
            Require(text.StartsWith("%PDF-1.4") && text.TrimEnd().EndsWith("%%EOF"));
            var match = System.Text.RegularExpressions.Regex.Match(text, @"startxref\n(\d+)");
            int off = int.Parse(match.Groups[1].Value);
            Require(text.Substring(off).StartsWith("xref"));
            int i = text.IndexOf("stream\n", StringComparison.Ordinal) + 7;
            var b = File.ReadAllBytes(path);
            using (var s = new MemoryStream(b, i, b.Length - i))
            using (var img = Image.FromStream(s))
                Require(img.Width == 12 && img.Height == 8);
        }

        [STAThread]
        public static int Main()
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            root = Path.Combine(Path.GetTempPath(), "hepta-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Preferences.DirectoryPath = P("test-appdata");
                var fixture = new Dictionary<string, string>();
                foreach (string ext in Engine.Images)
                {
                    string input = P("Şekil " + ext + "." + ext);
                    using (var b = new Bitmap(12, 8))
                    using (var g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Firebrick);
                        g.FillRectangle(Brushes.Aqua, 1, 1, 3, 3);
                        b.Save(input, ext == "jpg" ? ImageFormat.Jpeg : ext == "bmp" ? ImageFormat.Bmp : ext == "gif" ? ImageFormat.Gif : ext == "tiff" ? ImageFormat.Tiff : ImageFormat.Png);
                    }

                    fixture[ext] = input;
                    foreach (string t in Engine.Targets.Where(t => Engine.Accept(input, t) && new[] { "png", "jpg", "bmp", "tiff", "svg", "pdf" }.Contains(t)))
                    {
                        Test(ext + "->" + t, () =>
                        {
                            string output = Convert(input, t);
                            if (t == "svg")
                            {
                                var xml = XDocument.Load(output);
                                Require(xml.Descendants().Any(e => e.Name.LocalName == "path") && !xml.Descendants().Any(e => e.Name.LocalName == "image"));
                                var rendered = Convert(output, "png");
                                using (var b = new Bitmap(rendered))
                                    Require(b.Width == 12 && b.Height == 8);
                            }
                            else if (t == "pdf")
                                CheckPdf(output);
                            else
                                using (var b = new Bitmap(output))
                                {
                                    Require(b.Width == 12 && b.Height == 8);
                                    Require(b.GetPixel(10, 7).R > b.GetPixel(10, 7).G);
                                    Require(b.RawFormat.Guid == (t == "png" ? ImageFormat.Png : t == "jpg" ? ImageFormat.Jpeg : t == "bmp" ? ImageFormat.Bmp : ImageFormat.Tiff).Guid);
                                }
                        });
                    }
                }

                string svg = P("input.svg");
                File.WriteAllText(svg, "<svg xmlns='http://www.w3.org/2000/svg' width='12' height='8'><rect width='12' height='8' fill='#dd0000'/></svg>");
                foreach (string target in new[]
                {
                    "png",
                    "jpg",
                    "pdf"
                }

                )
                    Test("svg->" + target, () =>
                    {
                        string o = Convert(svg, target);
                        if (target == "pdf")
                            CheckPdf(o);
                        else
                            using (var b = new Bitmap(o))
                                Require(b.GetPixel(0, 0).R > 180 && b.Width == 12);
                    });
                Test("svg active content rejection", () =>
                {
                    File.WriteAllText(svg, "<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>");
                    Reject(() => Convert(svg, "png"));
                });
                Test("svg DTD rejection", () =>
                {
                    File.WriteAllText(svg, "<!DOCTYPE svg [<!ENTITY x 'hi'>]><svg/>");
                    Reject(() => Convert(svg, "png"));
                });
                var txt = P("İçerik.txt");
                File.WriteAllText(txt, "Örnek <tag>");
                Test("txt->html", () => Require(System.Net.WebUtility.HtmlDecode(File.ReadAllText(Convert(txt, "html")).Split(new[] { "<pre>" }, StringSplitOptions.None)[1]) == "Örnek <tag></pre>"));
                Test("txt join order", () =>
                {
                    var t = P("2.txt");
                    File.WriteAllText(t, "son");
                    var o = Engine.Run(new[] { txt, t }, "txt-merge", 90, CancellationToken.None);
                    Require(File.ReadAllLines(o).SequenceEqual(new[] { "Örnek <tag>", "son" }));
                });
                string srt = P("a.srt");
                File.WriteAllText(srt, "1\n00:00:02,100 --> 00:00:04,000\nŞehir\n");
                Test("srt->vtt", () => Require(File.ReadAllText(Convert(srt, "vtt")).Contains("00:00:02.100 --> 00:00:04.000")));
                Test("vtt->srt", () => Require(File.ReadAllText(Convert(Convert(srt, "vtt"), "srt")).Contains("00:00:02,100 --> 00:00:04,000" + Environment.NewLine + "Şehir")));
                foreach (var ext in new[]
                {
                    "csv",
                    "tsv"
                }

                )
                {
                    string file = P("rows." + ext), sep = ext == "csv" ? "," : "\t";
                    File.WriteAllText(file, "name" + sep + "id\n\"A,Ş\"" + sep + "001");
                    string target = ext == "csv" ? "tsv" : "csv";
                    Test(ext + "->" + target, () => Require(File.ReadAllText(Convert(file, target)).Contains("\"A,Ş\"" + (target == "tsv" ? "\t" : ",") + "\"001\"")));
                    Test(ext + "->json", () =>
                    {
                        var rows = new JavaScriptSerializer().Deserialize<string[][]>(File.ReadAllText(Convert(file, "json")));
                        Require(rows[1].SequenceEqual(new[] { "A,Ş", "001" }));
                    });
                }

                Test("spreadsheet formula rejection", () =>
                {
                    var f = P("bad.csv");
                    File.WriteAllText(f, "column\n=1+2");
                    Reject(() => Convert(f, "tsv"));
                });
                Test("zip nested folder / extraction", () =>
                {
                    var d = Directory.CreateDirectory(P("d"));
                    File.WriteAllText(Path.Combine(d.FullName, "ü.txt"), "Ü");
                    var z = Convert(d.FullName, "zip");
                    var outDir = Convert(z, "extract");
                    Require(File.ReadAllText(Path.Combine(outDir, "d", "ü.txt")) == "Ü");
                });
                Test("zip traversal refused", () =>
                {
                    var p = P("bad.zip");
                    using (var z = ZipFile.Open(p, ZipArchiveMode.Create))
                        z.CreateEntry("../escape.txt");
                    Reject(() => Convert(p, "extract"));
                    Require(!File.Exists(P("escape.txt")));
                });
                Test("output concurrency and original", () =>
                {
                    string before = File.ReadAllText(txt);
                    var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() => Convert(txt, "html"))).ToArray();
                    Task.WaitAll(tasks);
                    Require(tasks.Select(t => t.Result).Distinct().Count() == 12 && before == File.ReadAllText(txt));
                });
                Test("cancel temp cleanup", () =>
                {
                    Reject(() => Engine.Run(new[] { txt }, "html", 85, new CancellationToken(true)));
                    Require(!Directory.EnumerateFileSystemEntries(root, ".hepta-*").Any());
                });
                Test("settings roundtrip", () =>
                {
                    string p = P("settings.json");
                    Preferences.Write(p, new Preferences { English = true });
                    Require(Preferences.Read(p).English);
                });
                Test("settings reject invalid", () =>
                {
                    string p = P("invalid.json");
                    File.WriteAllText(p, "{\"Schema\":99}");
                    Reject(() => Preferences.Read(p));
                });
                Test("7 path geometry and lower petal", () =>
                {
                    using (var s = typeof(Engine).Assembly.GetManifestResourceStream("menu.svg"))
                    {
                        var xml = XDocument.Load(s);
                        Require(xml.Descendants().Count(x => ((string)x.Attribute("id") ?? "").StartsWith("petal-")) == 7);
                        Require(xml.Descendants().Any(x => (string)x.Attribute("id") == "center-heptagon"));
                        var r = new Radial(new[] { txt }, Engine.Targets, new Preferences(), null);
                        Require(r.Child != null);
                    }
                });
                string samplePdf = Convert(fixture["png"], "pdf");
                Test("native PDF parser and rendering", () =>
                {
                    Task.Run(async () =>
                    {
                        Require(await NativeMedia.Pages(samplePdf, CancellationToken.None) == 1);
                        foreach (string ext in new[]
                        {
                            "png",
                            "jpg"
                        }

                        )
                        {
                            string f = P("page." + ext);
                            await NativeMedia.Render(samplePdf, 0, f, ext, CancellationToken.None);
                            using (var b = new Bitmap(f))
                                Require(b.Width > 0 && b.Height > 0 && b.GetPixel(1, 1).A > 0);
                        }
                    }).GetAwaiter().GetResult();
                });
                string wav = P("sample.wav");
                using (var stream = File.Create(wav))
                using (var w = new BinaryWriter(stream))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));
                    w.Write(36 + 88200);
                    w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(44100);
                    w.Write(88200);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data"));
                    w.Write(88200);
                    for (int i = 0; i < 44100; i++)
                        w.Write((short)(Math.Sin(i * .02) * 10000));
                }

                foreach (string ext in new[]
                {
                    "mp3",
                    "m4a"
                }

                )
                    Test("native wav->" + ext, () =>
                    {
                        string output = P("audio." + ext);
                        File.WriteAllBytes(output, new byte[0]);
                        Task.Run(() => NativeMedia.Transcode(wav, output, ext, CancellationToken.None)).GetAwaiter().GetResult();
                        Require(new FileInfo(output).Length > 1000);
                    });
                foreach (string ext in new[]
                {
                    "mp3",
                    "m4a",
                    "wav"
                }

                )
                    foreach (string target in new[]
                    {
                        "mp3",
                        "m4a",
                        "wav"
                    }.Where(t => t != ext))
                        Test(ext + "->" + target, () =>
                        {
                            var input = ext == "wav" ? wav : P("audio." + ext);
                            string o = Task.Run(() => Convert(input, target)).GetAwaiter().GetResult();
                            using (var stream = File.OpenRead(o))
                            {
                                var header = new byte[16];
                                Require(stream.Read(header, 0, 16) == 16);
                                string ascii = Encoding.ASCII.GetString(header);
                                Require(target == "wav" ? ascii.StartsWith("RIFF") : target == "m4a" ? ascii.Contains("ftyp") : header[0] == 255 || ascii.StartsWith("ID3"));
                            }

                            var storage = Task.Run(() => NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(o), CancellationToken.None)).Result;
                            var properties = Task.Run(() => NativeMedia.Result(storage.Properties.GetMusicPropertiesAsync(), CancellationToken.None)).Result;
                            Require(properties.Duration.TotalSeconds > .7 && properties.Duration.TotalSeconds < 1.5);
                        });
                Test("PDF split ranges", () =>
                {
                    Require(Documents.Cuts("37, 14,14", 47).SequenceEqual(new[] { 14, 37 }));
                    foreach (var invalid in new[]
                    {
                        "",
                        "0",
                        "-1",
                        "47",
                        "48",
                        "14,",
                        "x"
                    }

                    )
                        Reject(() => Documents.Cuts(invalid, 47));
                });
                Test("PDF mixed merge and split integrity", () => Task.Run(async () =>
                {
                    var token = CancellationToken.None;
                    string merged = Documents.Merge(new[] { samplePdf, fixture["jpg"], samplePdf }, 90, token);
                    Require(await NativeMedia.Pages(merged, token) == 3);
                    var parts = Directory.GetFiles(Documents.Split(merged, "1", 90, token)).OrderBy(n => n).ToArray();
                    Require(parts.Length == 2 && await NativeMedia.Pages(parts[0], token) == 1 && await NativeMedia.Pages(parts[1], token) == 2);
                    foreach (var target in new[]
                    {
                        "png",
                        "jpg"
                    }

                    )
                    {
                        var rendered = Directory.GetFiles(Convert(merged, target));
                        Require(rendered.Length == 3);
                        foreach (var f in rendered)
                            using (var b = new Bitmap(f))
                                Require(b.Width > 0 && b.Height > 0);
                    }
                }).GetAwaiter().GetResult());
                Test("video fixture", () => Task.Run(async () =>
                {
                    var token = CancellationToken.None;
                    string output = P("movie.mp4");
                    File.WriteAllBytes(output, new byte[0]);
                    var composition = new Windows.Media.Editing.MediaComposition();
                    var clip = Windows.Media.Editing.MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 190, 80, 40), TimeSpan.FromSeconds(1));
                    composition.Clips.Add(clip);
                    var audio = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(wav), token);
                    composition.BackgroundAudioTracks.Add(await NativeMedia.Result(Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(audio), token));
                    var dest = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(output), token);
                    var op = composition.RenderToFileAsync(dest, Windows.Media.Editing.MediaTrimmingPreference.Precise, Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga));
                    await NativeMedia.Wait(op, token);
                    Require(op.GetResults() == Windows.Media.Transcoding.TranscodeFailureReason.None);
                }).GetAwaiter().GetResult());
                foreach (var target in new[]
                {
                    "wmv",
                    "mp3",
                    "m4a",
                    "wav"
                }

                )
                    Test("native mp4->" + target, () =>
                    {
                        string o = P("movie-out." + target);
                        File.WriteAllBytes(o, new byte[0]);
                        Task.Run(() => NativeMedia.Transcode(P("movie.mp4"), o, target, CancellationToken.None)).GetAwaiter().GetResult();
                        Require(new FileInfo(o).Length > 1000);
                    });
                foreach (var target in new[]
                {
                    "mp4",
                    "mp3",
                    "m4a",
                    "wav"
                }

                )
                    Test("wmv->" + target, () => Task.Run(async () =>
                    {
                        string o = Convert(P("movie-out.wmv"), target);
                        var f = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(o), CancellationToken.None);
                        TimeSpan duration = target == "mp4" ? (await NativeMedia.Result(f.Properties.GetVideoPropertiesAsync(), CancellationToken.None)).Duration : (await NativeMedia.Result(f.Properties.GetMusicPropertiesAsync(), CancellationToken.None)).Duration;
                        Require(duration.TotalSeconds > .6 && duration.TotalSeconds < 1.6);
                    }).GetAwaiter().GetResult());
                foreach (var pair in new[]
                {
                    "media-join",
                    "audio-join"
                }

                )
                    Test(pair, () => Task.Run(async () =>
                    {
                        bool video = pair == "media-join";
                        string o = Engine.Run(video ? new[] { P("movie.mp4"), P("movie-out.wmv") } : new[] { wav, P("audio.mp3") }, pair, 90, CancellationToken.None);
                        var f = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(o), CancellationToken.None);
                        var d = video ? (await NativeMedia.Result(f.Properties.GetVideoPropertiesAsync(), CancellationToken.None)).Duration : (await NativeMedia.Result(f.Properties.GetMusicPropertiesAsync(), CancellationToken.None)).Duration;
                        Require(d.TotalSeconds > 1.5 && d.TotalSeconds < 2.6);
                    }).GetAwaiter().GetResult());
                foreach (var ext in new[]
                {
                    "flac",
                    "wma",
                    "avi"
                }

                )
                    Test("new codec " + ext, () => Task.Run(async () =>
                    {
                        var token = CancellationToken.None;
                        string o = P("generated." + ext);
                        File.WriteAllBytes(o, new byte[0]);
                        await NativeMedia.Transcode(ext == "avi" ? P("movie.mp4") : wav, o, ext, token);
                        var f = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(o), token);
                        if (ext == "flac")
                        {
                            string decoded = P("decoded-flac.wav");
                            File.WriteAllBytes(decoded, new byte[0]);
                            await NativeMedia.Transcode(o, decoded, "wav", token);
                            f = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(decoded), token);
                        }

                        TimeSpan d = ext == "avi" ? (await NativeMedia.Result(f.Properties.GetVideoPropertiesAsync(), token)).Duration : (await NativeMedia.Result(f.Properties.GetMusicPropertiesAsync(), token)).Duration;
                        Require(d.TotalSeconds > .7 && d.TotalSeconds < 1.5);
                    }).GetAwaiter().GetResult());
                foreach (string ext in new[]
                {
                    "txt",
                    "md"
                }

                )
                    Test(ext + "->vector PDF", () => Task.Run(async () =>
                    {
                        string f = P("belge." + ext);
                        File.WriteAllText(f, (ext == "md" ? "# " : "") + "Türkçe Şehir");
                        string result = Convert(f, "pdf");
                        Require(await NativeMedia.Pages(result, CancellationToken.None) == 1);
                        string png = P("text-" + ext + ".png");
                        await NativeMedia.Render(result, 0, png, "png", CancellationToken.None);
                        using (var b = new Bitmap(png))
                        {
                            bool ink = false;
                            for (int y = 10; y < 150 && !ink; y++)
                                for (int x = 10; x < 400; x++)
                                    if (b.GetPixel(x, y).R < 128)
                                    {
                                        ink = true;
                                        break;
                                    }

                            Require(ink);
                        }
                    }).GetAwaiter().GetResult());
                Test("pdf->txt layer", () =>
                {
                    string pdf = Convert(txt, "pdf");
                    string output = Convert(pdf, "txt");
                    Require(File.ReadAllText(output).Contains("Örnek"));
                });
                Test("md->html", () =>
                {
                    string md = P("simple.md");
                    File.WriteAllText(md, "# Başlık\n- **item**\n<script>");
                    string h = File.ReadAllText(Convert(md, "html"));
                    Require(h.Contains("<h1>") && h.Contains("<strong>item</strong>") && !h.Contains("<script>"));
                });
                foreach (var ext in new[]
                {
                    "csv",
                    "tsv"
                }

                )
                    Test(ext + "->xlsx and xlsx->" + ext, () =>
                    {
                        var w = Convert(P("rows." + ext), "xlsx");
                        var rows = Workbook.Read(w, CancellationToken.None);
                        Require(rows.Count == 2 && rows[1][1] == "001");
                        Require(File.ReadAllText(Convert(w, ext)).Contains("\"001\""));
                    });
                Test("ods->csv and tsv", () =>
                {
                    string file = P("test.ods");
                    using (var z = ZipFile.Open(file, ZipArchiveMode.Create))
                    using (var w = new StreamWriter(z.CreateEntry("content.xml").Open()))
                        w.Write("<o:document-content xmlns:o='urn:oasis:names:tc:opendocument:xmlns:office:1.0' xmlns:t='urn:oasis:names:tc:opendocument:xmlns:table:1.0' xmlns:s='urn:oasis:names:tc:opendocument:xmlns:text:1.0'><o:body><o:spreadsheet><t:table><t:table-row><t:table-cell><s:p>Şehir</s:p></t:table-cell></t:table-row></t:table></o:spreadsheet></o:body></o:document-content>");
                    foreach (var target in new[]
                    {
                        "csv",
                        "tsv"
                    }

                    )
                        Require(File.ReadAllText(Convert(file, target)).Contains("Şehir"));
                });
                Test("JSON <-> YAML nested Unicode", () =>
                {
                    string input = P("nested.json");
                    File.WriteAllText(input, "{\"Şehir\":[\"Bursa\",3,true,null],\"nested\":{\"x\":{}}}");
                    string yaml = Convert(input, "yaml");
                    Require(File.ReadAllText(yaml).Contains("- \"Bursa\""));
                    Require(File.ReadAllText(Convert(yaml, "json")) == File.ReadAllText(input));
                    Reject(() => DataText.Transform("x: !!unsafe foo", false));
                });
                Test("GZ roundtrip", () => Require(File.ReadAllText(Convert(Convert(txt, "gz"), "extract")) == File.ReadAllText(txt)));
                foreach (string ext in new[]
                {
                    "tar",
                    "7z"
                }

                )
                    Test(ext + " extract", () =>
                    {
                        string file = P("archive." + ext);
                        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-c --format " + (ext == "7z" ? "7zip" : "pax") + " -f \"" + file + "\" -C \"" + root + "\" \"İçerik.txt\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var proc = System.Diagnostics.Process.Start(psi))
                        {
                            proc.WaitForExit();
                            Require(proc.ExitCode == 0);
                        }

                        Require(File.ReadAllText(Path.Combine(Convert(file, "extract"), "İçerik.txt")) == File.ReadAllText(txt));
                    });
                foreach (var ext in Engine.Audio.Concat(Engine.Video))
                {
                    string input = ext == "wav" ? wav : ext == "mp3" || ext == "m4a" ? P("audio." + ext) : ext == "mp4" ? P("movie.mp4") : ext == "wmv" ? P("movie-out.wmv") : P("generated." + ext);
                    foreach (string target in Engine.Audio.Concat(Engine.Video).Where(t => Engine.Accept(input, t)))
                    {
                        Test("matrix " + ext + "->" + target, () => Task.Run(async () =>
                        {
                            var token = CancellationToken.None;
                            string output = Convert(input, target);
                            if (target == "flac")
                            {
                                string decoded = P(Guid.NewGuid() + ".wav");
                                File.WriteAllBytes(decoded, new byte[0]);
                                await NativeMedia.Transcode(output, decoded, "wav", token);
                                output = decoded;
                            }

                            var sf = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(output), token);
                            TimeSpan duration = Engine.Video.Contains(target) ? (await NativeMedia.Result(sf.Properties.GetVideoPropertiesAsync(), token)).Duration : (await NativeMedia.Result(sf.Properties.GetMusicPropertiesAsync(), token)).Duration;
                            Require(duration.TotalSeconds > .55 && duration.TotalSeconds < 1.65);
                        }).GetAwaiter().GetResult());
                    }
                }

                foreach (string ext in Engine.Images)
                    Test(ext + "->ico and ico->png", () =>
                    {
                        string icon = Convert(fixture[ext], "ico");
                        using (var input = File.OpenRead(icon))
                        {
                            var frames = new System.Windows.Media.Imaging.IconBitmapDecoder(input, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames;
                            Require(frames.Count == 7 && frames.Any(x => x.PixelWidth == 32));
                        }

                        using (var b = new Bitmap(Convert(icon, "png")))
                            Require(b.Width == 256 && b.Height == 256);
                    });
                Test("GIF frames", () =>
                {
                    var dir = Convert(fixture["gif"], "gif-frames");
                    using (var b = new Bitmap(Directory.GetFiles(dir).Single()))
                        Require(b.Width == 12 && b.Height == 8);
                });
                Test("GIF -> MP4", () => Task.Run(async () =>
                {
                    var f = await NativeMedia.Result(Windows.Storage.StorageFile.GetFileFromPathAsync(Convert(fixture["gif"], "mp4")), CancellationToken.None);
                    var props = await NativeMedia.Result(f.Properties.GetVideoPropertiesAsync(), CancellationToken.None);
                    Require(props.Duration.TotalMilliseconds > 0);
                }).GetAwaiter().GetResult());
                Test("real monitor edge positioning", WindowsChecks.Monitors);
                Test("global shortcut collision", WindowsChecks.ShortcutConflict);
                Test("tar and 7z packing Unicode", () =>
                {
                    foreach (string target in new[]
                    {
                        "tar",
                        "7z"
                    }

                    )
                    {
                        string packed = Convert(txt, target);
                        string dir = Convert(packed, "extract");
                        Require(File.ReadAllText(Path.Combine(dir, Path.GetFileName(txt))) == File.ReadAllText(txt));
                    }
                });
                Test("RAR4 stored extraction", () =>
                {
                    string a = P("fixture.rar");
                    WindowsChecks.Rar(a);
                    Require(File.ReadAllText(Path.Combine(Convert(a, "extract"), "a.txt")) == "Bursa");
                });
                Test("approved SVG worker rendering", () =>
                {
                    string f = P("menu.svg");
                    File.Copy(Path.GetFullPath("assets/Hepta_Menu_V3.svg"), f);
                    string o = Task.Run(() => Convert(f, "png")).GetAwaiter().GetResult();
                    using (var b = new Bitmap(o))
                        Require(b.Width == 1200 && b.Height == 1200);
                });
                Test("XLS BIFF8 -> CSV and TSV", () =>
                {
                    string file = P("test.xls");
                    WindowsChecks.Xls(file);
                    foreach (var target in new[]
                    {
                        "csv",
                        "tsv"
                    }

                    )
                        Require(File.ReadAllText(Convert(file, target)).Contains("Şehir"));
                });
                Test("corrupt and locked images", () =>
                {
                    string f = P("corrupt.png");
                    File.WriteAllText(f, "not png");
                    Reject(() => Convert(f, "jpg"));
                    using (var held = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Reject(() => Convert(f, "jpg"));
                    Require(!Directory.EnumerateFileSystemEntries(root, ".hepta-*").Any());
                });
                Test("long Unicode path", () =>
                {
                    string dir = root;
                    for (int i = 0; i < 7; i++)
                        dir = Path.Combine(dir, new string ('x', 32));
                    Directory.CreateDirectory(dir);
                    string f = Path.Combine(dir, "ş.txt");
                    File.WriteAllText(f, "İstanbul");
                    Require(File.ReadAllText(Convert(f, "html")).Contains("stanbul"));
                });
                Test("vector holes survive rendering", () =>
                {
                    string f = P("hole.png");
                    using (var b = new Bitmap(32, 32))
                    using (var g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Blue);
                        g.FillRectangle(Brushes.White, 8, 8, 16, 16);
                        b.Save(f, ImageFormat.Png);
                    }

                    var traced = Convert(f, "svg");
                    Require(XDocument.Load(traced).Descendants().Count(n => n.Name.LocalName == "path") == 2);
                    using (var b = new Bitmap(Convert(traced, "png")))
                        Require(b.GetPixel(16, 16).R > 200 && b.GetPixel(1, 1).R < 20);
                });
                Test("PDF text layer preserved after split and merge", () => Task.Run(() =>
                {
                    string source = P("katman.txt");
                    File.WriteAllText(source, "Bursa ve İstanbul");
                    string pdf = Convert(source, "pdf");
                    string joined = Documents.Merge(new[] { pdf, pdf }, 90, CancellationToken.None);
                    var parts = Directory.GetFiles(Documents.Split(joined, "1", 90, CancellationToken.None));
                    Require(parts.Length == 2);
                    foreach (string piece in parts)
                        Require(PdfText.Read(piece, CancellationToken.None).Contains("İstanbul"));
                }).GetAwaiter().GetResult());
                Test("shortcut transaction rollback and swap", () =>
                {
                    var keys = new Dictionary<int, int>();
                    var manager = new ShortcutManager((id, m, k) =>
                    {
                        if (k == 88)
                            return false;
                        keys.Add(id, k);
                        return true;
                    }, id => keys.Remove(id));
                    Require(manager.Apply(67, 3, 69, 3));
                    var old = keys.ToArray();
                    Require(!manager.Apply(65, 5, 88, 5) && keys.SequenceEqual(old));
                    Require(manager.Apply(69, 3, 67, 3) && keys.Count == 2 && manager.IsEdit(keys.Single(i => i.Value == 67).Key));
                    manager.Clear();
                    Require(keys.Count == 0);
                });
                Test("locale key parity", () => Require(Locale.Map("tr.json").Keys.OrderBy(k => k).SequenceEqual(Locale.Map("en.json").Keys.OrderBy(k => k))));
                Test("PDF output location and render DPI", () => Task.Run(() =>
                {
                    string folder = P("exports");
                    Directory.CreateDirectory(folder);
                    string input = Convert(fixture["png"], "pdf");
                    var options = new OutputOptions
                    {
                        DirectoryName = folder,
                        PdfDpi = 192
                    };
                    var dir = OutputOptions.With(options, () => Convert(input, "png"));
                    if (Path.GetDirectoryName(dir) != folder)
                        throw new Exception("Output location mismatch: " + dir);
                    using (var b = new Bitmap(Directory.GetFiles(dir).Single()))
                    {
                        Console.WriteLine("PDF_RENDER " + b.Width + "x" + b.Height);
                        if (b.Width != 32 || b.Height < 20)
                            throw new Exception("Rendered dimensions " + b.Size);
                    }
                }).GetAwaiter().GetResult());
                foreach (string kind in new[]
                {
                    "webp",
                    "avif"
                }

                )
                    Test("platform " + kind + " output matrix", () =>
                    {
                        string f = P("probe." + kind);
                        File.Copy(Path.GetFullPath("assets/probes/" + kind + "." + kind), f);
                        Require(PlatformImages.CanRead(kind));
                        foreach (string t in new[]
                        {
                            "png",
                            "jpg",
                            "bmp",
                            "tiff",
                            "svg",
                            "pdf",
                            "ico",
                            "webp"
                        }.Where(t => t != kind))
                        {
                            string o = Convert(f, t);
                            if (t == "pdf")
                                CheckPdf(o);
                            else if (t == "svg")
                                Require(XDocument.Load(o).Descendants().Any(e => e.Name.LocalName == "path"));
                            else if (t == "ico")
                            {
                                using (var s = File.OpenRead(o))
                                    Require(new System.Windows.Media.Imaging.IconBitmapDecoder(s, 0, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames.Count == 7);
                            }
                            else
                                using (var b = t == "webp" ? PlatformImages.Open(o) : new Bitmap(o))
                                    Require(b.Width == 12 && b.Height == 8 && b.GetPixel(0, 0).G > b.GetPixel(0, 0).R);
                        }
                    });
                foreach (var ext in Engine.Images)
                    Test(ext + "->lossless webp", () =>
                    {
                        string o = Convert(fixture[ext], "webp");
                        using (var b = PlatformImages.Open(o))
                        using (var original = new Bitmap(fixture[ext]))
                            Require(b.Size == original.Size && b.GetPixel(3, 4) == original.GetPixel(3, 4));
                    });
                Test("CSV multiline / escaped quotes / empty records", () =>
                {
                    string raw = "a,b\r\n\"a\r\nb\",\"x\"\"y\"\r\n\r\nend,";
                    var rows = Delimited.Read(raw, ',', CancellationToken.None);
                    Require(rows.Count == 4 && rows[1].SequenceEqual(new[] { "a\r\nb", "x\"y" }) && rows[2].SequenceEqual(new[] { "" }) && rows[3][1] == "");
                    Reject(() => Delimited.Read("\"broken", ',', CancellationToken.None));
                });
                Test("archive output inside source folder", () =>
                {
                    string src = Directory.CreateDirectory(P("self-pack")).FullName;
                    File.WriteAllText(Path.Combine(src, "test.txt"), "x");
                    var zip = OutputOptions.With(new OutputOptions { DirectoryName = src }, () => Convert(src, "zip"));
                    using (var z = ZipFile.OpenRead(zip))
                        Require(z.Entries.Count == 2 && !z.Entries.Any(e => e.Name.StartsWith(".hepta-")));
                });
                Test("WebP lossless alpha and full RGB alphabet", () =>
                {
                    string f = P("palette.png");
                    using (var b = new Bitmap(256, 16))
                    {
                        for (int y = 0; y < 16; y++)
                            for (int x = 0; x < 256; x++)
                                b.SetPixel(x, y, Color.FromArgb(y == 0 ? 128 : 255, x, 255 - x, (3 * x) % 256));
                        b.Save(f, ImageFormat.Png);
                    }

                    using (var b = PlatformImages.Open(Convert(f, "webp")))
                        Require(b.GetPixel(213, 7).R == 213 && b.GetPixel(50, 0).A == 128);
                });
                Test("version 1 settings migration", () =>
                {
                    string f = P("v1.json");
                    File.WriteAllText(f, "{\"Schema\":1,\"MenuSize\":440,\"ConvertKey\":67,\"EditKey\":69}");
                    var migrated = Preferences.Read(f);
                    Require(migrated.Schema == 2 && migrated.MenuSize == 440 && migrated.ConvertModifiers == 3);
                });
                Test("changed source rejects staged output", () =>
                {
                    string source = P("changing.txt");
                    File.WriteAllText(source, "original");
                    Reject(() => OutputOptions.Watch(new[] { source }, () => Engine.Output(source, "html", stage =>
                    {
                        File.WriteAllText(stage, "staged");
                        File.AppendAllText(source, " new");
                    }, CancellationToken.None)));
                    Require(!File.Exists(P("changing.html")) && File.ReadAllText(source) == "original new");
                });
                Test("partial keep and discard", () =>
                {
                    string broken = P("broken.txt");
                    File.WriteAllBytes(broken, new byte[] { 0xff, 0xfe, 0xfd, 0xff, 0xff });
                    File.WriteAllBytes(broken, new byte[] { 0xc0, 0x80 });
                    Pending record;
                    string r = Pending.Run(new[] { txt, broken }, "txt-merge", 90, "partial", CancellationToken.None, out record);
                    Require(r == null && record != null && record.Included.Length == 1 && record.Failures.Length == 1 && File.Exists(record.Stage()));
                    string json = new JavaScriptSerializer().Serialize(record);
                    var restored = new JavaScriptSerializer().Deserialize<Pending>(json);
                    string output = restored.Keep();
                    Require(File.ReadAllText(output).Contains("Örnek") && File.Exists(broken));
                    Pending.Run(new[] { txt, broken }, "txt-merge", 90, "partial", CancellationToken.None, out record);
                    string stage = record.Stage();
                    record.Discard();
                    Require(!File.Exists(stage) && File.Exists(output));
                });
                Test("dashboard 12 pages and live WPF render", () =>
                {
                    var window = new Dashboard(true, true);
                    try
                    {
                        window.Width = 1080;
                        window.Height = 720;
                        for (int page = 0; page < 12; page++)
                        {
                            window.RenderPage(page);
                            var element = (System.Windows.FrameworkElement)window.Content;
                            element.Measure(new System.Windows.Size(1080, 720));
                            element.Arrange(new System.Windows.Rect(0, 0, 1080, 720));
                            element.UpdateLayout();
                            if (page == 0)
                            {
                                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1080, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                bitmap.Render(element);
                                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                                string folder = Path.GetFullPath("docs/screenshots");
                                Directory.CreateDirectory(folder);
                                using (var file = File.Create(Path.Combine(folder, "overview.png")))
                                    encoder.Save(file);
                            }
                        }
                    }
                    finally
                    {
                        window.EndTest();
                    }
                });
                Console.WriteLine("RESULT pass=" + pass + " fail=" + fail + " skip=" + skipped);
                return fail == 0 ? 0 : 1;
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
