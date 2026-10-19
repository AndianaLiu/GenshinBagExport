using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
namespace GenshinBagExport;
public static class ExcelExport
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static void Write(string path, List<BagRow> rows)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                var types = new XElement(ct + "Types",
                    new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));
                XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
                Add(zip, "_rels/.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", R.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
                var sheets = new XElement(S + "sheets");
                var relations = new XElement(rel + "Relationships");
                for (int i = 0; i < BagData.Categories.Length; i++)
                {
                    var category = BagData.Categories[i];
                    var id = i + 1;
                    sheets.Add(new XElement(S + "sheet", new XAttribute("name", category), new XAttribute("sheetId", id), new XAttribute(R + "id", $"rId{id}")));
                    relations.Add(new XElement(rel + "Relationship", new XAttribute("Id", $"rId{id}"), new XAttribute("Type", R.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{id}.xml")));
                    types.Add(new XElement(ct + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{id}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                    var data = new XElement(S + "sheetData");
                    data.Add(Row(1, ["物品 ID", "名称", "分类", "具体类型", "星级", "数量"], true));
                    int n = 2;
                    foreach (var row in rows.Where(x => x.Category == category))
                        data.Add(Row(n++, [row.Id.ToString(), row.Name, row.Category, row.Type, row.Star.ToString(), row.Count.ToString()], false));
                    var sheet = new XElement(S + "worksheet",
                        new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0),
                            new XElement(S + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                        new XElement(S + "cols", Enumerable.Range(1, 6).Select(col => new XElement(S + "col", new XAttribute("min", col), new XAttribute("max", col), new XAttribute("width", col is 2 or 3 or 4 ? 28 : 16), new XAttribute("customWidth", 1)))),
                        data, new XElement(S + "autoFilter", new XAttribute("ref", $"A1:F{n - 1}")));
                    Add(zip, $"xl/worksheets/sheet{id}.xml", sheet);
                }
                Add(zip, "[Content_Types].xml", types);
                Add(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), sheets));
                Add(zip, "xl/_rels/workbook.xml.rels", relations);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static XElement Row(int index, string[] values, bool header) =>
        new(S + "row", new XAttribute("r", index), values.Select((value, i) =>
        {
            var cell = new XElement(S + "c", new XAttribute("r", $"{(char)('A' + i)}{index}"));
            if (!header && i >= 4) cell.Add(new XElement(S + "v", value));
            else { cell.SetAttributeValue("t", "inlineStr"); cell.Add(new XElement(S + "is", new XElement(S + "t", value))); }
            return cell;
        }));
    private static void Add(ZipArchive zip, string path, XElement xml)
    {
        using var stream = zip.CreateEntry(path).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), xml).Save(stream);
    }
}
