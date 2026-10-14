using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>Qué carta se resolvió, de quién y con qué resultado (para feedback visual y logs).</summary>
    public struct ResolutionDto : INetworkSerializable
    {
        public int Slot;
        public FixedString32Bytes CardId;
        public int Phase;
        public int Outcome;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref CardId);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Outcome);
        }
    }
}
