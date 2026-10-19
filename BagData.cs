using System.IO;
using System.Text.Json;
namespace GenshinBagExport;
public record BagRow(long Id, string Name, string Category, string Type, int Star, long Count);
public static class BagData
{
    public static readonly string[] Categories = ["默认", "贵重物品", "角色与武器培养素材", "冒险道具", "区域特产", "任务道具", "圣物匣", "食材", "食物", "书籍", "素材", "消耗品", "小道具", "货币", "未识别物品"];
    private static readonly HashSet<long> CurrencyIds = [201, 202, 203, 204, 206, 207];
    public static List<BagRow> Load(string path) => LoadJson(File.ReadAllText(path));
    public static List<BagRow> LoadJson(string json)
    {
        using var catalogDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "material.json")));
        var catalog = catalogDoc.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("id").GetInt64(), x => x.Clone());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (Try(root, out var list, "ItemList", "itemList", "items")) root = list;
            else throw new InvalidDataException("JSON 须为物品数组，或包含 ItemList/items 的对象。");
        }
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("物品列表必须是数组。");
        var result = new List<BagRow>();
        foreach (var item in root.EnumerateArray())
        {
            if (!Try(item, out var idValue, "item_id", "ItemId", "itemId", "id")) throw new InvalidDataException("物品缺少 ID。");
            long id = Number(idValue);
            JsonElement countValue;
            bool hasCount = false;
            if (Try(item, out var kind, "kind"))
            {
                if (kind.GetString() is not ("material" or "virtual_item")) continue;
                if (Try(item, out var info, "info")) hasCount = Try(info, out countValue, "count", "Count");
                else countValue = default;
            }
            else if (Try(item, out var detail, "Material", "material", "VirtualItem", "virtualItem", "virtual_item"))
                hasCount = Try(detail, out countValue, "Count", "count");
            else
            {
                if (Try(item, out _, "Equip", "equip", "Furniture", "furniture", "Facility", "facility", "BeyondMaterial", "beyondMaterial")) continue;
                hasCount = Try(item, out countValue, "count", "Count");
            }
            if (!hasCount) throw new InvalidDataException($"物品 {id} 缺少数量。");
            var count = Number(countValue);
            if (count < 0) throw new InvalidDataException($"物品 {id} 数量不能为负数。");
            string name = $"未知物品 {id}", category = "未识别物品", type = "";
            int star = 0;
            if (catalog.TryGetValue(id, out var meta))
            {
                name = meta.GetProperty("name").GetString()!;
                type = meta.GetProperty("type").GetString()!;
                if (type is "角色成长" or "系统开放" or "好感成长") continue;
                category = meta.GetProperty("cType").GetString()!;
                star = meta.GetProperty("star").GetInt32();
            }
            if (CurrencyIds.Contains(id)) category = "货币";
            if (!Categories.Contains(category)) category = "未识别物品";
            result.Add(new(id, name, category, type, star, count));
        }
        return result.GroupBy(x => x.Id).Select(g => g.First() with { Count = g.Sum(x => x.Count) })
            .OrderBy(x => Array.IndexOf(Categories, x.Category)).ThenBy(x => x.Type).ThenBy(x => x.Id).ToList();
    }
    private static long Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? long.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : value.GetInt64();
    private static bool Try(JsonElement value, out JsonElement found, params string[] names)
    {
        foreach (var name in names) if (value.TryGetProperty(name, out found) && found.ValueKind != JsonValueKind.Null) return true;
        found = default; return false;
    }
}
