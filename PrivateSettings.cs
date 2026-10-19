using System.IO;
using System.Text.Json;
namespace GenshinBagExport;

// 仅 archive 构建包含此类；个人设置保存在不提交的 LocalSettings.json。
internal sealed record PrivateSettings(string GamePath = "", string Uid = "", string TemplatePath = "")
{
    public static PrivateSettings Load()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"LocalSettings.json");
        if(!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<PrivateSettings>(File.ReadAllText(path),
            new JsonSerializerOptions {PropertyNameCaseInsensitive=true}) ?? new();
    }
}
