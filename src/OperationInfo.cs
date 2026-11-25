using System;
using System.IO;
using System.Linq;

namespace Hepta
{
    internal static class OperationInfo
    {
        static string L(bool en, string id)
        {
            return Locale.Get(en, id);
        }

        public static string Describe(string[] files, string target, bool en)
        {
            if (files.Length == 0)
                return L(en, "select.files.in.explorer.or.on.the.desktop.2");
            if (Pending.IsMerge(target) && files.Length < 2)
                return L(en, "select.at.least.two.compatible.files");
            if (target == "pdf-split" && files.Length != 1)
                return L(en, "select.one.pdf");
            var bad = files.FirstOrDefault(f => !Engine.Accept(f, target));
            if (bad != null)
                return L(en, "unsupported.for.this.operation") + Path.GetFileName(bad);
            if (target == "jpg" || target == "pdf" && files.Any(f => Engine.Images.Contains(Engine.Ext(f))))
                return L(en, "transparency.becomes.white.jpeg.compression.can.lose.detai");
            if (target == "svg")
                return L(en, "traces.colour.region.boundaries.photographs.produce.approx");
            if (target == "pdf-merge" || target == "pdf-split")
                return L(en, "creates.image.based.pages.selectable.text.forms.links.and");
            if (Engine.Audio.Contains(target) && files.Any(f => Engine.Video.Contains(Engine.Ext(f))))
                return L(en, "requires.an.audio.stream.only.audio.is.exported");
            if (Engine.Video.Contains(target) || Engine.Audio.Contains(target))
                return L(en, "uses.windows.media.codecs.re.encoding.can.affect.quality");
            if (target == "csv" || target == "tsv")
                return L(en, "exports.values.from.one.sheet.formatting.is.omitted.and.fo");
            if (target == "txt")
                return L(en, "requires.readable.pdf.text.no.ocr.is.performed");
            if (target == "png" || target == "bmp" || target == "tiff" || target == "ico")
                return L(en, "colour.profiles.and.metadata.may.change.use.frame.extracti");
            return L(en, "left.click.to.run.right.click.for.job.options");
        }
    }
}
