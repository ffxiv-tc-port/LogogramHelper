using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.IO;
using Dalamud.Interface.Windowing;
using LogogramHelper.Windows;
using Dalamud.Game.Gui;
using System.Collections.Generic;
using Newtonsoft.Json;
using Dalamud.Data;
using LogogramHelper.Classes;
using System.Linq;
using System;
using Dalamud.Plugin.Services;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace LogogramHelper
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Logogram Helper";

        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static IGameGui GameGui { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
        [PluginService] public static IPluginLog Log { get; private set; } = null!;

        public WindowSystem WindowSystem = new("LogogramHelper");
        public MainWindow MainWindow { get; init; }
        public LogosWindow LogosWindow { get; init; }
        public static DebugHook DebugHook { get; private set; } = null!;

        internal List<LogosAction> LogosActions;
        internal IDictionary<int, Logogram> Logograms;
        internal IDictionary<int, uint> LogogramIcons;
        internal IDictionary<int, int> LogogramRowIndex;
        internal IDictionary<ulong, LogogramItem> LogogramItems;
        internal IDictionary<int, int> LogogramStock = new Dictionary<int, int>();
        internal Configuration Configuration = null!;
        internal HashSet<string> FillHistory => Configuration.FillHistory;
        internal List<Preset> Presets => Configuration.Presets;
        internal (string ActionName, int RecipeIdx)? CurrentLunarSelection { get; private set; }
        internal (string ActionName, int RecipeIdx)? CurrentStarSelection { get; private set; }

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            LoadData();

            DebugHook = new DebugHook();
            MainWindow = new MainWindow(this);
            LogosWindow = new LogosWindow(this);

            WindowSystem.AddWindow(MainWindow);
            WindowSystem.AddWindow(LogosWindow);

            PluginInterface.UiBuilder.Draw += DrawUI;

            AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "ItemDetail", ItemDetailOnUpdate);

            stopAllProvider = PluginInterface.GetIpcProvider<string, object>(StopAllIpcName);
            stopAllProvider.RegisterAction(StopAll);
        }

        // 艦隊急停：Action<string>，參數是呼叫端署名；與 BOCCHI.StopAll／HoardFarm.StopAll 同形。名字與型別是對外契約。
        private const string StopAllIpcName = "LogogramHelper.StopAll";
        private readonly ICallGateProvider<string, object> stopAllProvider;

        // 自動填入融合器在按下按鈕的那一幀於主執行緒同步做完，沒有排程中或待命的動作可停，所以只記一行；任何執行緒呼叫都安全。
        private static void StopAll(string? source)
        {
            var label = string.IsNullOrWhiteSpace(source) ? "?" : source.Trim();
            try
            {
                Log.Information($"[LogogramHelper] 收到急停（{label}）：沒有進行中的自動化（自動填入融合器只在按下按鈕的那一幀內執行），不需停止；設定不變。");
            }
            catch (Exception)
            {
                // 卸載期記不了 log 也不能擲回呼叫端。
            }
        }

        public void Dispose()
        {
            stopAllProvider?.UnregisterAction();
            this.WindowSystem.RemoveAllWindows();
            DebugHook.Dispose();
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
            var addonPtr = GameGui.GetAddonByName("EurekaMagiciteItemSynthesis", 1);
            if (addonPtr != IntPtr.Zero)
                MainWindow.IsOpen = true;
            else
            {
                if (MainWindow.IsOpen) MainWindow.IsOpen = false;
                if (LogosWindow.IsOpen) LogosWindow.IsOpen = false;
            }

        }

        private void LoadData()
        {

            using var logogramReader = new StreamReader(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "logograms.json"));
            var logogramJson = logogramReader.ReadToEnd();
            var Logos = JsonConvert.DeserializeObject<List<Logogram>>(logogramJson);
            Logograms = Logos.ToDictionary(keySelector: l => l.Id, elementSelector: l => l);
            // Row index (1-based) within the shard list's default "全部" tab ordering, matching
            // the order logograms.json entries are listed in.
            LogogramRowIndex = Logos.Select((l, i) => (l.Id, Row: i + 1)).ToDictionary(x => x.Id, x => x.Row);
            logogramReader.Close();

            using var itemReader = new StreamReader(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "itemContents.json"));
            var itemJson = itemReader.ReadToEnd();
            var items = JsonConvert.DeserializeObject<List<LogogramItem>>(itemJson);
            LogogramItems = items.ToDictionary(keySelector: i => i.Id, elementSelector: i => i);
            itemReader.Close();

            using var r = new StreamReader(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "logosActions.json"));
            var logosJson = r.ReadToEnd();
            LogosActions = JsonConvert.DeserializeObject<List<LogosAction>>(logosJson);
            r.Close();

            LogogramIcons = Logograms.Values.ToDictionary(
                keySelector: l => l.Id,
                elementSelector: l => LogosActions.First(a => a.Name == l.Name).IconID);
        }

        public void DrawLogosDetailUI(LogosAction action)
        {
            LogosWindow.SetDetails(action);
            LogosWindow.IsOpen = true;
        }

        internal static string HistoryKey(string actionName, int recipeIdx, bool starChart) => $"{actionName}#{recipeIdx}#{(starChart ? "star" : "lunar")}";

        internal bool HasFillHistory(string actionName) => FillHistory.Any(key => key.StartsWith($"{actionName}#"));

        internal void FillSynthesizer(LogosAction action, int recipeIdx, bool starChart)
        {
            var recipe = action.Recipes[recipeIdx];
            if (!SynthesisAutomation.SelectSynthesizer(starChart)) return;
            foreach (var item in recipe)
            {
                if (!LogogramRowIndex.TryGetValue(item.LogogramID, out var row)) continue;
                for (var q = 0; q < item.Quantity; q++)
                {
                    // An unexpected confirmation/quantity dialog (e.g. NumberInputDialog) can pop up
                    // mid-sequence; continuing to blast further synthetic FireCallback events while it's
                    // open ends up hitting its buttons instead of the shard list, closing it out from
                    // under the player. Bail out and let the player finish manually if that happens.
                    if (GameGui.GetAddonByName("NumberInputDialog", 1) != IntPtr.Zero)
                    {
                        Log.Warning("FillSynthesizer: unexpected NumberInputDialog detected, aborting automation.");
                        return;
                    }
                    SynthesisAutomation.AddShard(row);
                }
            }
            FillHistory.Add(HistoryKey(action.Name, recipeIdx, starChart));
            if (starChart)
                CurrentStarSelection = (action.Name, recipeIdx);
            else
                CurrentLunarSelection = (action.Name, recipeIdx);
            Configuration.Save();
        }

        internal bool SavePreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || (CurrentLunarSelection == null && CurrentStarSelection == null)) return false;
            Presets.RemoveAll(p => p.Name == name);
            Presets.Add(new Preset
            {
                Name = name,
                LunarActionName = CurrentLunarSelection?.ActionName ?? "",
                LunarRecipeIdx = CurrentLunarSelection?.RecipeIdx ?? 0,
                StarActionName = CurrentStarSelection?.ActionName ?? "",
                StarRecipeIdx = CurrentStarSelection?.RecipeIdx ?? 0,
            });
            Configuration.Save();
            return true;
        }

        internal void ClearCurrentSelection()
        {
            CurrentLunarSelection = null;
            CurrentStarSelection = null;
        }

        internal void ApplyPreset(Preset preset)
        {
            var lunarAction = LogosActions.FirstOrDefault(a => a.Name == preset.LunarActionName);
            var starAction = LogosActions.FirstOrDefault(a => a.Name == preset.StarActionName);
            if (lunarAction != null) FillSynthesizer(lunarAction, preset.LunarRecipeIdx, false);
            if (starAction != null) FillSynthesizer(starAction, preset.StarRecipeIdx, true);
        }

        internal void DeletePreset(Preset preset)
        {
            Presets.Remove(preset);
            Configuration.Save();
        }

        private unsafe void ItemDetailOnUpdate(AddonEvent type, AddonArgs args)
        {
            var id = GameGui.HoveredItem;
            if (LogogramItems.ContainsKey(id))
            {
                var contentsId = LogogramItems[id].Contents;
                var contents = new List<string>();
                // The ContainsKey above guards LogogramItems (itemContents.json); the lookup below is against
                // Logograms (logograms.json), keyed by an id taken from the *value* of the first dictionary - different
                // files whose key spaces are equal only by convention. This runs on every item tooltip from an addon
                // hook, so degrade to the raw id rather than throwing while an item is hovered.
                contentsId.ForEach(content =>
                {
                    contents.Add(Logograms.TryGetValue(content, out var logogram)
                        ? Loc.T(logogram.Name)
                        : $"#{content}");
                });

                // 🔴 Framework.Instance() 是 [StaticAddress(..., isPointer: true)]，遊戲尚未建立單例時回 null；
                //    GetUIModule() / GetRaptureAtkModule() 同樣可能回 null。裸解參考 null 原生指標是 AVE，try/catch 攔不到。
                //    這支跑在道具 tooltip 的 addon hook 上（每次滑過道具都經過），取不到就放棄本次附註，
                //    走既有的 seStr == null 相同語意：tooltip 維持原樣，不崩潰。
                var framework = Framework.Instance();
                if (framework == null) return;
                var uiModule = framework->GetUIModule();
                if (uiModule == null) return;
                var raptureAtkModule = uiModule->GetRaptureAtkModule();
                if (raptureAtkModule == null) return;

                // 🔴 這裡與上面「單例還沒建立」不同類：AtkArrayDataHolder.StringArrays 是 StringArrayData**（裸指標陣列，沒有邊界檢查），
                //    陣列本身可能還沒配置、索引可能越界、取回的元素可以是 null（元素由 GetTooltipString 擋）。
                //    裸解參考／越界讀是 AVE，屬 corrupted-state exception，try/catch 攔不到。
                var arrayData = raptureAtkModule->AtkModule.AtkArrayDataHolder;
                if (arrayData.StringArrays == null || arrayData.StringArrayCount <= 27)
                {
                    // 走到這裡代表 tooltip 附註會靜默消失，所以留一筆要使用者回報的診斷（只留一次，
                    // 這支每次滑過道具都會跑，不能每幀寫 log）。
                    if (!_stringArrayUnavailableLogged)
                    {
                        _stringArrayUnavailableLogged = true;
                        Log.Information(
                            $"[LogogramHelper] 取不到道具詳情字串陣列（StringArrays={(nint)arrayData.StringArrays:X}, StringArrayCount={arrayData.StringArrayCount}），本次起略過 tooltip 附註。");
                    }

                    return;
                }

                var stringArrayData = arrayData.StringArrays[27];
                var seStr = GetTooltipString(stringArrayData, 13);
                if (seStr == null) return;

                var insert = $"\n\n{Loc.T("Potential logograms contained:")} {string.Join(", ", contents.ToArray())}";
                if (!seStr.TextValue.Contains(insert)) seStr.Payloads.Insert(1, new TextPayload(insert));

                stringArrayData->SetValue(13, seStr.Encode(), false, true, true);
            }
        }

        private static bool _stringArrayUnavailableLogged;

        private static unsafe SeString? GetTooltipString(StringArrayData* stringArrayData, int field)
        {
            // ⚠️ 這個參數的來源是 AtkArrayDataHolder.StringArrays[n]，是陣列元素，槽位可以合法是 null；
            //    呼叫端只擋陣列本身與邊界，元素是不是 null 在這裡擋。
            //    回 null 而不是空字串 —— 呼叫端的既有語意就是 seStr == null 時放棄本次附註、tooltip 維持原樣；
            //    回空字串會讓呼叫端繼續往下把 field 13 覆寫成只剩附註文字，等於把 tooltip 內容洗掉。
            if (stringArrayData == null) return null;

            var stringAddress = new IntPtr(stringArrayData->StringArray[field]);
            return stringAddress != IntPtr.Zero ? MemoryHelper.ReadSeStringNullTerminated(stringAddress) : null;
        }
    }
}
