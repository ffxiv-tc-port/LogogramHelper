using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace LogogramHelper
{
    [Serializable]
    public class Preset
    {
        public string Name { get; set; } = "";
        public string LunarActionName { get; set; } = "";
        public int LunarRecipeIdx { get; set; }
        public string StarActionName { get; set; } = "";
        public int StarRecipeIdx { get; set; }
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public HashSet<string> FillHistory { get; set; } = new();
        public List<Preset> Presets { get; set; } = new();

        public void Save()
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
    }
}
