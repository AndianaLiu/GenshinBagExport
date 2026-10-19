using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
namespace GenshinBagExport;

public static class TemplateExport
{
    public static string DefaultTemplate
    {
        get
        {
            var path=PrivateSettings.Load().TemplatePath;
            return string.IsNullOrWhiteSpace(path) ? Path.Combine(AppContext.BaseDirectory,"素材.xlsx") : path;
        }
    }
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private sealed record CellMap(string Address, long Id);
    private sealed record RowMap(string Sheet, int Row, string Label, List<CellMap> Cells, string Total, string Formula);
    private sealed record CopyState(string Key, string InventoryHash, string TemplateHash, string MappingHash, string OutputPath);
    private static readonly object CopyGate = new();
    public static string? WriteIfChanged(List<BagRow> rows, string? uid, string stateDirectory, string? template = null, DateTime? time = null)
    {
        template ??= DefaultTemplate;
        if (!File.Exists(template)) return null;
        lock (CopyGate)
        {
            template = Path.GetFullPath(template);
            string Hash(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
            var key = Hash(System.Text.Encoding.UTF8.GetBytes(template.ToUpperInvariant() + "|" + (uid ?? "unknown")));
            var inventoryHash = SnapshotStore.Fingerprint(rows);
            var templateHash = Hash(File.ReadAllBytes(template));
            var mappingHash = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","template-map.json")));
            var statePath = Path.Combine(stateDirectory,".template-copy-state.json");
            List<CopyState> states = [];
            if(File.Exists(statePath))
            {
                try { states = JsonSerializer.Deserialize<List<CopyState>>(File.ReadAllText(statePath)) ?? []; }
                catch(JsonException) { }
            }
            var previous = states.FirstOrDefault(s => s.Key == key);
            if(previous != null && previous.InventoryHash == inventoryHash &&
                previous.TemplateHash == templateHash && previous.MappingHash == mappingHash &&
                File.Exists(previous.OutputPath)) return null;
            var output = Write(rows, template, time);
            states.RemoveAll(s => s.Key == key);
            states.Add(new CopyState(key,inventoryHash,templateHash,mappingHash,output));
            Directory.CreateDirectory(stateDirectory);
            var temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary,JsonSerializer.Serialize(states,new JsonSerializerOptions {WriteIndented=true}));
                File.Move(temporary,statePath,true);
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
            return output;
        }
    }
    public static string Write(List<BagRow> rows, string? template = null, DateTime? time = null)
    {
        template ??= DefaultTemplate;
        if (!File.Exists(template)) throw new FileNotFoundException("找不到素材模板。", template);
        var maps = JsonSerializer.Deserialize<List<RowMap>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","template-map.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("模板映射缺失。");
        var counts = rows.ToDictionary(x => x.Id, x => x.Count);
        var stamp = time ?? SnapshotStore.ChinaNow;
        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(template))!, $"素材_{stamp:yyyyMMdd_HHmmss}.xlsx");
        if (File.Exists(output)) throw new IOException("同一秒的素材副本已存在，请稍后重试。");
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(template,temporary,false);
            using (var zip = ZipFile.Open(temporary,ZipArchiveMode.Update))
            {
                var book = Read(zip,"xl/workbook.xml");
                XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
                var relations = Read(zip,"xl/_rels/workbook.xml.rels").Root!.Elements(rel+"Relationship")
                    .ToDictionary(x => (string)x.Attribute("Id")!, x => (string)x.Attribute("Target")!);
                var sheets = book.Descendants(S+"sheet").ToDictionary(x => (string)x.Attribute("name")!,x => (string)x.Attribute(R+"id")!);
                var strings = zip.GetEntry("xl/sharedStrings.xml") == null ? [] : Read(zip,"xl/sharedStrings.xml").Root!.Elements(S+"si").Select(x => string.Concat(x.Descendants(S+"t").Select(t => t.Value))).ToList();
                foreach (var group in maps.GroupBy(x=>x.Sheet))
                {
                    if (!sheets.TryGetValue(group.Key,out var sheetId)) throw new InvalidDataException($"模板缺少工作表：{group.Key}");
                    var target=relations[sheetId];
                    var entryPath = new Uri(new Uri("http://xlsx/xl/workbook.xml"),target).AbsolutePath.TrimStart('/');
                    var sheet=Read(zip,entryPath);
                    var cells=sheet.Descendants(S+"c").ToDictionary(x=>(string)x.Attribute("r")!);
                    foreach(var map in group)
                    {
                        if (!cells.TryGetValue($"A{map.Row}",out var label) || Text(label,strings)!=map.Label)
                            throw new InvalidDataException($"模板行已改变：{map.Sheet}!A{map.Row}，应为“{map.Label}”。");
                        if (!cells.TryGetValue(map.Total,out var total) || Formula(total,cells) != map.Formula.TrimStart('='))
                            throw new InvalidDataException($"模板合计公式已改变：{map.Sheet}!{map.Total}。");
                        var values=new List<long>();
                        foreach(var item in map.Cells)
                        {
                            if(!cells.TryGetValue(item.Address,out var cell)) throw new InvalidDataException($"模板缺少数量单元格：{item.Address}");
                            long value=counts.GetValueOrDefault(item.Id);
                            if(value<0) throw new InvalidDataException("材料数量不能为负数。");
                            SetValue(cell,value); values.Add(value);
                        }
                        long sum=0;
                        for(int i=0;i<values.Count;i++)
                        {
                            long weight=group.Key=="角色培养素材" ? 1 : values.Count==3 ? new long[]{9,3,1}[i] : new long[]{27,9,3,1}[i];
                            sum=checked(sum+checked(values[i]*weight));
                        }
                        SetValue(total,sum);
                    }
                    Save(zip,entryPath,sheet);
                }
                var calc=book.Root!.Element(S+"calcPr");
                if(calc==null) { calc=new XElement(S+"calcPr"); book.Root.Add(calc); }
                calc.SetAttributeValue("calcMode","auto");calc.SetAttributeValue("fullCalcOnLoad","1");
                Save(zip,"xl/workbook.xml",book);
            }
            File.Move(temporary,output,false);
            return output;
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string? Formula(XElement cell, Dictionary<string,XElement> cells)
    {
        var f=cell.Element(S+"f");
        if(f==null) return null;
        if(!string.IsNullOrEmpty(f.Value)) return f.Value;
        if((string?)f.Attribute("t")!="shared") return null;
        string? si=(string?)f.Attribute("si");
        var anchor=cells.Values.FirstOrDefault(c => (string?)c.Element(S+"f")?.Attribute("si")==si && !string.IsNullOrEmpty(c.Element(S+"f")?.Value));
        if(anchor==null) return null;
        int Row(XElement c)=>int.Parse(System.Text.RegularExpressions.Regex.Match((string)c.Attribute("r")!, @"\d+$").Value,CultureInfo.InvariantCulture);
        int delta=Row(cell)-Row(anchor);
        return System.Text.RegularExpressions.Regex.Replace(anchor.Element(S+"f")!.Value, @"(\$?[A-Z]+)(\$?)(\d+)", m =>
            m.Groups[1].Value+m.Groups[2].Value+(int.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture)+(m.Groups[2].Value=="$"?0:delta)).ToString(CultureInfo.InvariantCulture));
    }
    private static string Text(XElement cell,List<string> strings) =>
        (string?)cell.Attribute("t")=="s" ? strings[int.Parse(cell.Element(S+"v")!.Value,CultureInfo.InvariantCulture)] :
        (string?)cell.Attribute("t")=="inlineStr" ? string.Concat(cell.Descendants(S+"t").Select(x=>x.Value)) : cell.Element(S+"v")?.Value ?? "";
    private static void SetValue(XElement cell,long value)
    {
        cell.SetAttributeValue("t",null);
        cell.Element(S+"is")?.Remove();
        var v=cell.Element(S+"v");
        if(v==null) {v=new XElement(S+"v"); cell.Add(v);}
        v.Value=value.ToString(CultureInfo.InvariantCulture);
    }
    private static XDocument Read(ZipArchive zip,string path)
    {
        using var stream=(zip.GetEntry(path)??throw new InvalidDataException($"模板缺少 {path}")).Open();
        return XDocument.Load(stream,LoadOptions.PreserveWhitespace);
    }
    private static void Save(ZipArchive zip,string path,XDocument xml)
    {
        zip.GetEntry(path)?.Delete();
        using var stream=zip.CreateEntry(path).Open();
        xml.Save(stream,SaveOptions.DisableFormatting);
    }
}
