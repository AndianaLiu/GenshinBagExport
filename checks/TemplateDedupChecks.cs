using System.IO;
namespace GenshinBagExport;
internal static class TemplateDedupChecks
{
    public static void Run(string original)
    {
        string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(original))!,"runs","template-dedup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string template=Path.Combine(dir,"素材.xlsx"),state=Path.Combine(dir,"state");
        File.Copy(original,template);
        DateTime time=new(2026,10,19,15,0,0);
        List<BagRow> Rows(long count)=>[new(113003,"东风之翎","角色与武器培养素材","角色培养素材",5,count)];
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        var first=TemplateExport.WriteIfChanged(Rows(3),"900000001",state,template,time);
        Check(first!=null && File.Exists(first),"首次读取未生成副本");
        var unchanged=TemplateExport.WriteIfChanged(Rows(3),"900000001",state,template,time.AddSeconds(1));
        Check(unchanged==null && Directory.GetFiles(dir,"素材_*.xlsx").Length==1,"同库存重复生成副本");
        var changed=TemplateExport.WriteIfChanged(Rows(4),"900000001",state,template,time.AddSeconds(2));
        Check(changed!=null,"库存变化未生成副本");
        var otherUid=TemplateExport.WriteIfChanged(Rows(4),"900000002",state,template,time.AddSeconds(3));
        Check(otherUid!=null,"不同 UID 未隔离");
        var reverted=TemplateExport.WriteIfChanged(Rows(3),"900000001",state,template,time.AddSeconds(4));
        Check(reverted!=null,"库存变化回历史状态应生成新副本");
        File.Delete(reverted!);
        Check(TemplateExport.WriteIfChanged(Rows(3),"900000001",state,template,time.AddSeconds(5))!=null,"副本被删除后应重新生成");
        Check(TemplateExport.WriteIfChanged(Rows(3),"900000001",state,Path.Combine(dir,"missing.xlsx"),time)==null,"缺少模板应跳过");
        Console.WriteLine("PASS：archive 相同库存跳过、变化生成、UID 隔离、历史状态切换及副本删除恢复。");
    }
}
