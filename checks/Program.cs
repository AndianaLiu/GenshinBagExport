using GenshinBagExport;
using System.IO.Compression;
using System.Xml.Linq;
var rows = BagData.Load(args[0]);
if (rows.Count != 4 || rows.Any(r => r.Id == 11501)) throw new Exception("材料过滤失败");
if (rows.Count(r => r.Category == "货币") != 2) throw new Exception("货币分类失败");
ExcelExport.Write(args[1], rows);
using var zip = ZipFile.OpenRead(args[1]);
XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
using var workbook = zip.GetEntry("xl/workbook.xml")!.Open();
var sheets = XDocument.Load(workbook).Descendants(s + "sheet").ToList();
if (sheets.Count != 15 || sheets.Select(x => (string)x.Attribute("name")!).Distinct().Count() != 15) throw new Exception("Sheet 分类失败");
var cellRows = 0;
foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")))
{
    using var stream = entry.Open();
    cellRows += XDocument.Load(stream).Descendants(s + "row").Count() - 1;
}
if (cellRows != 4) throw new Exception("导出行数不符");
Console.WriteLine("PASS：材料过滤、货币分类、15 个独立 Sheet、4 行物品数据。");

// Protobuf 样例：一个数量为 24 的材料，以及一个应忽略的装备。
byte[] validPacket = [0x1A,0x06,0x08,0x7B,0x2A,0x02,0x08,0x18,0x1A,0x04,0x08,0x01,0x32,0x00];
var parsed = MaterialPacket.Parse(validPacket);
if (parsed.Count != 1) throw new Exception("协议材料过滤失败");
var parsedJson = System.Text.Json.JsonSerializer.Serialize(parsed);
if (!parsedJson.Contains("\"count\":24")) throw new Exception("材料数量解析失败");
bool rejected = false;
try { MaterialPacket.Parse([0x1A,0x06,0x08]); } catch (EndOfStreamException) { rejected = true; }
if (!rejected) throw new Exception("必须拒绝截断包");
Console.WriteLine("PASS：Protobuf 材料解析、装备过滤、截断数据拒绝。");

// 模板副本：精确数量、零库存、公式缓存、原文件不改写及同名保护。
if(args.Length>2)
{
    var runDirectory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"runs",Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(runDirectory);
    var template=Path.Combine(runDirectory,"素材.xlsx");
    File.Copy(Path.GetFullPath(args[2]),template);
    byte[] original=File.ReadAllBytes(template);
    var mappedRows=new List<BagRow> {new(113003,"东风之翎","角色与武器培养素材","角色培养素材",5,123),new(104114,"燃愿玛瑙","角色与武器培养素材","角色突破素材",5,2)};
    var date=new DateTime(2026,10,19,12,34,56);
    var output=TemplateExport.Write(mappedRows,template,date);
    using(var z=ZipFile.OpenRead(output))
    {
        foreach(var e in z.Entries.Where(x=>x.FullName.StartsWith("xl/worksheets/") && x.FullName.EndsWith(".xml")))
        {
            using var input=e.Open();
            var xml=XDocument.Load(input);
            var c=xml.Descendants(s+"c").ToDictionary(x=>(string)x.Attribute("r")!);
            if(e.FullName=="xl/worksheets/sheet1.xml")
            {
                if(c["B2"].Element(s+"v")!.Value!="123" || c["C2"].Element(s+"v")!.Value!="0" || c["E2"].Element(s+"v")!.Value!="123" || c["E2"].Element(s+"f")==null) throw new Exception("模板周本写入或合计失败");
            }
            if(e.FullName=="xl/worksheets/sheet2.xml" && c["F2"].Element(s+"v")!.Value!="54") throw new Exception("宝石合计失败");
        }
    }
    if(!original.SequenceEqual(File.ReadAllBytes(template))) throw new Exception("原模板被改写");
    bool conflict=false; try{TemplateExport.Write(mappedRows,template,date);}catch(IOException){conflict=true;}
    if(!conflict) throw new Exception("未保护同名文件");
    Console.WriteLine("PASS：模板 238 个数量、零库存、合计缓存、原文件保留及同名保护。");
}

var snapshotText=SnapshotJson.Serialize(rows,"{\"uid\":\"sample-user\",\"capturedAt\":\"2026-10-19T12:34:56+08:00\",\"gameVersion\":\"7.1.0\"}");
var snapshotPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"roundtrip.json");
File.WriteAllText(snapshotPath,snapshotText);
var reloaded=BagData.Load(snapshotPath);
if(!rows.SequenceEqual(reloaded)) throw new Exception("JSON 再导入后物品发生变化");
using(var snapshotDoc=System.Text.Json.JsonDocument.Parse(snapshotText))
{
    if(snapshotDoc.RootElement.GetProperty("uid").GetString()!="sample-user") throw new Exception("JSON UID 未保留");
    if(snapshotDoc.RootElement.GetProperty("capturedAt").GetString()!="2026-10-19T12:34:56+08:00") throw new Exception("JSON 采集时间未保留");
}
Console.WriteLine("PASS：JSON 导出/重读一致，UID 和原采集时间保留。");


var missingTemplate=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,Guid.NewGuid().ToString("N"),"素材.xlsx");
if(TemplateExport.WriteIfChanged(rows,"123456789",Path.GetDirectoryName(missingTemplate)!,missingTemplate)!=null) throw new Exception("缺少模板时应静默跳过");
if(Directory.Exists(Path.GetDirectoryName(missingTemplate))) throw new Exception("跳过模板不应创建目录");
Console.WriteLine("PASS：模板不存在时自动跳过，不生成文件或目录。");

SnapshotChecks.Run(args[1]);
if(args.Length>2) TemplateDedupChecks.Run(args[2]);
