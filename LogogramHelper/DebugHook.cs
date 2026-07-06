using System;
using System.Text;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LogogramHelper
{
    public unsafe class DebugHook : IDisposable
    {
        private delegate void FireCallbackDelegate(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close);
        private readonly Hook<FireCallbackDelegate> fireCallbackHook;

        public bool Enabled;

        public DebugHook()
        {
            var address = AtkUnitBase.Addresses.FireCallback.Value;
            fireCallbackHook = Plugin.GameInteropProvider.HookFromAddress<FireCallbackDelegate>(address, FireCallbackDetour);
            fireCallbackHook.Enable();
        }

        private void FireCallbackDetour(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close)
        {
            if (Enabled)
            {
                var name = thisPtr->NameString;
                if (name.Contains("Eureka") || name.Contains("Context") || name.Contains("Synthesis"))
                {
                    var sb = new StringBuilder();
                    sb.Append($"[LogogramHelper Debug] FireCallback addon={name} valueCount={valueCount}\n");
                    for (var i = 0; i < valueCount; i++)
                    {
                        var v = values[i];
                        sb.Append($"  [{i}] type={v.Type} int={v.Int}");
                        if (v.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String && v.String != null)
                            sb.Append($" str=\"{v.String}\"");
                        sb.Append('\n');
                    }
                    Plugin.Log.Info(sb.ToString());
                }
            }
            fireCallbackHook.Original(thisPtr, valueCount, values, close);
        }

        public void Dispose()
        {
            fireCallbackHook.Dispose();
        }
    }
}
