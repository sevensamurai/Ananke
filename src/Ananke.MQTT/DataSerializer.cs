using MessagePack;
using MessagePack.Resolvers;

namespace Ananke.MQTT;

/// <summary>
/// Binary serializer using MessagePack for efficient MQTT payloads.
/// </summary>
internal static class DataSerializer
{
    public static byte[] Serialize<T>(T item)
    {
        return MessagePackSerializer.Serialize(item, ContractlessStandardResolver.Options);
    }

    public static T? Deserialize<T>(byte[]? data)
    {
        if (data is null || data.Length == 0) return default;
        return MessagePackSerializer.Deserialize<T>(data, ContractlessStandardResolver.Options);
    }
}
