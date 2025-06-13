using System;
using BepInEx;
using HarmonyLib;
using LethalLib.Modules;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MysteryButton.Scripts
{
	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	[BepInDependency(LethalLib.Plugin.ModGUID)]
	public class MysteryButton : BaseUnityPlugin
	{
		public static Harmony? Harmony { get; protected set; }
		
		public static List<AssetBundle> Bundles = [];
		
		public static MysteryButtonConfig ModConfig { get; private set; }

		public static Material buttonUsedMaterial;
		
		protected void Awake()
		{
			Patch();
			PatchNetCode();

			string buttonAscii = "\n";
			buttonAscii += "*         *     *\n";
			buttonAscii += "    *  _______ \n";
			buttonAscii += " * ___|_______|___  *\n";
			buttonAscii += "  |_______________|";

			Logger.LogInfo(buttonAscii);

			AddBundle("mysterybutton");

			if (Bundles.Count == 0)
			{
				Logger.LogInfo($"Loaded no bundles.");
			}
			else
			{
				Logger.LogInfo($"Loaded {Bundles.Count} bundles : {string.Join(", ", Bundles)}");
			}
			
			ModConfig = new MysteryButtonConfig(Config);
			
			AddFromBundle<MysteryButtonAI>(Bundles.First(), "MysteryButton");

			Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
		}

		private void Patch()
		{
			Harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID);

			Logger.LogDebug("Patching...");
			Harmony.PatchAll();
			Logger.LogDebug("Finished patching!");
		}

		private void PatchNetCode()
		{
			var types = Assembly.GetExecutingAssembly().GetTypes();
			foreach (var type in types)
			{
				var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
				foreach (var method in methods)
				{
					var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
					if (attributes.Length > 0)
					{
						method.Invoke(null, null);
					}
				}
			}
		}

		private void AddBundle(string bundleName)
		{
			string assemblyPath = Assembly.GetExecutingAssembly().Location;
			string? assemblyDirectory = Path.GetDirectoryName(assemblyPath);

			if (assemblyDirectory != null)
			{
				string bundlePath = Path.Combine(assemblyDirectory, bundleName);

				AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
				if (bundle == null)
				{
					Logger.LogError($"Failed to load bundle {bundleName}");
				}
				else
				{
					Bundles.Add(bundle);
				}
			}
			else
			{
				Logger.LogError("Assembly directory not found");
			}
		}

		private void AddFromBundle<T>(AssetBundle bundle, string modName) where T : EnemyAI
		{
			EnemyType enemyType = bundle.LoadAsset<EnemyType>("MysteryButtonET");
			if (enemyType == null || enemyType.enemyPrefab == null)
			{
				Logger.LogError($"Could not load enemy {modName} from bundle {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Loaded enemy {modName} from bundle {bundle.name}.");
			
			var terminalNode = bundle.LoadAsset<TerminalNode>("MysteryButtonTN");
			if (terminalNode == null)
			{
				Logger.LogError($"Could not load {modName} terminal node from {bundle.name}.");
				return;
			}
			
			var terminalKeyword = bundle.LoadAsset<TerminalKeyword>("MysteryButtonTK");
			if (terminalKeyword == null)
			{
				Logger.LogError($"Could not load {modName} terminal keyword from {bundle.name}.");
				return;
			}

			enemyType.enemyPrefab = bundle.LoadAsset<GameObject>($"{modName}.prefab");
			if (enemyType.enemyPrefab == null)
			{
				Logger.LogError($"Could not load enemy prefab {modName} from bundle {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Loaded enemy {modName} prefab from bundle {bundle.name}.");

			T enemyAI = enemyType.enemyPrefab.AddComponent<T>();
			if (enemyAI == null)
			{
				Logger.LogError($"Could not attach AI to enemy {modName} from {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Attached {typeof(T).Name} script to enemy {modName} from {bundle.name}.");
			
			buttonUsedMaterial = bundle.LoadAsset<Material>("ButtonUsedMaterial");
			if (buttonUsedMaterial == null)
			{
				Logger.LogError($"Could not load material ButtonUsedMaterial from bundle {bundle.name}.");
				return;
			}
			
			Logger.LogInfo($"Loaded material ButtonUsedMaterial from bundle {bundle.name}.");

			// Item mysteryButtonItem = bundle.LoadAsset<Item>($"{name}Item");
			// if (mysteryButtonItem == null)
			// {
			// 	Logger.LogError($"Could not load {name}Item from bundle {bundle.name}.");
			// 	return;
			// }
			//
			// Logger.LogInfo($"Loaded enemy {name} prefab from bundle {bundle.name}.");

			enemyAI.enemyType = enemyType;
			enemyAI.enemyType.enemyPrefab.GetComponentInChildren<EnemyAICollisionDetect>().mainScript = enemyAI;

			var maxAmount = MysteryButtonConfig.ConfigMaxAmount.Value;
			Logger.LogInfo($"Maximum number of buttons per round set to {maxAmount}");
			enemyAI.enemyType.MaxCount = maxAmount;
			
			NetworkPrefabs.RegisterNetworkPrefab(enemyAI.enemyType.enemyPrefab);
			// NetworkPrefabs.RegisterNetworkPrefab(mysteryButtonItem.spawnPrefab);
			
			(Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) = ConfigParsing(MysteryButtonConfig.ConfigRarity.Value);

			if (!MysteryButtonConfig.ConfigSpawnDisabled.Value)
			{
				Logger.LogInfo("Spawn enabled for MysteryButton");
				Enemies.RegisterEnemy(enemyAI.enemyType, spawnRateByLevelType, spawnRateByCustomLevelType, terminalNode, terminalKeyword);
				Logger.LogInfo($"Loaded enemy {terminalNode.creatureName} with terminal name {terminalKeyword.word}.");
			}
			else
			{
				Logger.LogInfo("Spawn disabled for MysteryButton");
			}
			// Items.RegisterScrap(mysteryButtonItem, 30, Levels.LevelTypes.All);
		}
		
		private (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) ConfigParsing(string configMoonRarity) {
			Dictionary<Levels.LevelTypes, int> spawnRateByLevelType = new Dictionary<Levels.LevelTypes, int>();
			Dictionary<string, int> spawnRateByCustomLevelType = new Dictionary<string, int>();
		
			foreach (string entry in configMoonRarity.Split(',').Select(s => s.Trim())) {
				string[] entryParts = entry.Split(':');

				if (entryParts.Length != 2) {
					continue;
				}
				string moonName = entryParts[0];
				int spawnRate;

				if (!int.TryParse(entryParts[1], out spawnRate)) {
					continue;
				}

				if (Enum.TryParse(moonName, true, out Levels.LevelTypes levelType)) {
					spawnRateByLevelType[levelType] = spawnRate;
					Logger.LogInfo($"Registered spawn rate for level type {levelType} to {spawnRate}");
				} else {
					spawnRateByCustomLevelType[moonName] = spawnRate;
					Logger.LogInfo($"Registered spawn rate for custom level type {moonName} to {spawnRate}");
				}
			}
			return (spawnRateByLevelType, spawnRateByCustomLevelType);
		}
	}
}
