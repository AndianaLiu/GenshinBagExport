$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'release/default/GenshinBagExport-v7.1.0.dll'))
if($null -ne $assembly.GetType('GenshinBagExport.TemplateExport')){throw '公开版含模板代码'}
if($null -ne $assembly.GetType('GenshinBagExport.PrivateSettings')){throw '公开版含个人设置代码'}
$window=[GenshinBagExport.MainWindow]::new()
try {
 if($window.FindName('GamePath').Text -ne ''){throw '公开版默认游戏路径非空'}
 if($window.FindName('UidInput').Text -ne ''){throw '公开版默认 UID 非空'}
} finally {$window.Close()}
'PASS：default 实际界面路径与 UID 均为空，无个人设置和模板处理类'



