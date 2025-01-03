using BepInEx;
using HarmonyLib;
using LethalLib.Modules;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MysteryButton
{
	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	[BepInDependency(LethalLib.Plugin.ModGUID)]
	public class MysteryButton : BaseUnityPlugin
	{
		public static Harmony? Harmony { get; protected set; }
		public static List<AssetBundle> Bundles = [];
		
		protected void Awake()
		{
			Patch();
			PatchNetcode();

			AddBundle("mysterybutton");

			if (Bundles.Count == 0)
			{
				Logger.LogInfo($"Loaded no bundles.");
			}
			else
			{
				Logger.LogInfo($"Loaded {Bundles.Count} bundles : {string.Join(", ", Bundles)}");
			}

			AddEnemyFromBundle<ButtonAI>(Bundles.First(), "MysteryButton", "Mystery Button", 180, Levels.LevelTypes.All, Enemies.SpawnType.Default);

			Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
		}

		protected void Patch()
		{
			Harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID);

			Logger.LogDebug("Patching...");
			Harmony.PatchAll();
			Logger.LogDebug("Finished patching!");
		}

		protected void PatchNetcode()
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

		protected void Unpatch()
		{
			Logger.LogDebug("Unpatching...");
			Harmony?.UnpatchSelf();
			Logger.LogDebug("Finished unpatching!");
		}

		protected void AddBundle(string name)
		{
			string assemblyPath = Assembly.GetExecutingAssembly().Location;
			string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
			string bundlePath = Path.Combine(assemblyDirectory, name);

			AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
			if (bundle == null)
			{
				Logger.LogError($"Failed to load bundle {name}");
			}
			else
			{
				Bundles.Add(bundle);
			}
		}

		protected void AddEnemyFromBundle<T>(AssetBundle bundle, string name, string nameInTerminal, int rarity, Levels.LevelTypes levelTypes, Enemies.SpawnType spawnType) where T : EnemyAI
		{
			EnemyType enemyType = bundle.LoadAsset<EnemyType>("MysteryButtonET");
			if (enemyType == null || enemyType.enemyPrefab == null)
			{
				Logger.LogError($"Could not load enemy {name} from bundle {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Loaded enemy {name} from bundle {bundle.name}.");
			
			var terminalNode = bundle.LoadAsset<TerminalNode>("MysteryButtonTN");
			if (terminalNode == null)
			{
				Logger.LogError($"Could not load {name} terminal node from {bundle.name}.");
				return;
			}
			
			var terminalKeyword = bundle.LoadAsset<TerminalKeyword>("MysteryButtonTK");
			if (terminalKeyword == null)
			{
				Logger.LogError($"Could not load {name} terminal keyword from {bundle.name}.");
				return;
			}

			enemyType.enemyPrefab = bundle.LoadAsset<GameObject>($"{name}.prefab");
			if (enemyType.enemyPrefab == null)
			{
				Logger.LogError($"Could not load enemy prefab {name} from bundle {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Loaded enemy {name} prefab from bundle {bundle.name}.");

			T enemyAI = enemyType.enemyPrefab.AddComponent<T>();
			if (enemyAI == null)
			{
				Logger.LogError($"Could not attach AI to enemy {name} from {bundle.name}.");
				return;
			}

			Logger.LogInfo($"Attached {typeof(T).Name} script to enemy {name} from {bundle.name}.");

			enemyAI.enemyType = enemyType;
			enemyAI.enemyType.enemyPrefab.GetComponentInChildren<EnemyAICollisionDetect>().mainScript = enemyAI;
			
			NetworkPrefabs.RegisterNetworkPrefab(enemyAI.enemyType.enemyPrefab);
			Enemies.RegisterEnemy(enemyAI.enemyType, rarity, levelTypes, spawnType, terminalNode, terminalKeyword);

			Logger.LogInfo($"Loaded enemy {terminalNode.creatureName} with terminal name {terminalKeyword.word}.");
		}
	}
}
