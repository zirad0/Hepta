using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Hepta
{
    internal static class Workbook
    {
        static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        static readonly XNamespace DocRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static XDocument Xml(ZipArchive z, string name)
        {
            var e = z.GetEntry(name);
            if (e == null || e.Length > 16000000)
                throw new InvalidDataException("Missing or oversized XML: " + name);
            using (var f = e.Open())
            using (var r = XmlReader.Create(f, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 16000000 }))
                return XDocument.Load(r);
        }

        static string Col(int n)
        {
            string s = "";
            while (n > 0)
            {
                n--;
                s = (char)('A' + n % 26) + s;
                n /= 26;
            }

            return s;
        }

        static void WriteXml(ZipArchive z, string name, XElement node)
        {
            using (var s = z.CreateEntry(name).Open())
                node.Save(s);
        }

        public static void Save(string file, List<string[]> rows, CancellationToken token)
        {
            using (var stream = File.Create(file))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                WriteXml(zip, "[Content_Types].xml", new XElement(ct + "Types", new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")), new XElement(ct + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
                Action<string, string, string> rel = (path, type, dest) => WriteXml(zip, path, new XElement(Rel + "Relationships", new XElement(Rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", DocRel.NamespaceName + "/" + type), new XAttribute("Target", dest))));
                rel("_rels/.rels", "officeDocument", "xl/workbook.xml");
                rel("xl/_rels/workbook.xml.rels", "worksheet", "worksheets/sheet1.xml");
                WriteXml(zip, "xl/workbook.xml", new XElement(Sheet + "workbook", new XElement(Sheet + "sheets", new XElement(Sheet + "sheet", new XAttribute("name", "Data"), new XAttribute("sheetId", 1), new XAttribute(DocRel + "id", "rId1")))));
                using (var s = zip.CreateEntry("xl/worksheets/sheet1.xml").Open())
                using (var w = XmlWriter.Create(s))
                {
                    w.WriteStartElement("worksheet", Sheet.NamespaceName);
                    w.WriteStartElement("sheetData");
                    int n = 0;
                    foreach (string[] row in rows)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++n > 1048576 || row.Length > 16384)
                            throw new InvalidDataException("Workbook size limit");
                        w.WriteStartElement("row");
                        w.WriteAttributeString("r", n.ToString());
                        int column = 0;
                        foreach (string cell in row)
                        {
                            if (cell.Length > 32767)
                                throw new InvalidDataException("Excel cell length");
                            w.WriteStartElement("c");
                            w.WriteAttributeString("r", Col(++column) + n);
                            w.WriteAttributeString("t", "inlineStr");
                            w.WriteStartElement("is");
                            w.WriteStartElement("t");
                            w.WriteAttributeString("xml", "space", null, "preserve");
                            w.WriteString(cell);
                            w.WriteEndElement();
                            w.WriteEndElement();
                            w.WriteEndElement();
                        }

                        w.WriteEndElement();
                    }

                    w.WriteEndElement();
                    w.WriteEndElement();
                }
            }
        }

        public static List<string[]> Read(string file, CancellationToken token)
        {
            using (var zip = ZipFile.OpenRead(file))
            {
                if (zip.Entries.Count > 2000)
                    throw new InvalidDataException("Workbook entry limit");
                if (Engine.Ext(file) == "ods")
                    return Ods(Xml(zip, "content.xml"), token);
                var sheets = Xml(zip, "xl/workbook.xml").Descendants(Sheet + "sheet").ToArray();
                if (sheets.Length != 1)
                    throw new InvalidDataException("Single worksheet required");
                string id = (string)sheets[0].Attribute(DocRel + "id");
                var rel = Xml(zip, "xl/_rels/workbook.xml.rels").Descendants(Rel + "Relationship").Single(r => (string)r.Attribute("Id") == id);
                string path = (string)rel.Attribute("Target");
                if (path.Contains("..") || path.Contains(':') || path.Contains('\\'))
                    throw new InvalidDataException("Worksheet reference");
                var doc = Xml(zip, path.StartsWith("/") ? path.Substring(1) : "xl/" + path);
                if (doc.Descendants(Sheet + "mergeCell").Any() || doc.Descendants(Sheet + "f").Any())
                    throw new InvalidDataException("Formulas or merged cells are unsupported");
                string[] shared = zip.GetEntry("xl/sharedStrings.xml") == null ? new string[0] : Xml(zip, "xl/sharedStrings.xml").Descendants(Sheet + "si").Select(x => string.Concat(x.Descendants(Sheet + "t").Select(t => t.Value))).ToArray();
                var rows = new List<string[]>();
                long cells = 0;
                foreach (var r in doc.Descendants(Sheet + "row"))
                {
                    token.ThrowIfCancellationRequested();
                    int index = (int? )r.Attribute("r") ?? rows.Count + 1;
                    if (index <= rows.Count || index > 100000)
                        throw new InvalidDataException("Workbook row limit");
                    while (rows.Count < index - 1)
                        rows.Add(new string[0]);
                    var row = new List<string>();
                    foreach (var c in r.Elements(Sheet + "c"))
                    {
                        var addr = Regex.Match((string)c.Attribute("r") ?? "", @"^([A-Z]{1,3})([0-9]+)$");
                        if (!addr.Success || int.Parse(addr.Groups[2].Value) != index)
                            throw new InvalidDataException("Cell address");
                        int col = addr.Groups[1].Value.Aggregate(0, (n, ch) => 26 * n + ch - 'A' + 1);
                        if (col <= row.Count || col > 16384 || cells + col > 1000000)
                            throw new InvalidDataException("Workbook cell limit");
                        while (row.Count < col - 1)
                            row.Add("");
                        string type = (string)c.Attribute("t") ?? "n", value = (string)c.Element(Sheet + "v") ?? "";
                        if (type == "s")
                            value = shared[int.Parse(value)];
                        else if (type == "inlineStr")
                            value = string.Concat(c.Descendants(Sheet + "t").Select(x => x.Value));
                        else if (type == "b")
                            value = value == "1" ? "true" : "false";
                        else if (type != "str" && type != "n" && type != "d")
                            throw new InvalidDataException("Unsupported cell type");
                        if (c.Attribute("s") != null && type == "n")
                            throw new InvalidDataException("Formatted numeric cells require explicit interpretation");
                        row.Add(value);
                    }

                    cells += row.Count;
                    rows.Add(row.ToArray());
                }

                return rows;
            }
        }

        static List<string[]> Ods(XDocument doc, CancellationToken token)
        {
            XNamespace t = "urn:oasis:names:tc:opendocument:xmlns:table:1.0", text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0", office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            var sheets = doc.Descendants(t + "table").ToArray();
            if (sheets.Length != 1)
                throw new InvalidDataException("Single worksheet required");
            var rows = new List<string[]>();
            long cells = 0;
            foreach (var r in sheets[0].Elements(t + "table-row"))
            {
                token.ThrowIfCancellationRequested();
                int repeat = (int? )r.Attribute(t + "number-rows-repeated") ?? 1;
                if (repeat < 1 || repeat + rows.Count > 100000)
                    throw new InvalidDataException("ODS row limit");
                var row = new List<string>();
                foreach (var c in r.Elements())
                {
                    if (c.Name != t + "table-cell" || c.Attribute(t + "formula") != null || c.Attribute(t + "number-columns-spanned") != null || c.Attribute(t + "number-rows-spanned") != null)
                        throw new InvalidDataException("Complex ODS cell");
                    int count = (int? )c.Attribute(t + "number-columns-repeated") ?? 1;
                    if (count < 1 || count + row.Count > 16384)
                        throw new InvalidDataException("ODS column limit");
                    string v = string.Join("\n", c.Elements(text + "p").Select(p => p.Value));
                    if (v.Length == 0)
                        v = (string)c.Attribute(office + "value") ?? (string)c.Attribute(office + "string-value") ?? "";
                    row.AddRange(Enumerable.Repeat(v, count));
                }

                cells += (long)repeat * row.Count;
                if (cells > 1000000)
                    throw new InvalidDataException("ODS cell limit");
                rows.AddRange(Enumerable.Repeat(row.ToArray(), repeat));
            }

            return rows;
        }
    }
}
