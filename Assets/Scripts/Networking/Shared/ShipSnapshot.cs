using System;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>Estado de un barco tal como lo ven todos los clientes (se guarda en una NetworkList).</summary>
    public struct ShipSnapshot : INetworkSerializable, IEquatable<ShipSnapshot>
    {
        public int Slot;
        public int X;
        public int Y;
        public int Facing;
        public int Health;

        public bool Equals(ShipSnapshot other) =>
            Slot == other.Slot && X == other.X && Y == other.Y && Facing == other.Facing && Health == other.Health;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref Health);
        }
    }
}
