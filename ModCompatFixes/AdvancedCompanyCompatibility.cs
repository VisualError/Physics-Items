using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Physics_Items.ModCompatFixes
{
    internal static class AdvancedCompanyCompatibility
    {
        private static bool? _enabled;

        public static bool enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.potatoepet.AdvancedCompany");
                }
                return (bool)_enabled;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void ApplyFixes()
        {
            Plugin.Instance.moddedSkipList.Add(typeof(AdvancedCompany.Objects.LightningRod));
        }
    }
}
