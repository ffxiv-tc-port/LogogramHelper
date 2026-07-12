using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using ImGuiNET;
using ImGuiScene;
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
        private unsafe void ObtainLogograms()
        {
            var arrayData = Framework.Instance()->GetUIModule()->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder;
            for (var i = 1; i <= arrayData.NumberArrays[136]->IntArray[0]; i++)
            {
                var id = arrayData.NumberArrays[136]->IntArray[(4 * i) + 1];
                var stock = arrayData.NumberArrays[136]->IntArray[4 * i];
                if (!LogogramStock.ContainsKey(id))
                {
                    LogogramStock.Add(id, stock);
                    continue;
                }
                if (LogogramStock[id] != stock)
                    LogogramStock[id] = stock;
            }
        }
        public void SetDetails(LogosAction action) {
            this.Action = action;
            this.Texture = Plugin.TextureProvider.GetFromGameIcon(action.IconID);
        }

        private void FillSynthesizer(List<Recipe> recipe, bool starChart)
        {
            if (!SynthesisAutomation.SelectSynthesizer(starChart)) return;
            foreach (var item in recipe)
            {
                if (!Plugin.LogogramRowIndex.TryGetValue(item.LogogramID, out var row)) continue;
                for (var q = 0; q < item.Quantity; q++)
                {
                    // An unexpected confirmation/quantity dialog (e.g. NumberInputDialog) can pop up
                    // mid-sequence; continuing to blast further synthetic FireCallback events while it's
                    // open ends up hitting its buttons instead of the shard list, closing it out from
                    // under the player. Bail out and let the player finish manually if that happens.
                    if (Plugin.GameGui.GetAddonByName("NumberInputDialog", 1) != IntPtr.Zero)
                    {
                        Plugin.Log.Warning("FillSynthesizer: unexpected NumberInputDialog detected, aborting automation.");
                        return;
                    }
                    SynthesisAutomation.AddShard(row);
                }
            }
        }
        public override void Draw()
        {
            var addonShardListPtr = Plugin.GameGui.GetAddonByName("EurekaMagiciteItemShardList", 1);
            if (addonShardListPtr != IntPtr.Zero)
            {
                ObtainLogograms();
            }
            if (Texture == null)
                return;
            var fontScaling = ImGui.GetFontSize() / 17;
            ImGui.PushTextWrapPos(540.0f * fontScaling);
            ImGui.BeginGroup();
            ImGui.Image(Texture.GetWrapOrEmpty().ImGuiHandle, new Vector2(40, 40) * fontScaling, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(Loc.T(Action.Name));
            ImGui.SameLine();
            ImGui.BeginGroup();
            Action.Roles.ForEach(role => {
                var roleTexture = Plugin.TextureProvider.GetFromGameIcon(role).GetWrapOrEmpty();
                ImGui.Image(roleTexture.ImGuiHandle, new Vector2(18, 18) * fontScaling, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
                ImGui.SameLine();
            });
            ImGui.EndGroup();
            var details = Loc.T(Action.Type);
            if (Action.Duration != null)
                details += $" · 持續時間：{Action.Duration}";
            if (Action.Cast != null)
                details += $" · 詠唱時間：{Action.Cast}";
            if (Action.Recast != null)
                details += $" · 重使用時間：{Action.Recast}";
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), details);
            ImGui.EndGroup();
            ImGui.EndGroup();
            ImGui.Spacing();
            ImGui.Text(Loc.T(Action.Description));
            ImGui.Spacing();
            ImGui.Text(Loc.T("Combinations:"));
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
                if (ImGui.SmallButton($"放入靈極##{recipeIdx}"))
                    FillSynthesizer(recipe, false);
                ImGui.SameLine();
                if (ImGui.SmallButton($"放入星極##{recipeIdx}"))
                    FillSynthesizer(recipe, true);
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
                        ImGui.Image(Plugin.TextureProvider.GetFromGameIcon(logogramIcon).GetWrapOrEmpty().ImGuiHandle, new Vector2(iconSize, iconSize), new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f));
                        ImGui.SameLine();
                    }
                    var owned = LogogramStock[item.LogogramID];
                    ImGui.Text($"{Loc.T(Logograms[item.LogogramID].Name)} x{item.Quantity}（庫存 {owned}）");
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
