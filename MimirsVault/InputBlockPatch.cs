using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace MimirsVault
{
    /// <summary>
    /// While the sorter's search box has focus, stop typed letters from reaching the game
    /// (otherwise "E" would close the chest, "Tab" the inventory, WASD would move, ...).
    /// </summary>
    [HarmonyPatch]
    internal static class InputBlockPatch
    {
        private static readonly string[] Names = { "GetButton", "GetButtonDown", "GetButtonUp", "GetKey", "GetKeyDown", "GetKeyUp" };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(ZInput).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(bool) && Names.Contains(m.Name))
                .Cast<MethodBase>();
        }

        private static bool Prefix(ref bool __result)
        {
            if (!SorterBehaviour.SearchFocused) return true;
            __result = false;
            return false;
        }
    }
}
