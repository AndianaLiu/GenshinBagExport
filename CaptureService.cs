using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace GenshinBagExport;

public sealed class CaptureService
{
    private static readonly uint[] Props = [10015, 10016, 10025, 10042, 10022, 10023, 10026, 10053, 10043, 10058];
    public async Task<string> CaptureAsync(string exe, string uid, IProgress<string> progress, CancellationToken token)
    {
        if (!Environment.Is64BitProcess) throw new InvalidOperationException("采集需要 x64 进程。");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("请关闭工具，右键程序选择“以管理员身份运行”后重试。");
        if (!File.Exists(exe) || !Path.GetFileName(exe).Equals("Yuanshen.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择国服游戏本体 Yuanshen.exe。");
        if (string.IsNullOrWhiteSpace(uid) || uid.Any(c => !char.IsAsciiDigit(c)))
            throw new InvalidOperationException("请输入数字 UID，UID 作为存档标签。");
        if (Process.GetProcessesByName("Yuanshen").Length != 0)
            throw new InvalidOperationException("请先关闭已运行的原神，再开始采集。");
        string iniPath = Path.Combine(Path.GetDirectoryName(exe)!, "config.ini");
        if (!File.Exists(iniPath) || !System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(iniPath),
                @"(?m)^\s*game_version\s*=\s*7\.1\.0\s*$"))
            throw new InvalidOperationException("当前配置仅验证过国服 7.1.0；游戏 config.ini 版本不匹配或无法读取。");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "teyvatguide-native-config.json")));
        var native = config.RootElement.GetProperty("nativeConfig");
        uint storeCmd = native.GetProperty("storeCmdId").GetUInt32();
        var rva = native.GetProperty("methodRva").GetProperty("chinese");
        var dll = Path.Combine(AppContext.BaseDirectory, "Native", "YaeAchievementLib.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("缺少 Yae DLL。", dll);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll)));
        if (hash != "03CA64C077E4A26BAFE7149FE587BD574C77F69AACEB5A3DF454B15FBFDA6AF3")
            throw new InvalidDataException("DLL 与已验证配置不匹配。");
        using var pipe = new NamedPipeServerStream("YaeAchievementPipe", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var cancelRead = timeout.Token.Register(() => pipe.Dispose());
        progress.Report("正在启动游戏并加载采集组件…");
        await Task.Run(() => NativeCapture.Start(exe, dll), token);
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            progress.Report("采集组件已连接，请登录并进入游戏。");
            return await Task.Run(() => Read(pipe, storeCmd, rva, uid, progress, timeout.Token), timeout.Token);
        }
        catch (ObjectDisposedException) when (timeout.IsCancellationRequested) { throw new OperationCanceledException("采集取消或超时，请关闭游戏后重试。"); }
        catch (IOException) when (timeout.IsCancellationRequested) { throw new OperationCanceledException("采集取消或超时，请关闭游戏后重试。"); }
    }

    private static string Read(Stream stream, uint storeCmd, JsonElement rva, string uid, IProgress<string> progress, CancellationToken token)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        var properties = new Dictionary<uint, double>();
        List<object>? items = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            switch (reader.ReadByte())
            {
                case 0xFA:
                    writer.Write(storeCmd); writer.Write(uint.MaxValue); writer.Flush(); break;
                case 0xFB:
                    foreach (var prop in Props) writer.Write(prop);
                    writer.Write(uint.MaxValue); writer.Flush(); break;
                case 0xFD:
                    writer.Write(0x12345678u);
                    foreach (var name in new[] { "updateNormalProp", "newString", "findGameObject", "eventSystemUpdate", "simulatePointerClick", "toInt32", "tcpStatePtr", "sharedInfoPtr", "decompress" })
                        writer.Write(rva.GetProperty(name).GetUInt32());
                    writer.Flush(); break;
                case 0xFE: progress.Report("等待游戏返回背包与货币数据…"); break;
                case 0x03:
                    uint property = reader.ReadUInt32();
                    double propValue = reader.ReadDouble();
                    if (!double.IsFinite(propValue)) throw new InvalidDataException("收到无效货币数量。");
                    properties[property] = propValue;
                    writer.Write(true); writer.Flush(); break;
                case 0x04:
                    ushort cmd = reader.ReadUInt16();
                    int length = reader.ReadInt32();
                    if (length < 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("背包数据包长度无效。");
                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length) throw new EndOfStreamException("背包数据不完整。");
                    if (cmd != storeCmd) throw new InvalidDataException("收到未请求的协议包。");
                    items = MaterialPacket.Parse(bytes);
                    writer.Write(true); writer.Flush();
                    progress.Report($"背包已读取：{items.Count} 条材料，等待货币数据…"); break;
                case 0xFF:
                    if (items == null || Props.Any(p => !properties.ContainsKey(p)))
                        throw new InvalidDataException("采集结束但背包或货币数据不完整。");
                    foreach (var (id, prop, subtract) in new (uint, uint, uint)[] { (201,10015,10022), (202,10016,10023), (203,10025,10026), (204,10042,10043), (206,10053,0), (207,10058,0) })
                    {
                        double value = properties[prop] - (subtract == 0 ? 0 : properties[subtract]);
                        if (value < 0 || value > long.MaxValue || value != Math.Truncate(value)) throw new InvalidDataException("货币数量无效。");
                        items.Add(new { item_id = id, kind = "material", info = new { count = (long)value } });
                    }
                    writer.Write(true); writer.Flush();
                    return JsonSerializer.Serialize(new { uid, capturedAt = DateTimeOffset.Now, gameVersion = "7.1.0", items }, new JsonSerializerOptions { WriteIndented = true });
                default: throw new InvalidDataException("收到不支持的采集命令。");
            }
        }
    }
}

