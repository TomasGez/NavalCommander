using System;
using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    public enum MatchPhase : byte { Lobby, Selecting, Resolving, Finished }

    /// <summary>Una carta jugada por un jugador. Aim: -1 = sin dirección, 0..3 = RelativeDirection.</summary>
    public struct PlayDto : INetworkSerializable
    {
        public FixedString32Bytes CardId;
        public int Aim;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref CardId);
            serializer.SerializeValue(ref Aim);
        }
    }

    /// <summary>Estado de un barco tras resolver (lo que ven todos los clientes).</summary>
    public struct ShipSnapshot : INetworkSerializable, IEquatable<ShipSnapshot>
    {
        public int Slot;
        public int X;
        public int Y;
        public int Facing;
        public int Health;

        public bool Equals(ShipSnapshot o) =>
            Slot == o.Slot && X == o.X && Y == o.Y && Facing == o.Facing && Health == o.Health;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref Health);
        }
    }

    /// <summary>Qué carta se resolvió, de quién y en qué fase (para animaciones/feedback).</summary>
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

    /// <summary>Eventos locales para que la UI/visuales reaccionen sin depender del networking.</summary>
    public static class MatchEvents
    {
        public static event Action<string[]> HandReceived;
        public static event Action<ShipSnapshot[]> BoardReady;
        public static event Action<ShipSnapshot[], ResolutionDto[]> TurnResolved;

        public static void RaiseHand(string[] ids) => HandReceived?.Invoke(ids);
        public static void RaiseBoardReady(ShipSnapshot[] ships) => BoardReady?.Invoke(ships);
        public static void RaiseTurnResolved(ShipSnapshot[] ships, ResolutionDto[] res) => TurnResolved?.Invoke(ships, res);
    }
}
