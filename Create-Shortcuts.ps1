param(
    [string]$Destination = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('archive','default')][string]$Edition = 'default'
)
$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot "release/$Edition"
$exe = Join-Path $folder 'GenshinBagExport-v7.1.0.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Missing application: $exe" }
$shell = New-Object -ComObject WScript.Shell
$path = Join-Path $Destination "Genshin Bag Export ($Edition).lnk"
$link = $shell.CreateShortcut($path)
$link.TargetPath = $exe
$link.WorkingDirectory = $folder
$link.IconLocation = "$exe,0"
$link.Description = "Genshin Bag Export v7.1.0 ($Edition)"
$link.Save()
# Remove expandable icon records so Shell uses the absolute IconLocation.
$bytes = [IO.File]::ReadAllBytes($path)
$flags = [BitConverter]::ToUInt32($bytes,20)
$offset = 76
if ($flags -band 1) { $offset += 2 + [BitConverter]::ToUInt16($bytes,$offset) }
if ($flags -band 2) { $offset += [BitConverter]::ToUInt32($bytes,$offset) }
$charSize = if ($flags -band 128) { 2 } else { 1 }
foreach ($bit in @(4,8,16,32,64)) {
    if ($flags -band $bit) { $offset += 2 + [BitConverter]::ToUInt16($bytes,$offset) * $charSize }
}
$stream = New-Object IO.MemoryStream
$stream.Write($bytes,0,$offset)
while ($offset + 4 -le $bytes.Length) {
    $size = [BitConverter]::ToUInt32($bytes,$offset)
    if ($size -eq 0) { break }
    if ($size -lt 8 -or $offset + $size -gt $bytes.Length) { throw 'Invalid shortcut block' }
    $signature = [BitConverter]::ToUInt32($bytes,$offset+4)
    if ($signature -ne 0xA0000007) { $stream.Write($bytes,$offset,$size) }
    $offset += $size
}
$stream.Write((New-Object byte[] 4),0,4)
$result = $stream.ToArray()
$stream.Dispose()
[BitConverter]::GetBytes([uint32]($flags -band (-bnot 0x4000))).CopyTo($result,20)
[IO.File]::WriteAllBytes($path,$result)
Write-Output $path
