using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Un impacto de una carta de ataque (daño, escudo, espejo o rebote). Lo arma el servidor y viaja junto al
    /// informe del turno (NotifyTurnResolvedRpc); Index lo asocia con su ResolutionDto.
    /// </summary>
    public struct HitDto : INetworkSerializable
    {
        public int Index;     // posición de la carta en el arreglo de ResolutionDto
        public int Slot;      // barco afectado
        public int X, Y;      // celda del impacto
        public int Kind;      // Gameplay.HitKind
        public int Damage;
        public int Destroyed; // 1 = este impacto hundió al barco

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Index);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref Destroyed);
        }
    }
}
