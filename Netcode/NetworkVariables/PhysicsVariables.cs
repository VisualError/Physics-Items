using System;
using System.Xml.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Physics_Items.Netcode.NetworkVariables
{
    public struct PhysicsVariables : INetworkSerializable, IEquatable<PhysicsVariables>
    {
        public bool IsKinematic;
        public bool isPlaced;
        public Vector3 speed;
        public bool Equals(PhysicsVariables other)
        {
            return other.IsKinematic == IsKinematic && other.speed == speed && other.isPlaced == isPlaced; // idk how this works >:3
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            switch (serializer.IsReader)
            {
                case true:
                    var reader = serializer.GetFastBufferReader();
                    reader.ReadValueSafe(out IsKinematic);
                    reader.ReadValueSafe(out isPlaced);
                    reader.ReadValueSafe(out speed);
                    break;
                case false:
                    var writer = serializer.GetFastBufferWriter();
                    writer.WriteValueSafe(IsKinematic);
                    writer.WriteValueSafe(isPlaced);
                    writer.WriteValueSafe(speed);
                    break;
            }
        }
    }
}
