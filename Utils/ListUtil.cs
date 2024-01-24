using System.Collections.Generic;

namespace Physics_Items.Utils
{
    internal class ListUtil
    {
        public static T GetRandomElement<T>(List<T> objects, ref int index)
        {
            if (objects.Count == 0)
            {
                index = -1;
                return default;
            }
            index = UnityEngine.Random.Range(0, objects.Count);
            return objects[index];
        }
        public static T GetRandomElement<T>(List<T> objects)
        {
            int num = 0;
            return GetRandomElement<T>(objects, ref num);
        }
    }
}
