using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Una carta jugada por un cliente. Viaja SOLO al servidor (PlayerNet.SubmitPlaysRpc) y no es de fiar:
    /// MatchRules la valida (carta en la mano, dirección válida, sin repetir).
    /// </summary>
    public struct PlayDto : INetworkSerializable
    {
        public FixedString32Bytes CardId;
        public int Aim; // -1 = sin dirección, 0..3 = RelativeDirection

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref CardId);
            serializer.SerializeValue(ref Aim);
        }
    }
}
