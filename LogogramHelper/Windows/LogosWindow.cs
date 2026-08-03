using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using LogogramHelper.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LogogramHelper.Windows
{
    public class LogosWindow : Window, IDisposable
    {
        private Plugin Plugin { get; }
        private LogosAction Action { get; set; }
        private IDictionary<int, int> LogogramStock { get; set; }
        private IDictionary<int, Logogram> Logograms { get; }
        private ISharedImmediateTexture Texture { get; set; } = null!;
        private IDictionary<uint, ISharedImmediateTexture> RoleTextures { get; set; } = null!;

        // Index of the EurekaMagiciteItemShardList number array inside AtkArrayDataHolder.
        // This is a client-version constant and it has moved on nearly every expansion:
        // 134 (6.x) -> 135 (6.5) -> 136 (7.0) -> 137 (7.3). FFXIVClientStructs still declares
        // NumberArrayType.EurekaLogosShardList = 136, but those enum values were last
        // revalidated before global 7.3, whereas upstream's own "7.3" commit
        // (apetih/LogogramHelper 37d6531, 2025-08-08) moved this read to 137 - the same +1
        // shift we already carry for the ItemDetail *string* array (26 -> 27, commit 148307b).
        // TC 7.20 is a 7.3-generation client, so 137 is the expected value here; 136 was
        // correct while we were on TC 7.15 and started returning a null slot after the
        // TC 7.20 migration, which is what blew the whole window up.
        // Rather than trust a bare constant that is guaranteed to drift again, probe the
        // plausible indexes and accept only an array whose decoded contents are all
        // logogram item IDs we know about.
        private static readonly int[] ShardArrayCandidates = { 137, 136, 138, 135 };
        private int shardArrayIndex = -1;
        private bool loggedShardArrayFailure;

        public LogosWindow(Plugin plugin) : base(
        Loc.T("Logos Details"), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize)
        {
            this.Plugin = plugin;
            this.Action = plugin.LogosActions[0];
            this.LogogramStock = plugin.LogogramStock;
            this.Logograms = plugin.Logograms;
        }

        public void Dispose()
        {
        }
        /// <summary>
        /// Refreshes <see cref="LogogramStock"/> from the shard list's number array.
        /// Every dereference is guarded: a layout change in a future client must degrade to
        /// "no fresh stock" rather than throwing out of Draw() and replacing the whole window
        /// with Dalamud's error placeholder.
        /// </summary>
        /// <returns><c>true</c> if stock was read.</returns>
        private unsafe bool ObtainLogograms()
        {
            var framework = Framework.Instance();
            if (framework == null) return false;
            var uiModule = framework->GetUIModule();
            if (uiModule == null) return false;
            var raptureAtkModule = uiModule->GetRaptureAtkModule();
            if (raptureAtkModule == null) return false;

            var arrayData = raptureAtkModule->AtkModule.AtkArrayDataHolder;
            var numberArrays = arrayData.NumberArrays;
            if (numberArrays == null) return false;
            int arrayCount = arrayData.NumberArrayCount;

            // Fast path: the index we already resolved this session.
            if (shardArrayIndex >= 0 && TryReadShardArray(numberArrays, arrayCount, shardArrayIndex))
                return true;

            foreach (var candidate in ShardArrayCandidates)
            {
                if (candidate == shardArrayIndex) continue;
                if (!TryReadShardArray(numberArrays, arrayCount, candidate)) continue;

                Plugin.Log.Information(
                    $"Resolved the Eureka shard list number array to index {candidate} (was {shardArrayIndex}).");
                shardArrayIndex = candidate;
                loggedShardArrayFailure = false;
                return true;
            }

            if (!loggedShardArrayFailure)
            {
                loggedShardArrayFailure = true;
                Plugin.Log.Warning(
                    "Could not locate the Eureka shard list number array (tried indexes " +
                    $"{string.Join(", ", ShardArrayCandidates)} of {arrayCount}). " +
                    "Logogram stock counts will not be updated.");
            }
            return false;
        }

        /// <summary>
        /// Validates then reads one candidate number array. Nothing is written to
        /// <see cref="LogogramStock"/> unless every decoded row is a known logogram, so a
        /// wrong index can never poison the displayed counts.
        /// </summary>
        private unsafe bool TryReadShardArray(NumberArrayData** numberArrays, int arrayCount, int index)
        {
            if (index < 0 || index >= arrayCount) return false;

            var array = numberArrays[index];
            if (array == null) return false;

            var intArray = array->IntArray;
            if (intArray == null) return false;

            // AtkArrayData.Size is the element count of IntArray.
            var size = array->Size;
            if (size < 2) return false;

            var rows = intArray[0];
            // Eureka has exactly 28 logograms and is finished content, so a plausible row
            // count is small. The cap is deliberately loose; the ID check below is the real filter.
            if (rows <= 0 || rows > 64) return false;
            // Row i occupies IntArray[4 * i] (stock) and IntArray[4 * i + 1] (item id).
            if ((4L * rows) + 1 >= size) return false;

            for (var i = 1; i <= rows; i++)
            {
                if (!Logograms.ContainsKey(intArray[(4 * i) + 1])) return false;
                var stock = intArray[4 * i];
                if (stock < 0 || stock > 99999) return false;
            }

            for (var i = 1; i <= rows; i++)
                LogogramStock[intArray[(4 * i) + 1]] = intArray[4 * i];

            return true;
        }
        public void SetDetails(LogosAction action) {
            this.Action = action;
            this.Texture = Plugin.TextureProvider.GetFromGameIcon(action.IconID);
        }

        private string HistoryKey(int recipeIdx, bool starChart) => Plugin.HistoryKey(Action.Name, recipeIdx, starChart);

        public override void Draw()
        {
            var addonShardListPtr = Plugin.GameGui.GetAddonByName("EurekaMagiciteItemShardList", 1);
            var stockUnavailable = false;
            if (addonShardListPtr != IntPtr.Zero)
            {
                // Only treat this as "unavailable" when we have nothing at all to show;
                // a failed refresh on top of previously read stock just leaves it stale.
                stockUnavailable = !ObtainLogograms() && LogogramStock.Count == 0;
            }
            if (Texture == null)
                return;
            var fontScaling = ImGui.GetFontSize() / 17;
            ImGui.PushTextWrapPos(540.0f * fontScaling);
            ImGui.BeginGroup();
            ImGui.Image(Texture.GetWrapOrEmpty().Handle, new Vector2(40, 40) * fontScaling, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(Loc.T(Action.Name));
            ImGui.SameLine();
            ImGui.BeginGroup();
            Action.Roles.ForEach(role => {
                var roleTexture = Plugin.TextureProvider.GetFromGameIcon(role).GetWrapOrEmpty();
                ImGui.Image(roleTexture.Handle, new Vector2(18, 18) * fontScaling, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
                ImGui.SameLine();
            });
            ImGui.EndGroup();
            var details = Loc.T(Action.Type);
            if (Action.Duration != null)
                details += $" · {Loc.T("DURATION: ")}{Action.Duration}";
            if (Action.Cast != null)
                details += $" · {Loc.T("CAST: ")}{Action.Cast}";
            if (Action.Recast != null)
                details += $" · {Loc.T("RECAST: ")}{Action.Recast}";
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), details);
            ImGui.EndGroup();
            ImGui.EndGroup();
            ImGui.Spacing();
            ImGui.Text(Loc.T(Action.Description));
            ImGui.Spacing();
            ImGui.Text(Loc.T("Combinations:"));
            if (stockUnavailable)
                ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), Loc.T("Could not read logogram stock; the counts below are not accurate."));
            var iconSize = ImGui.GetFontSize() * 1.4f;
            var rowHeight = iconSize + 6;
            ImGui.BeginChild($"combinations{Action.Name}", new Vector2(540.0f * fontScaling, rowHeight * Action.Recipes.Count), false, ImGuiWindowFlags.NoScrollbar);
            ImGui.Columns(3, "combinations", false);
            ImGui.SetColumnWidth(0, 90f * fontScaling);
            ImGui.SetColumnWidth(1, 40f);
            ImGui.SetColumnWidth(2, 410f * fontScaling);
            for (var recipeIdx = 0; recipeIdx < Action.Recipes.Count; recipeIdx++)
            {
                var recipe = Action.Recipes[recipeIdx];
                var lunarDone = Plugin.FillHistory.Contains(HistoryKey(recipeIdx, false));
                var starDone = Plugin.FillHistory.Contains(HistoryKey(recipeIdx, true));
                if (lunarDone)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.4f, 1.0f));
                if (ImGui.SmallButton($"{Loc.T("Umbral")}##{recipeIdx}"))
                    Plugin.FillSynthesizer(Action, recipeIdx, false);
                if (lunarDone)
                    ImGui.PopStyleColor();
                ImGui.SameLine();
                if (starDone)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.4f, 1.0f));
                if (ImGui.SmallButton($"{Loc.T("Astral")}##{recipeIdx}"))
                    Plugin.FillSynthesizer(Action, recipeIdx, true);
                if (starDone)
                    ImGui.PopStyleColor();
                ImGui.NextColumn();
                var craftable = new List<int>();
                recipe.ForEach(item => {
                    if (!LogogramStock.ContainsKey(item.LogogramID))
                        LogogramStock.Add(item.LogogramID, 0);
                    craftable.Add(LogogramStock[item.LogogramID] / item.Quantity);
                });
                if (craftable.Min() > 0)
                    ImGui.Text($"{craftable.Min()}");
                else
                    ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), $"{craftable.Min()}");
                ImGui.NextColumn();
                for (var idx = 0; idx < recipe.Count; idx++)
                {
                    var item = recipe[idx];
                    if (Plugin.LogogramIcons.TryGetValue(item.LogogramID, out var logogramIcon))
                    {
                        ImGui.Image(Plugin.TextureProvider.GetFromGameIcon(logogramIcon).GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize), new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
                        ImGui.SameLine();
                    }
                    var owned = LogogramStock[item.LogogramID];
                    // Recipes come from logosActions.json but the names come from
                    // logograms.json - two separate data files whose key sets are only equal by
                    // convention (verified equal today: 28 ids on both sides). The
                    // LogogramIcons lookup above is built from Logograms and therefore shares
                    // its keys exactly, so a miss there means this lookup would throw - and
                    // throwing out of Draw() replaces the entire window with Dalamud's error
                    // placeholder, which is exactly the failure this window already had once.
                    // Degrade to the raw id instead of hiding the ingredient, so a desynced
                    // data file shows up as an odd label rather than a silently short recipe.
                    var itemName = Logograms.TryGetValue(item.LogogramID, out var logogram)
                        ? Loc.T(logogram.Name)
                        : $"#{item.LogogramID}";
                    ImGui.Text($"{itemName} x{item.Quantity} ({Loc.T("Stock")} {owned})");
                    if (idx != recipe.Count - 1)
                    {
                        ImGui.SameLine();
                        ImGui.Text("+");
                        ImGui.SameLine();
                    }
                }
                ImGui.NextColumn();
            }
            ImGui.EndChild();
            ImGui.PopTextWrapPos();
        }
    }
}
