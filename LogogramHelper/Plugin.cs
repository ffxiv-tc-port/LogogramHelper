using Dalamud.IoC;
using Dalamud.Plugin;
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
        }

        public void Dispose()
        {
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
                // The ContainsKey above guards LogogramItems (itemContents.json); the lookup
                // below is against Logograms (logograms.json) keyed by an id that came out of
                // the *value* of the first dictionary. Different files, different key spaces -
                // equal only by convention (verified equal today: 28 ids on both sides). This
                // runs on every item tooltip from an addon hook, so degrade to the raw id
                // rather than throwing once per frame while an item is hovered.
                contentsId.ForEach(content =>
                {
                    contents.Add(Logograms.TryGetValue(content, out var logogram)
                        ? Loc.T(logogram.Name)
                        : $"#{content}");
                });

                // 🔴 原本是三層裸鏈。Framework.Instance() 是 [StaticAddress(..., isPointer: true)]：
                //    產生器讀「指標的位址」再解參考一層，遊戲尚未建立單例時回 null（非 isPointer
                //    的那種才保證不回 null，是擲 InvalidOperationException）。
                //    GetUIModule() / GetRaptureAtkModule() 同樣可能回 null
                //    （RaptureAtkModule.Instance() 在 CS 裡就是 `uiModule == null ? null : ...` 的手寫包裝）。
                //    裸解參考 null 原生指標是 AVE，屬 corrupted-state exception，try/catch 攔不到。
                //    這支跑在道具 tooltip 的 addon hook 上（每次滑過道具都經過），取不到就放棄本次
                //    附註，走既有的 seStr == null 相同語意：tooltip 維持原樣，不崩潰。
                var framework = Framework.Instance();
                if (framework == null) return;
                var uiModule = framework->GetUIModule();
                if (uiModule == null) return;
                var raptureAtkModule = uiModule->GetRaptureAtkModule();
                if (raptureAtkModule == null) return;

                // 🔴 上面三層判空守住的是「單例還沒建立」，這裡是**另一類**:
                //    AtkArrayDataHolder.StringArrays 在 FFXIVClientStructs 裡是
                //    StringArrayData**（裸指標陣列，沒有邊界檢查），而 CS 對這組欄位的
                //    註解明寫「these are total counts - some of the slots can be (and are) empty」
                //    —— 也就是**陣列本身可能還沒配置、索引可能越界、取回的元素可以是 null**。
                //    原本 arrayData.StringArrays[27] 直接餵進 GetTooltipString 再
                //    stringArrayData->StringArray[field]，三種情況都是裸解參考／越界讀。
                //    這支跑在道具 tooltip 的 addon hook 上（每次滑過道具都經過），
                //    AVE 是 corrupted-state exception，try/catch 攔不到。
                var arrayData = raptureAtkModule->AtkModule.AtkArrayDataHolder;
                if (arrayData.StringArrays == null || arrayData.StringArrayCount <= 27)
                {
                    // 走到這裡代表 tooltip 附註會靜默消失，所以留一筆（只留一次，
                    // 這支每次滑過道具都會跑，不能每幀寫 log）。使用者跑 LogLevel 1，
                    // 診斷一律 Information 才收得到。
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
            // ⚠️ 這個參數的來源是 AtkArrayDataHolder.StringArrays[n]，是**陣列元素**，
            //    與「單例取不到」不同類:槽位可以合法是 null（CS 註解:some of the slots
            //    can be (and are) empty）。呼叫端已經先擋過一次，這裡再擋一次是因為本函式
            //    是 static helper，日後多一個呼叫點就會多一條裸解參考的路徑。
            //    回 null 而不是空字串 —— 呼叫端的既有語意就是 seStr == null 時放棄本次
            //    附註、tooltip 維持原樣；回空字串會讓呼叫端繼續往下把 field 13 覆寫成
            //    只剩附註文字，等於把 tooltip 內容洗掉。
            if (stringArrayData == null) return null;

            var stringAddress = new IntPtr(stringArrayData->StringArray[field]);
            return stringAddress != IntPtr.Zero ? MemoryHelper.ReadSeStringNullTerminated(stringAddress) : null;
        }
    }
}
