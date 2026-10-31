using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using Wish;

namespace SunHavenCreate
{
    [BepInPlugin("local.sunhavencreate.prototype", "Sun Haven Create", "0.3.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static bool EnableBridge;
        internal static bool Live;
        void Awake()
        {
            Log = Logger;
            EnableBridge = Config.Bind("Development", "LocalTestBridge", false, "Enable local JSON file commands for testing only.").Value;
            Live = Config.Bind("Gameplay", "WorldIntegration", true, "Persistent machines and real resources. False enables the temporary sandbox.").Value;
            new Harmony("local.sunhavencreate.prototype.lifetime").Patch(
                typeof(Scene).GetMethod("GetRootGameObjects", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(ProtectOwnRoots)));
            new Harmony("local.sunhavencreate.prototype.occupancy").Patch(
                typeof(GameManager).GetMethods().Single(m => m.Name == "HasObjectSubTile" && m.GetParameters().Length == 12),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(CheckMachineOccupancy)));
            SceneManager.sceneLoaded += EnsureRuntime;
            Player.OnPlayerInitializedAsOwner += () => { if (EnableBridge) Application.runInBackground = true; };
            Logger.LogInfo("Bootstrap installed; runtime will start after initial scene load.");
        }
        static void EnsureRuntime(Scene scene, LoadSceneMode mode)
        {
            if (FactoryRuntime.Instance) return;
            var host = new GameObject("SunHavenCreate_Runtime");
            host.AddComponent<FactoryRuntime>();
            Log.LogInfo("Runtime created after scene: " + scene.name);
        }
        static void ProtectOwnRoots(ref GameObject[] __result)
        {
            var runtime = FactoryRuntime.Instance;
            if (!runtime) return;
            __result = Array.FindAll(__result, o => o != runtime.gameObject && o != runtime.root);
        }
        static void CheckMachineOccupancy(Vector3Int position, Vector2Int size, ref bool __result)
        {
            if (!__result && Live && FactoryRuntime.Instance)
                __result = FactoryRuntime.Instance.Occupies(position, size);
        }
    }
}
