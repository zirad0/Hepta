using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Hepta
{
    internal static class ImageTools
    {
        public static void Icon(Image image, string output)
        {
            int[] sizes =
            {
                16,
                24,
                32,
                48,
                64,
                128,
                256
            };
            var frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (var b = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(b))
                using (var ms = new MemoryStream())
                {
                    double scale = Math.Min(size / (double)image.Width, size / (double)image.Height);
                    int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, (size - w) / 2, (size - h) / 2, w, h);
                    b.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            }

            using (var writer = new BinaryWriter(File.Create(output)))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] % 256));
                    writer.Write((byte)(sizes[i] % 256));
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset += frames[i].Length;
                }

                foreach (var bytes in frames)
                    writer.Write(bytes);
            }
        }

        public static string Frames(string file, CancellationToken token)
        {
            return Documents.Folder(file, dir =>
            {
                using (var image = Image.FromFile(file))
                {
                    if ((long)image.Width * image.Height > 16000000)
                        throw new InvalidDataException("GIF dimensions");
                    var dim = new FrameDimension(image.FrameDimensionsList[0]);
                    int count = image.GetFrameCount(dim);
                    if ((long)count * image.Width * image.Height > 500000000)
                        throw new InvalidDataException("GIF frame limit");
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        image.SelectActiveFrame(dim, i);
                        using (var frame = new Bitmap(image))
                            frame.Save(Path.Combine(dir, (i + 1).ToString("D5") + ".png"), ImageFormat.Png);
                    }
                }
            }, token);
        }

        public static async Task Movie(string file, string output, CancellationToken token)
        {
            string temp = Path.Combine(Path.GetTempPath(), "hepta-gif-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var composition = new MediaComposition();
                using (var image = Image.FromFile(file))
                {
                    var dim = new FrameDimension(image.FrameDimensionsList[0]);
                    int count = image.GetFrameCount(dim);
                    if (count > 2000 || (long)count * image.Width * image.Height > 150000000)
                        throw new InvalidDataException("GIF frame limit");
                    byte[] ticks = image.PropertyIdList.Contains(0x5100) ? image.GetPropertyItem(0x5100).Value : new byte[0];
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        image.SelectActiveFrame(dim, i);
                        string path = Path.Combine(temp, i + ".png");
                        using (var frame = new Bitmap(image))
                            frame.Save(path, ImageFormat.Png);
                        int ms = ticks.Length >= 4 * i + 4 ? Math.Max(10, BitConverter.ToInt32(ticks, 4 * i) * 10) : 100;
                        var sf = await NativeMedia.Result(StorageFile.GetFileFromPathAsync(path), token).ConfigureAwait(false);
                        composition.Clips.Add(await NativeMedia.Result(MediaClip.CreateFromImageFileAsync(sf, TimeSpan.FromMilliseconds(ms)), token).ConfigureAwait(false));
                    }
                }

                var dest = await NativeMedia.Result(StorageFile.GetFileFromPathAsync(output), token).ConfigureAwait(false);
                var op = composition.RenderToFileAsync(dest, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga));
                await NativeMedia.Wait(op, token).ConfigureAwait(false);
                if (op.GetResults() != Windows.Media.Transcoding.TranscodeFailureReason.None)
                    throw new InvalidDataException("GIF video encoding");
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
