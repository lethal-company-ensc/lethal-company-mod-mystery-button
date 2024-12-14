using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Example
{
	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	[BepInDependency(LethalLib.Plugin.ModGUID)]
	public class Example : BaseUnityPlugin
	{
		public static Harmony? Harmony { get; protected set; }
		public static List<AssetBundle> Bundles = [];

		protected void Awake()
		{
			Patch();
			PatchNetcode();

			// AddBundle("bundle");

			if (Bundles.Count == 0)
			{
				Logger.LogInfo($"Loaded no bundles.");
			}
			else
			{
				Logger.LogInfo($"Loaded {Bundles.Count} bundles : {string.Join(", ", Bundles)}");
			}

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
	}
}