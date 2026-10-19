using System.Text.Json;
using System.Text.Encodings.Web;
namespace GenshinBagExport;

public static class SnapshotJson
{
    public static string Serialize(List<BagRow> rows, string? sourceJson)
    {
        var snapshot = new Dictionary<string,object?>();
        if(sourceJson != null)
        {
            using var doc = JsonDocument.Parse(sourceJson);
            if(doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach(var key in new[]{"uid","capturedAt","gameVersion"})
                    if(doc.RootElement.TryGetProperty(key,out var value)) snapshot[key]=value.Clone();
        }
        snapshot["items"] = rows.Select(row => new { item_id=row.Id, kind="material", info=new {count=row.Count} }).ToArray();
        return JsonSerializer.Serialize(snapshot,new JsonSerializerOptions {WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
    }
}
