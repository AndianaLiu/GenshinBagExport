# Genshin Bag Export

<img src="Assets/Keqing.png" width="96" alt="Genshin Bag Export">

原神背包导出工具，将背包材料整理为一个 Excel 文件。

**v7.1.0 · 适配原神国服官服 7.1.0 · 基于 TeyvatGuide 0.12.5 资料与采集配置**

## 功能

- 读取游戏背包，支持 JSON 导入、导出与分类预览。
- 材料按分类分别导出到不同 Sheet。
- 自动保存快照，每个 UID 保留最近 10 份不同库存。

## 使用

需要 Windows x64 和 [.NET 9 桌面运行时](https://dotnet.microsoft.com/download/dotnet/9.0)。

1. 解压完整程序目录，以管理员身份运行 `GenshinBagExport.exe`。
2. 关闭游戏，选择游戏本体 `YuanShen.exe`，填写 UID。
3. 点击“读取背包”，登录进入游戏，等待读取完成后导出 Excel 或 JSON。

也可直接点击“读取JSON”打开已有数据。快照位于 `%LOCALAPPDATA%\GenshinBagExporter\Snapshots`。

## 开发

使用 .NET 9 SDK，构建方法与代码结构见 [开发说明](DEVELOPMENT.md)。

## 致谢

- [TeyvatGuide](https://github.com/BTMuli/TeyvatGuide)：材料资料、分类及采集实现参考，见 [MIT 声明](THIRD_PARTY/TeyvatGuide-LICENSE)。
- [Yae](https://github.com/HolographicHat/Yae)：背包采集组件，见 [GPL-3.0 声明](THIRD_PARTY/Yae-LICENSE)。
