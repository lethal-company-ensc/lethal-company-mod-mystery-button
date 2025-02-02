using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;

namespace MysteryButton;

public class MysteryButtonConfig
{
    public static ConfigEntry<bool> ConfigSpawnDisabled { get; private set; }
    public static ConfigEntry<string> ConfigRarity { get; private set; }
    public static ConfigEntry<int> ConfigMaxAmount { get; private set; }
    
    private static ManualLogSource logger = Logger.CreateLogSource(
        "MysteryButton.MysteryButtonConfig"
    );
    
    public MysteryButtonConfig(ConfigFile cfg)
    {
        ConfigSpawnDisabled = cfg.Bind("Enemy Options", 
            "Spawning Disabled",
            false,
            "Enable or Disable MysteryButton spawning.");
        
        ConfigRarity = cfg.Bind("Enemy Options", 
            "Spawn Weight",
            "EmbrionLevel:20,ArtificeLevel:20,AdamanceLevel:20,TitanLevel:20,DineLevel:20,RendLevel:20,MarchLevel:20,OffenseLevel:20,VowLevel:20,AssuranceLevel:20,ExperimentationLevel:20,Modded:20",
            "Spawn Weight of the MysteryButton in all moons, Feel free to add to it any moon, just follow the format (also needs LLL installed for LE moons to work with this config).");
        
        ConfigMaxAmount = cfg.Bind("Enemy Options", 
            "Max Amount",
            1,
            "How many MysteryButton can spawn.");
            
        ClearOrphanedEntries(cfg); 
        logger.LogInfo("Setting up config for MysteryButton plugin...");
    }
    
    static void ClearOrphanedEntries(ConfigFile cfg) 
    { 
        PropertyInfo orphanedEntriesProp = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries"); 
        var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg); 
        orphanedEntries.Clear(); 
    } 
}