using System;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LogogramHelper
{
    public unsafe class DebugHook : IDisposable
    {
        private delegate void FireCallbackDelegate(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close);

        // Calls the real FireCallback directly via its resolved address instead of hooking it.
        // Hooking this globally-shared function was found to leave ContextMenu/AddonContextSub in
        // a stale state that closes unrelated dialogs (SelectYesno/InputNumeric) on the next click.
        // null＝位址解不出：自動填入停用，不讓插件載入失敗。
        private readonly FireCallbackDelegate? fireCallback;

        public bool IsAvailable => fireCallback != null;

        public DebugHook()
        {
            var address = AtkUnitBase.Addresses.FireCallback.Value;
            if (address == 0)
            {
                Plugin.Log.Warning("[LogogramHelper] 找不到 AtkUnitBase.FireCallback 的位址（遊戲改版後特徵碼對不上），自動填入融合器停用，其餘功能照常。");
                return;
            }
            fireCallback = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<FireCallbackDelegate>(address);
        }

        public bool Invoke(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close = false)
        {
            if (fireCallback == null) return false;
            fireCallback(thisPtr, valueCount, values, close);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
