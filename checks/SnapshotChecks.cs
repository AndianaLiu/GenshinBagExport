using System.IO;
using System.Text.Json;
namespace GenshinBagExport;
internal static class SnapshotChecks
{
    public static void Run(string output)
    {
        string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"runs","snapshots-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string uid="900000001", other="900000002";
        DateTime start=new(2026,10,19,12,0,0);
        List<BagRow> Rows(long count)=>[new(201,"原石","货币","稀有货币",5,count)];
        string Json(string owner,long count,int second)=>JsonSerializer.Serialize(new {uid=owner,capturedAt=start.AddSeconds(second).ToString("O"),items=new[]{new{item_id=201,kind="material",info=new{count}}}});
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        var first=SnapshotStore.Save(dir,uid,Json(uid,1,0),Rows(1),start);
        Check(Path.GetFileName(first)==$"背包_{uid}_20261019_120000.json","统一命名失败");
        var same=SnapshotStore.Save(dir,uid,Json(uid,1,1),Rows(1),start.AddSeconds(1));
        Check(first==same && Directory.GetFiles(dir,"背包_*.json").Length==1,"相同库存未去重");
        var otherFile=SnapshotStore.Save(dir,other,Json(other,777,0),Rows(777),start);
        var manual=Path.Combine(dir,SnapshotStore.FileName(uid,start.AddHours(1)));
        File.WriteAllText(manual,Json(uid,999,0));
        var malformed=Path.Combine(dir,$"bag-{uid}-20261018-120000-{Guid.NewGuid():N}.json");
        File.WriteAllText(malformed,"not json");
        for(int i=2;i<=12;i++) SnapshotStore.Save(dir,uid,Json(uid,i,i),Rows(i),start.AddSeconds(i));
        var entries=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,".snapshot-index.json")));
        Check(entries.RootElement.EnumerateArray().Count(e=>e.GetProperty("Uid").GetString()==uid)==10,"未按 UID 保留十份");
        Check(!File.Exists(first),"未删除超额旧快照");
        Check(File.Exists(otherFile) && File.Exists(manual) && File.Exists(malformed),"误删其他 UID、手动文件或损坏文件");
        SnapshotStore.Save(dir,uid,Json(uid,3,20),Rows(3),start.AddSeconds(20));
        SnapshotStore.Save(dir,uid,Json(uid,13,21),Rows(13),start.AddSeconds(21));
        using(var index=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,".snapshot-index.json"))))
        {
            var saved=index.RootElement.EnumerateArray().Where(e=>e.GetProperty("Uid").GetString()==uid)
                .Select(e=>BagData.Load(Path.Combine(dir,e.GetProperty("File").GetString()!))[0].Count).ToList();
            Check(saved.Count==10 && saved.Contains(3) && !saved.Contains(4),"重复状态重新读取后的保留顺序失败");
        }
        string protectedDir=Path.Combine(dir,"manual-overwrite");
        var protectedFile=SnapshotStore.Save(protectedDir,uid,Json(uid,1,0),Rows(1),start);
        SnapshotStore.MarkManualExport(protectedDir,protectedFile);
        for(int i=2;i<=13;i++) SnapshotStore.Save(protectedDir,uid,Json(uid,i,i),Rows(i),start.AddSeconds(i));
        Check(File.Exists(protectedFile),"手动覆盖的文件不应参与清理");
        string collisionDir=Path.Combine(dir,"collision");
        var a=SnapshotStore.Save(collisionDir,uid,Json(uid,1,0),Rows(1),start);
        var b=SnapshotStore.Save(collisionDir,uid,Json(uid,2,0),Rows(2),start);
        Check(a!=b && File.Exists(a) && File.Exists(b),"同秒不同快照发生覆盖");
        string legacyDir=Path.Combine(dir,"legacy");
        Directory.CreateDirectory(legacyDir);
        for(int i=1;i<=12;i++)
            File.WriteAllText(Path.Combine(legacyDir,$"bag-{uid}-{start.AddDays(-1).AddSeconds(i):yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"),Json(uid,i,i));
        SnapshotStore.Save(legacyDir,uid,Json(uid,12,30),Rows(12),start.AddSeconds(30));
        Check(Directory.GetFiles(legacyDir,"bag-*.json").Length==0,"旧自动快照未统一命名");
        Check(Directory.GetFiles(legacyDir,"背包_*.json").Length==10,"旧快照保留上限失败");
        Check(SnapshotStore.FileName(SnapshotStore.UidFromJson("[ ]"),start)=="背包_未知UID_20261019_120000.json","无 UID 命名失败");
        Console.WriteLine("PASS：统一命名、库存去重、每 UID 最近十份、旧快照迁移、手动文件保护及同秒防覆盖。");
    }
}
