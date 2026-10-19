using System.IO;
namespace GenshinBagExport;
public static class MaterialPacket
{
    // 顶层字段号随版本变化；根据 wire type 寻找 Item 子消息。
    public static List<object> Parse(byte[] bytes)
    {
        var output = new List<object>();
        var top = new ProtoReader(bytes);
        while (top.Next(out _, out int wire))
        {
            if (wire != 2) { top.Skip(wire); continue; }
            var item = new ProtoReader(top.Bytes());
            ulong id = 0, count = 0;
            bool material = false;
            while (item.Next(out int field, out int innerWire))
            {
                if (field == 1 && innerWire == 0) id = item.Varint();
                else if (field == 5 && innerWire == 2)
                {
                    var detail = new ProtoReader(item.Bytes());
                    material = true;
                    while (detail.Next(out int sub, out int subWire))
                        if (sub == 1 && subWire == 0) count = detail.Varint(); else detail.Skip(subWire);
                }
                else item.Skip(innerWire);
            }
            if (material)
            {
                if (id == 0 || id > uint.MaxValue || count > uint.MaxValue) throw new InvalidDataException("材料字段超出范围。");
                output.Add(new { item_id = (uint)id, kind = "material", info = new { count = (long)count } });
            }
        }
        return output;
    }
}
internal sealed class ProtoReader(byte[] data)
{
    private int offset;
    public bool Next(out int field, out int wire)
    {
        if (offset == data.Length) { field = wire = 0; return false; }
        ulong tag = Varint();
        field = checked((int)(tag >> 3)); wire = (int)(tag & 7);
        if (field == 0) throw new InvalidDataException("无效 Protobuf 字段。");
        return true;
    }
    public ulong Varint()
    {
        ulong result = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            if (offset >= data.Length) throw new EndOfStreamException("Protobuf 数据截断。");
            byte b = data[offset++];
            if (shift == 63 && b > 1) throw new InvalidDataException("Varint 溢出。");
            result |= (ulong)(b & 127) << shift;
            if (b < 128) return result;
        }
        throw new InvalidDataException("Varint 无效。");
    }
    public byte[] Bytes()
    {
        int length = checked((int)Varint());
        if (length > data.Length - offset) throw new EndOfStreamException("Protobuf 字段截断。");
        byte[] result = data.AsSpan(offset, length).ToArray(); offset += length; return result;
    }
    public void Skip(int wire)
    {
        switch (wire)
        {
            case 0: Varint(); return;
            case 2: Bytes(); return;
            case 1: Advance(8); return;
            case 5: Advance(4); return;
            default: throw new InvalidDataException("不支持的 Protobuf wire type。");
        }
    }
    private void Advance(int count)
    {
        if (count > data.Length - offset) throw new EndOfStreamException();
        offset += count;
    }
}
