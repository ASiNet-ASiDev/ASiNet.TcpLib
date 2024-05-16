using ASiNet.Data.Serialization.V2;

namespace ASiNet.TcpLib;
public class SerializerIndexer : ModelsIndexer<ushort>
{

    private ushort _index;

    public override ushort OnRegister(Type type)
    {
        _index++;
        return _index;
    }

    public override ushort ReadIndex(SerializerIO iO)
    {
        var buffer = (stackalloc byte[sizeof(ushort)]);
        iO.ReadBytes(buffer);
        return BitConverter.ToUInt16(buffer);
    }

    public override void WriteIndex(ushort key, SerializerIO iO)
    {
        var buffer = (stackalloc byte[sizeof(ushort)]);
        BitConverter.TryWriteBytes(buffer, key);
        iO.WriteBytes(buffer);
    }
}
