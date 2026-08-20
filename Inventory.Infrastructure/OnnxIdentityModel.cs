using System.Text;

namespace Inventory.Infrastructure;

/// <summary>
/// Minimal ONNX Identity (float[1] X → Y) written as protobuf so the repo does not download models.
/// </summary>
internal static class OnnxIdentityModel
{
    public static byte[] Create()
    {
        var node = Concat(
            StringField(1, "X"),
            StringField(2, "Y"),
            StringField(3, "id"),
            StringField(4, "Identity"));

        var dim = VarintField(1, 1);
        var shape = LengthField(1, dim);
        var tensorType = Concat(VarintField(1, 1), LengthField(2, shape));
        var type = LengthField(1, tensorType);
        var input = Concat(StringField(1, "X"), LengthField(2, type));
        var output = Concat(StringField(1, "Y"), LengthField(2, type));

        var graph = Concat(
            LengthField(1, node),
            StringField(2, "identity"),
            LengthField(11, input),
            LengthField(12, output));

        var opset = VarintField(2, 13);
        return Concat(
            VarintField(1, 8),
            StringField(2, "SpringClinicInventory"),
            LengthField(7, graph),
            LengthField(8, opset));
    }

    private static byte[] StringField(int field, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return LengthField(field, utf8);
    }

    private static byte[] VarintField(int field, long value) =>
        Concat(Key(field, 0), Varint((ulong)value));

    private static byte[] LengthField(int field, byte[] payload) =>
        Concat(Key(field, 2), Varint((ulong)payload.Length), payload);

    private static byte[] Key(int field, int wire) => Varint((ulong)((field << 3) | wire));

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var buffer = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
            offset += part.Length;
        }

        return buffer;
    }
}
