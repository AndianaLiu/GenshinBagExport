using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GenshinBagExport;

public partial class MainWindow : Window
{
    private List<BagRow> rows = [];
    private string? sourceJson;
    private CancellationTokenSource? captureLifetime;
    private bool busy;
    private static string SnapshotDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenshinBagExporter", "Snapshots");
    public MainWindow()
    {
        InitializeComponent();
#if TEMPLATE_COPY
        var personal=PrivateSettings.Load();
        GamePath.Text=personal.GamePath;
        UidInput.Text=personal.Uid;
#endif
        Category.ItemsSource = new[] { "全部" }.Concat(BagData.Categories);
        Category.SelectedIndex = 0;
    }
    protected override void OnClosed(EventArgs e) { captureLifetime?.Cancel(); base.OnClosed(e); }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "国服游戏本体|Yuanshen.exe" };
        if (dialog.ShowDialog() == true) GamePath.Text = dialog.FileName;
    }
    private void SetBusy(bool value)
    {
        busy=value;
        CaptureButton.IsEnabled=BrowseButton.IsEnabled=ImportButton.IsEnabled=GamePath.IsEnabled=UidInput.IsEnabled=!value;
        ExportButton.IsEnabled=ExportJsonButton.IsEnabled=!value && rows.Count>0;
        BusyProgress.Visibility=value ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if(busy) return;
        string exe=GamePath.Text.Trim(),uid=UidInput.Text.Trim();
        captureLifetime=new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var json=await new CaptureService().CaptureAsync(exe,uid,new Progress<string>(message=>Status.Text=message),captureLifetime.Token);
            var loaded=BagData.LoadJson(json);
            await Task.Run(()=>SnapshotStore.Save(SnapshotDirectory,uid,json,loaded));
            sourceJson=json;rows=loaded;
            Category.SelectedIndex=0;Refresh();
            await CreateTemplateCopyAsync();
        }
        catch(OperationCanceledException) { Status.Text="采集超时或窗口已关闭。请关闭游戏后重试；已有数据保留。"; }
        catch(Exception error) { Status.Text=$"采集失败：{error.Message}";MessageBox.Show(this,error.Message,"采集失败"); }
        finally { captureLifetime.Dispose();captureLifetime=null;SetBusy(false); }
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if(busy) return;
        Directory.CreateDirectory(SnapshotDirectory);
        var dialog=new OpenFileDialog {Filter="背包 JSON|*.json",InitialDirectory=SnapshotDirectory};
        if(dialog.ShowDialog()!=true) return;
        SetBusy(true);
        try
        {
            var json=await File.ReadAllTextAsync(dialog.FileName);
            var loaded=BagData.LoadJson(json);
            sourceJson=json;rows=loaded;
            Category.SelectedIndex=0;Refresh();
            await CreateTemplateCopyAsync();
        }
        catch(Exception error) {MessageBox.Show(this,error.Message,"读取失败");}
        finally {SetBusy(false);}
    }
    private async Task CreateTemplateCopyAsync()
    {
#if TEMPLATE_COPY
        var snapshot=rows.ToList();
        try
        {
            Status.Text="正在更新素材模板副本…";
            var uid=SnapshotStore.UidFromJson(sourceJson);
            await Task.Run(()=>TemplateExport.WriteIfChanged(snapshot,uid,SnapshotDirectory));

        }
        catch(Exception error)
        {
            Status.Text=$"数据读取成功，素材副本生成失败：{error.Message}；仍可导出 JSON 或 Excel。";
            MessageBox.Show(this,Status.Text,"素材副本未生成");
            return;
        }
#else
        await Task.CompletedTask;
#endif
        Status.Text=$"读取完成：{rows.Count}种材料及货币";
    }
    private void Category_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>Refresh();
    private void Refresh()
    {
        if(Grid==null || ItemCount==null) return;
        var selected=Category.SelectedItem as string;
        var visible=rows.Where(row=>selected=="全部" || row.Category==selected).ToList();
        Grid.ItemsSource=visible;
        ItemCount.Text=$"{visible.Count:N0} 种物品";
    }
    private async void ExportJson_Click(object sender,RoutedEventArgs e)
    {
        if(busy || rows.Count==0) return;
        Directory.CreateDirectory(SnapshotDirectory);
        var dialog=new SaveFileDialog {Filter="背包 JSON|*.json",DefaultExt=".json",FileName=SnapshotStore.FileName(SnapshotStore.UidFromJson(sourceJson)),InitialDirectory=SnapshotDirectory};
        if(dialog.ShowDialog()!=true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName,SnapshotJson.Serialize(rows,sourceJson));
            SnapshotStore.MarkManualExport(SnapshotDirectory,dialog.FileName);
            Status.Text=$"JSON 已导出：{dialog.FileName}";
        }
        catch(Exception error) {MessageBox.Show(this,error.Message,"导出失败");}
    }
    private void Export_Click(object sender,RoutedEventArgs e)
    {
        if(busy || rows.Count==0) return;
        var dialog=new SaveFileDialog {Filter="Excel 工作簿|*.xlsx",FileName=$"原神背包_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"};
        if(dialog.ShowDialog()!=true) return;
        try {ExcelExport.Write(dialog.FileName,rows);Status.Text=$"Excel 已导出：{dialog.FileName}";}
        catch(Exception error) {MessageBox.Show(this,error.Message,"导出失败");}
    }
}
