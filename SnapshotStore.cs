using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenshinBagExport;

public static class SnapshotStore
{
    private const int KeepCount = 10;
    private const string IndexName = ".snapshot-index.json";
    private static readonly object Gate = new();
    private sealed record Entry(string Uid, string File, string Hash, DateTimeOffset LastSeenUtc);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly Regex LegacyName = new(@"^bag-([0-9]+)-([0-9]{8}-[0-9]{6})-[a-fA-F0-9]{32}[.]json$", RegexOptions.CultureInvariant);

    public static DateTime ChinaNow => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time");
    public static string FileName(string? uid, DateTime? time = null)
    {
        var label = ValidUid(uid) ? uid : "未知UID";
        return $"背包_{label}_{time ?? ChinaNow:yyyyMMdd_HHmmss}.json";
    }
    public static string? UidFromJson(string? json)
    {
        if (json == null) return null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("uid", out var value)) return null;
        string? uid = value.ValueKind == JsonValueKind.String ? value.GetString() :
            value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return ValidUid(uid) ? uid : null;
    }
    private static bool ValidUid(string? uid) => !string.IsNullOrEmpty(uid) && uid.All(char.IsAsciiDigit);

    // 只有自动生成并登记的文件、以及严格匹配旧版自动命名的文件参与清理。
    public static string Save(string directory, string uid, string json, List<BagRow> rows, DateTime? time = null)
    {
        if (!ValidUid(uid) || UidFromJson(json) != uid) throw new InvalidDataException("快照 UID 与读取数据不一致");
        lock (Gate)
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var indexPath = Path.Combine(directory, IndexName);
            var entries = ReadIndex(indexPath);
            ImportLegacy(directory, uid, entries);
            entries = entries.Where(e => e.Uid != uid || IsUnchanged(directory, e)).ToList();
            var hash = Fingerprint(rows);
            var now = new DateTimeOffset(DateTime.SpecifyKind(time ?? ChinaNow, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToUniversalTime();
            var existing = entries.Where(e => e.Uid == uid && e.Hash == hash).OrderByDescending(e => e.LastSeenUtc).FirstOrDefault();
            string path;
            if (existing != null)
            {
                path = SafePath(directory, existing.File)!;
                entries.Remove(existing);
                entries.Add(existing with { LastSeenUtc = now });
            }
            else
            {
                var stamp = time ?? ChinaNow;
                path = Path.Combine(directory, FileName(uid, stamp));
                while (File.Exists(path)) { stamp = stamp.AddSeconds(1); path = Path.Combine(directory, FileName(uid, stamp)); }
                AtomicWrite(path, json, overwrite: false);
                entries.Add(new Entry(uid, Path.GetFileName(path), hash, now));
            }
            // 先成功保存索引，再清理旧文件。不会因清理失败损失本次读取的数据。
            WriteIndex(indexPath, entries);
            var sorted = entries.Where(e => e.Uid == uid).OrderByDescending(e => e.File == Path.GetFileName(path)).ThenByDescending(e => e.LastSeenUtc).ThenByDescending(e => e.File, StringComparer.Ordinal).ToList();
            var seen = new HashSet<string>();
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in sorted)
                if (seen.Add(entry.Hash) && retained.Count < KeepCount) retained.Add(entry.File);
            foreach (var entry in sorted.Where(e => !retained.Contains(e.File)))
            {
                try
                {
                    if (IsUnchanged(directory, entry))
                        File.Delete(SafePath(directory, entry.File)!);
                    entries.Remove(entry);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try { WriteIndex(indexPath, entries); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // 旧版自动文件在成功保存后统一命名；不处理未登记的手动导出文件。
            foreach (var entry in entries.Where(e => e.Uid == uid && LegacyName.IsMatch(e.File)).ToList())
            {
                if (!IsUnchanged(directory, entry)) continue;
                var legacy = LegacyName.Match(entry.File);
                if (!DateTime.TryParseExact(legacy.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)) continue;
                var oldPath = SafePath(directory, entry.File)!;
                var newPath = Path.Combine(directory, FileName(uid, stamp));
                while (File.Exists(newPath)) { stamp = stamp.AddSeconds(1); newPath = Path.Combine(directory, FileName(uid, stamp)); }
                var renamed = entry with { File = Path.GetFileName(newPath) };
                bool moved = false;
                try
                {
                    File.Move(oldPath, newPath, false);
                    moved = true;
                    entries.Remove(entry); entries.Add(renamed);
                    WriteIndex(indexPath, entries);
                    if (path == oldPath) path = newPath;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    entries.Remove(renamed);
                    if (!entries.Contains(entry)) entries.Add(entry);
                    if (moved)
                    {
                        try { File.Move(newPath, oldPath, false); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            return path;
        }
    }
    public static void MarkManualExport(string directory, string file)
    {
        directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        file=Path.GetFullPath(file);
        if (!string.Equals(Path.GetDirectoryName(file),directory,StringComparison.OrdinalIgnoreCase)) return;
        lock(Gate)
        {
            var indexPath=Path.Combine(directory,IndexName);
            if(!File.Exists(indexPath)) return;
            var entries=ReadIndex(indexPath);
            if(entries.RemoveAll(e=>string.Equals(e.File,Path.GetFileName(file),StringComparison.OrdinalIgnoreCase))>0)
                WriteIndex(indexPath,entries);
        }
    }
    internal static string Fingerprint(List<BagRow> rows)
    {
        // 名称、顺序、采集时间、JSON 缩进均不影响相同库存的判断。
        var canonical = string.Join(";", rows.GroupBy(x => x.Id).Select(g => (Id: g.Key, Count: g.Sum(x => x.Count)))
            .Where(x => x.Count != 0).OrderBy(x => x.Id).Select(x => x.Id.ToString(CultureInfo.InvariantCulture) + ":" + x.Count.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static List<Entry> ReadIndex(string path)
    {
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? []; }
        catch (JsonException) { return []; }
    }
    private static void WriteIndex(string path, List<Entry> entries) =>
        AtomicWrite(path, JsonSerializer.Serialize(entries, Options), overwrite: true);
    private static void AtomicWrite(string path, string text, bool overwrite)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, overwrite); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static string? SafePath(string directory, string? name)
    {
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || Path.IsPathRooted(name)) return null;
        var full = Path.GetFullPath(Path.Combine(directory, name));
        if (!full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return null;
        return full;
    }
    private static bool IsUnchanged(string directory, Entry entry)
    {
        if (!ValidUid(entry.Uid) || string.IsNullOrEmpty(entry.Hash)) return false;
        bool managed = Regex.IsMatch(entry.File ?? "", @"^背包_" + Regex.Escape(entry.Uid) + @"_[0-9]{8}_[0-9]{6}[.]json$") ||
            LegacyName.Match(entry.File ?? "") is { Success: true } legacy && legacy.Groups[1].Value == entry.Uid;
        if (!managed) return false;
        var path = SafePath(directory, entry.File);
        if (path == null || !File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            return UidFromJson(json) == entry.Uid && Fingerprint(BagData.LoadJson(json)) == entry.Hash;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or OverflowException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }
    private static void ImportLegacy(string directory, string uid, List<Entry> entries)
    {
        var known = entries.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "bag-*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            var match = LegacyName.Match(name);
            if (!match.Success || match.Groups[1].Value != uid || known.Contains(name) || SafePath(directory, name) == null) continue;
            try
            {
                var json = File.ReadAllText(path);
                if (UidFromJson(json) != uid) continue;
                if (!DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)) continue;
                entries.Add(new Entry(uid, name, Fingerprint(BagData.LoadJson(json)), new DateTimeOffset(stamp, TimeSpan.FromHours(8)).ToUniversalTime()));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or OverflowException or InvalidOperationException or KeyNotFoundException) { }
        }
    }
}
