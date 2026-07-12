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
        private readonly FireCallbackDelegate fireCallback;

        public DebugHook()
        {
            var address = AtkUnitBase.Addresses.FireCallback.Value;
            fireCallback = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<FireCallbackDelegate>(address);
        }

        public void Invoke(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close = false) =>
            fireCallback(thisPtr, valueCount, values, close);

        public void Dispose()
        {
        }
    }
}
