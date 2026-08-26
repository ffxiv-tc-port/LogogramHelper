using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LogogramHelper
{
    public static unsafe class SynthesisAutomation
    {
        // Determined from live FireCallback logs: (29, 1) focuses the 靈極融合器 (left slot),
        // (29, 0) focuses the 星極融合器 (right slot).
        public static bool SelectSynthesizer(bool starChart)
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("EurekaMagiciteItemSynthesis", 1);
            if (addon == null) return false;
            FireTwoInts(addon, 29, starChart ? 0 : 1);
            return true;
        }

        // (14, rowIndex) adds one unit of the shard at the given 1-based row of the
        // currently displayed (全部 tab) shard list to whichever synthesizer has focus.
        public static bool AddShard(int rowIndex)
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("EurekaMagiciteItemShardList", 1);
            if (addon == null) return false;
            FireTwoInts(addon, 14, rowIndex);
            return true;
        }

        private static void FireTwoInts(AtkUnitBase* addon, int a, int b)
        {
            var values = stackalloc AtkValue[2];
            values[0].Type = ValueType.Int;
            values[0].Int = a;
            values[1].Type = ValueType.Int;
            values[1].Int = b;
            Plugin.DebugHook.Invoke(addon, 2, values);
        }
    }
}
