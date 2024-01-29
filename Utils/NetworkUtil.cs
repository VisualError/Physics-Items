

using Unity.Netcode;

namespace Physics_Items.Utils
{
    internal class NetworkUtil
    {
        public static bool IsServerOrHost => NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
        public static bool ServerHasMod = false;
    }
}
