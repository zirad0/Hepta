using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hepta
{
    internal static class PdfPages
    {
        const string Lib = "pdfium.dll";
        static readonly object Gate = new object ();
        static bool initialized;
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern void FPDF_InitLibrary();
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, IntPtr password);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern IntPtr FPDF_CreateNewDocument();
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern int FPDF_GetPageCount(IntPtr doc);
        [DllImport(Lib, ExactSpelling = true, CharSet = CharSet.Ansi), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern int FPDF_ImportPages(IntPtr dest, IntPtr src, string range, int at);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int WriteBlock(IntPtr self, IntPtr data, uint length);
        [StructLayout(LayoutKind.Sequential)]
        struct Writer
        {
            public int Version;
            public WriteBlock Write;
        }

        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern int FPDF_SaveAsCopy(IntPtr doc, ref Writer writer, uint flags);
        static void Initialize()
        {
            if (initialized)
                return;
            FPDF_InitLibrary();
            initialized = true;
        }

        sealed class Input : IDisposable
        {
            GCHandle data;
            public IntPtr Doc;
            public Input(string file, int quality)
            {
                byte[] bytes;
                if (Engine.Ext(file) == "pdf")
                {
                    if (new FileInfo(file).Length > 192L * 1024 * 1024)
                        throw new InvalidDataException("PDF input exceeds 192 MiB");
                    bytes = File.ReadAllBytes(file);
                }
                else
                {
                    string imagePdf = Path.Combine(Path.GetTempPath(), "hepta-" + Guid.NewGuid().ToString("N") + ".pdf");
                    try
                    {
                        if (Engine.Ext(file) == "svg")
                            Svg.Render(file, imagePdf, "pdf", quality, CancellationToken.None);
                        else
                            using (var img = PlatformImages.CanRead(Engine.Ext(file)) ? (Image)PlatformImages.Open(file) : Image.FromFile(file, true))
                            {
                                if ((long)img.Width * img.Height > 32000000 || Array.Exists(img.FrameDimensionsList, g => img.GetFrameCount(new System.Drawing.Imaging.FrameDimension(g)) != 1))
                                    throw new InvalidDataException("Unsupported image size or frame count");
                                Engine.Pdf(img, imagePdf, quality);
                            }

                        bytes = File.ReadAllBytes(imagePdf);
                    }
                    finally
                    {
                        if (File.Exists(imagePdf))
                            File.Delete(imagePdf);
                    }
                }

                data = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    Doc = FPDF_LoadMemDocument64(data.AddrOfPinnedObject(), (UIntPtr)(uint)bytes.Length, IntPtr.Zero);
                    if (Doc == IntPtr.Zero || FPDF_GetPageCount(Doc) <= 0)
                        throw new InvalidDataException("Unreadable or encrypted PDF");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (Doc != IntPtr.Zero)
                {
                    FPDF_CloseDocument(Doc);
                    Doc = IntPtr.Zero;
                }

                if (data.IsAllocated)
                    data.Free();
            }
        }

        public static void Validate(string file, int quality)
        {
            lock (Gate)
            {
                Initialize();
                using (var input = new Input(file, quality))
                {
                    if (FPDF_GetPageCount(input.Doc) > 5000)
                        throw new InvalidDataException("PDF page limit");
                }
            }
        }

        static void Save(IntPtr doc, string output, CancellationToken token)
        {
            using (var f = File.Create(output))
            {
                Exception error = null;
                var buf = new byte[65536];
                var writer = new Writer
                {
                    Version = 1,
                    Write = (self, data, n) =>
                    {
                        try
                        {
                            for (uint pos = 0; pos < n;)
                            {
                                token.ThrowIfCancellationRequested();
                                int count = (int)Math.Min((uint)buf.Length, n - pos);
                                Marshal.Copy(IntPtr.Add(data, checked((int)pos)), buf, 0, count);
                                f.Write(buf, 0, count);
                                pos += (uint)count;
                            }

                            return 1;
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                            return 0;
                        }
                    }
                };
                int result = FPDF_SaveAsCopy(doc, ref writer, 2);
                GC.KeepAlive(writer.Write);
                if (error != null)
                    throw error;
                if (result == 0)
                    throw new IOException("PDF save failed");
            }
        }

        public static void Merge(string[] files, string output, int quality, CancellationToken token)
        {
            lock (Gate)
            {
                Initialize();
                IntPtr dest = FPDF_CreateNewDocument();
                if (dest == IntPtr.Zero)
                    throw new OutOfMemoryException();
                try
                {
                    int total = 0;
                    foreach (var f in files)
                    {
                        token.ThrowIfCancellationRequested();
                        using (var src = new Input(f, quality))
                        {
                            int n = FPDF_GetPageCount(src.Doc);
                            if (n + total > 5000)
                                throw new InvalidDataException("PDF page limit");
                            if (FPDF_ImportPages(dest, src.Doc, null, total) == 0)
                                throw new InvalidDataException("PDF page import");
                            total += n;
                        }
                    }

                    Save(dest, output, token);
                }
                finally
                {
                    FPDF_CloseDocument(dest);
                }
            }
        }

        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr buffer, int stride);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Lib), DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotation, int flags);
        public static string RenderAll(string file, string ext, CancellationToken token)
        {
            lock (Gate)
            {
                Initialize();
                using (var source = new Input(file, 90))
                {
                    int pages = FPDF_GetPageCount(source.Doc);
                    if (pages > 5000)
                        throw new InvalidDataException("PDF page limit");
                    return Documents.Folder(file, dir =>
                    {
                        for (int i = 0; i < pages; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var page = FPDF_LoadPage(source.Doc, i);
                            if (page == IntPtr.Zero)
                                throw new InvalidDataException("PDF page");
                            try
                            {
                                int dpi = OutputOptions.Current.PdfDpi;
                                double scale = dpi / 72d;
                                int width = (int)Math.Ceiling(FPDF_GetPageWidthF(page) * scale), height = (int)Math.Ceiling(FPDF_GetPageHeightF(page) * scale);
                                if (width <= 0 || height <= 0 || (long)width * height > 32000000)
                                    throw new InvalidDataException("PDF resolution limit");
                                using (var image = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                                {
                                    image.SetResolution(dpi, dpi);
                                    using (var g = Graphics.FromImage(image))
                                        g.Clear(Color.White);
                                    var data = image.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                    try
                                    {
                                        var native = FPDFBitmap_CreateEx(width, height, 4, data.Scan0, data.Stride);
                                        if (native == IntPtr.Zero)
                                            throw new OutOfMemoryException();
                                        try
                                        {
                                            FPDF_RenderPageBitmap(native, page, 0, 0, width, height, 0, 1);
                                        }
                                        finally
                                        {
                                            FPDFBitmap_Destroy(native);
                                        }
                                    }
                                    finally
                                    {
                                        image.UnlockBits(data);
                                    }

                                    token.ThrowIfCancellationRequested();
                                    image.Save(Path.Combine(dir, (i + 1).ToString("D4") + "." + ext), ext == "jpg" ? System.Drawing.Imaging.ImageFormat.Jpeg : System.Drawing.Imaging.ImageFormat.Png);
                                }
                            }
                            finally
                            {
                                FPDF_ClosePage(page);
                            }
                        }
                    }, token);
                }
            }
        }

        public static string Split(string file, string points, CancellationToken token)
        {
            lock (Gate)
            {
                Initialize();
                using (var input = new Input(file, 90))
                {
                    int count = FPDF_GetPageCount(input.Doc);
                    var cuts = Documents.Cuts(points, count);
                    return Documents.Folder(file, dir =>
                    {
                        int start = 1;
                        foreach (int end in System.Linq.Enumerable.Concat(cuts, new[] { count }))
                        {
                            token.ThrowIfCancellationRequested();
                            IntPtr dest = FPDF_CreateNewDocument();
                            if (dest == IntPtr.Zero)
                                throw new OutOfMemoryException();
                            try
                            {
                                string range = start == end ? start.ToString() : start + "-" + end;
                                if (FPDF_ImportPages(dest, input.Doc, range, 0) == 0)
                                    throw new InvalidDataException("PDF range import");
                                Save(dest, Path.Combine(dir, start.ToString("D4") + "-" + end.ToString("D4") + ".pdf"), token);
                            }
                            finally
                            {
                                FPDF_CloseDocument(dest);
                            }

                            start = end + 1;
                        }
                    }, token);
                }
            }
        }
    }
}
