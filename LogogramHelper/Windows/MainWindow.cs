using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using ImGuiScene;
using System.Collections.Generic;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using System.Diagnostics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures;

namespace LogogramHelper.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin { get; }
    private List<LogosAction> LogosActions { get; }

    public MainWindow(Plugin plugin) : base(
        Loc.T("Logos Actions"), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.Plugin = plugin;
        this.LogosActions = plugin.LogosActions;
        this.ShowCloseButton = false;
    }

    public void Dispose()
    {
    }

    private string filter = "";
    private string presetName = "";

    public override unsafe void Draw()
    {
        var fontScaling = ImGui.GetFontSize() / 17;

        ImGui.PushItemWidth(400);
        ImGui.InputTextWithHint("", Loc.T("Filter Logos Actions..."), ref filter, 50, ImGuiInputTextFlags.AutoSelectAll);
        ImGui.PopItemWidth();

        ImGui.SameLine();

        if (ImGuiComponents.IconButton("KoFi", FontAwesomeIcon.Coffee, new Vector4(1.0f, 0.35f, 0.37f, 1.0f)))
            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/apetih", UseShellExecute = true });
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Support me on Ko-Fi"));

        ImGui.PushItemWidth(200);
        ImGui.InputTextWithHint("##presetName", Loc.T("Preset name..."), ref presetName, 50);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        var canSave = (Plugin.CurrentLunarSelection != null || Plugin.CurrentStarSelection != null) && !string.IsNullOrWhiteSpace(presetName);
        ImGui.BeginDisabled(!canSave);
        if (ImGui.Button(Loc.T("Save current combination")) && Plugin.SavePreset(presetName))
            presetName = "";
        ImGui.EndDisabled();
        if (!canSave && ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Fill in at least one synthesizer first"));

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Clear")))
            Plugin.ClearCurrentSelection();

        if (Plugin.Presets.Count > 0)
        {
            ImGui.Spacing();
            foreach (var preset in Plugin.Presets.ToArray())
            {
                ImGui.Text(preset.Name);
                ImGui.SameLine();
                if (ImGui.SmallButton($"{Loc.T("Apply")}##{preset.Name}"))
                    Plugin.ApplyPreset(preset);
                ImGui.SameLine();
                if (ImGui.SmallButton($"{Loc.T("Delete")}##{preset.Name}"))
                    Plugin.DeletePreset(preset);
            }
            ImGui.Spacing();
        }

        for (var i = 0; i < 56; i++)
        {
            var action = LogosActions[i];
            var padding = 2;
            var bg = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            var tint = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            var actionName = Loc.T(action.Name);
            if (!action.Name.ToLower().Contains(filter.ToLower()) && !actionName.Contains(filter)) tint.W = 0.25f;
            var recorded = Plugin.HasFillHistory(action.Name);
            if (recorded) bg = new Vector4(0.15f, 0.5f, 0.15f, 1.0f);
            if (ImGui.ImageButton(Plugin.TextureProvider.GetFromGameIcon(action.IconID).GetWrapOrEmpty().ImGuiHandle, new Vector2(40, 40) * fontScaling, new Vector2(0.0f, 0.0f), new Vector2(1.0f, 1.0f), padding, bg, tint))
            {
                /*var roleTextures = new Dictionary<uint, ISharedImmediateTexture>();
                action.Roles.ForEach(role =>
                {
                    var tex = Plugin.TextureProvider.GetFromGameIcon(role);
                    roleTextures.Add(role, tex);
                });*/
                Plugin.DrawLogosDetailUI(action);
            }
            if (ImGui.IsItemHovered())
            {
                var tooltip = actionName;
                if (recorded) tooltip += $"\n{Loc.T("Already filled before")}";
                ImGui.SetTooltip(tooltip);
            }
            if ((i + 1) % 10 != 0) ImGui.SameLine();
        }

    }
}