internal static class NativeCapture
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string? reserved, desktop, title;
        public int x,y,xSize,ySize,xCount,yCount,fill,flags; public short show,reservedSize;
        public nint reservedPtr,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public nint process,thread; public uint pid,tid; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool CreateProcessW(string app, StringBuilder? command, nint pa, nint ta, bool inherit, uint flags, nint env, string cwd, ref StartupInfo si, out ProcessInfo pi);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern nint VirtualAllocEx(nint process,nint address,nuint size,uint type,uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFreeEx(nint process,nint address,nuint size,uint type);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool WriteProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint written);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern nint CreateRemoteThread(nint process,nint attributes,nuint stack,nint start,nint arg,uint flags,nint id);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true, SetLastError=true)] private static extern nint GetProcAddress(nint module,string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern nint LoadLibraryExW(string name,nint file,uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle,uint ms);
    private static void Check(bool condition) { if (!condition) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static void Run(nint process,nint start,nint arg)
    {
        nint thread = CreateRemoteThread(process,0,0,start,arg,0,0);
        Check(thread != 0);
        try { if (WaitForSingleObject(thread,30000) != 0) throw new TimeoutException("采集组件初始化超时，请关闭游戏后重试。"); }
        finally { CloseHandle(thread); }
    }
    public static void Start(string exe,string dll)
    {
        var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        Check(CreateProcessW(exe,null,0,0,false,0,0,Path.GetDirectoryName(exe)!,ref si,out var pi));
        nint remote = 0;
        bool loaderFinished = false;
        try
        {
            byte[] path = Encoding.Unicode.GetBytes(dll + "\0");
            remote = VirtualAllocEx(pi.process,0,(nuint)path.Length,0x3000,4); Check(remote != 0);
            Check(WriteProcessMemory(pi.process,remote,path,(nuint)path.Length,out var written) && written == (nuint)path.Length);
            nint loader = GetProcAddress(GetModuleHandleW("kernel32.dll"),"LoadLibraryW"); Check(loader != 0);
            Run(pi.process,loader,remote); loaderFinished = true;
            using var process = Process.GetProcessById((int)pi.pid);
            process.Refresh();
            var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName,Path.GetFileName(dll),StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("游戏未加载采集 DLL，请关闭游戏后重试。");
            nint local = LoadLibraryExW(dll,0,1); Check(local != 0);
            nint relative;
            try { nint entry = GetProcAddress(local,"YaeMain"); Check(entry != 0); relative = entry-local; }
            finally { FreeLibrary(local); }
            // YaeMain 会同步连接管道；独立线程由管道握手继续，不能在这里等待。
            nint thread = CreateRemoteThread(pi.process,0,0,module.BaseAddress+relative,module.BaseAddress,0,0);
            Check(thread != 0); CloseHandle(thread);
        }
        finally
        {
            if (remote != 0 && loaderFinished) VirtualFreeEx(pi.process,remote,0,0x8000);
            CloseHandle(pi.thread); CloseHandle(pi.process);
        }
    }
}
