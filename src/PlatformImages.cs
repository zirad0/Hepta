using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;

namespace Hepta
{
    internal static class PlatformImages
    {
        static readonly Lazy<bool> Webp = new Lazy<bool>(() => Probe("webp"));
        static readonly Lazy<bool> Avif = new Lazy<bool>(() => Probe("avif"));
        public static bool CanRead(string ext)
        {
            return ext == "webp" ? Webp.Value : ext == "avif" && Avif.Value;
        }

        static bool Probe(string kind)
        {
            try
            {
                using (var s = typeof(PlatformImages).Assembly.GetManifestResourceStream(kind + ".probe"))
                using (var image = Read(s))
                {
                    return image.Width == 12 && image.Height == 8;
                }
            }
            catch
            {
                return false;
            }
        }

        static Bitmap Read(Stream input)
        {
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1)
                throw new InvalidDataException("Single image required");
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 16000000)
                throw new InvalidDataException("Image dimensions");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(frame);
            using (var data = new MemoryStream())
            {
                enc.Save(data);
                data.Position = 0;
                using (var b = new Bitmap(data))
                    return new Bitmap(b);
            }
        }

        public static Bitmap Open(string file)
        {
            using (var input = File.OpenRead(file))
                return Read(input);
        }

        public static void Convert(string file, string output, string target, int quality, CancellationToken token)
        {
            using (var b = Open(file))
            {
                switch (target)
                {
                    case "png":
                        b.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                        break;
                    case "bmp":
                        b.Save(output, System.Drawing.Imaging.ImageFormat.Bmp);
                        break;
                    case "tiff":
                        b.Save(output, System.Drawing.Imaging.ImageFormat.Tiff);
                        break;
                    case "jpg":
                        using (var stream = File.Create(output))
                            Engine.Jpeg(b, stream, quality);
                        break;
                    case "pdf":
                        Engine.Pdf(b, output, quality);
                        break;
                    case "ico":
                        ImageTools.Icon(b, output);
                        break;
                    case "svg":
                        Tracing.Write(b, output, quality, token);
                        break;
                    case "webp":
                        Hepta.Webp.Save(b, output, token);
                        break;
                    default:
                        throw new InvalidDataException("Image target");
                }
            }
        }
    }
}
