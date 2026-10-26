using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Qué carta se resolvió, de quién y con qué resultado (para feedback visual y logs).
    /// Lo arma el servidor (MatchController.ToDto) y viaja a todos en NotifyTurnResolvedRpc, en el orden de resolución.
    /// </summary>
    public struct ResolutionDto : INetworkSerializable
    {
        public int Slot;
        public FixedString32Bytes CardId;
        public int Phase;
        public int Outcome;

        // Barco justo antes (From) y justo después (To) de la carta.
        public int FromX, FromY, FromFacing;
        public int ToX, ToY, ToFacing;

        // Celdas objetivo en línea: (TargetX, TargetY) + (StepX, StepY) * i, para i < TargetCount.
        // En un ataque son las celdas que alcanza; en un movimiento bloqueado, la celda a la que no pudo ir.
        public int TargetX, TargetY, StepX, StepY, TargetCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref CardId);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Outcome);
            serializer.SerializeValue(ref FromX);
            serializer.SerializeValue(ref FromY);
            serializer.SerializeValue(ref FromFacing);
            serializer.SerializeValue(ref ToX);
            serializer.SerializeValue(ref ToY);
            serializer.SerializeValue(ref ToFacing);
            serializer.SerializeValue(ref TargetX);
            serializer.SerializeValue(ref TargetY);
            serializer.SerializeValue(ref StepX);
            serializer.SerializeValue(ref StepY);
            serializer.SerializeValue(ref TargetCount);
        }
    }
}
