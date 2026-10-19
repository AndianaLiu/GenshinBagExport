# 开发说明

## 结构

| 文件 | 职责 |
| --- | --- |
| MainWindow.xaml / .cs | 界面与读写流程 |
| CaptureService.cs / MaterialPacket.cs | 游戏采集、管道通信与材料协议解析 |
| BagData.cs | 材料资料匹配及分类 |
| ExcelExport.cs | 分类 Excel 导出，无需安装 Excel |
| SnapshotJson.cs / SnapshotStore.cs | JSON、快照去重及每个 UID 的保留策略 |
| Data / Native / THIRD_PARTY | 资料、采集配置、组件及第三方声明 |
| checks | 回归检查、公开版隐私检查与图标生成脚本 |

## 构建

在项目目录运行，需 .NET 9 SDK：

```powershell
dotnet publish -c Release -o release/default --no-self-contained -p:EnableTemplateCopy=false
```

发布时打包整个 `release/default`。`LocalSettings.json`、私有模板、测试产物及发布目录均不提交。

发布目录自带程序依赖与资料，可单独复制、移动；运行仍需安装 .NET 9 桌面运行时。

## 验证

```powershell
dotnet run --project checks/Checks.csproj -- Data/sample.json checks/sample-export.xlsx
pwsh -NoProfile -STA -File checks/check-public-build.ps1
python checks/audit-public.py
```

隐私检查使用本地 `LocalSettings.json` 作为对照。采集配置与资料来源记录在 `Data/source-info.json`，离线检查不能代替真实游戏采集验证。

## 图标与快捷方式

`Assets/Keqing.ico` 同时用于程序和窗口图标。修改 `Assets/Keqing.png` 后，使用已安装 Pillow 的 Python 运行 `checks/build-icon.py`，再重新构建。

需要快捷方式时手动运行 `Create-Shortcuts.ps1 -Destination <目标目录> -Edition default`。构建不会创建或更新桌面快捷方式。
