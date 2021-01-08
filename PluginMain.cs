using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using I2.Loc;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;
using PolyPhysics;
using Poly.Physics.Solver;

namespace StressTypeVisualization
{
    [BepInPlugin("56kyle.stress_type_visualization", "Stress Type Visualization", "1.5.0")]
	[BepInProcess("Poly Bridge 2.exe")]
    public class PluginMain : BaseUnityPlugin
    {
        private static ConfigEntry<bool> configIsEnabled;

        private static ConfigEntry<bool> configStressViewIsEnabled;
        private static ConfigEntry<bool> configTensileCompressiveModeIsEnabled;

        private static ConfigEntry<Color> configTensileMin;
        private static ConfigEntry<Color> configTensileMax;
        private static ConfigEntry<Color> configCompressiveMin;
        private static ConfigEntry<Color> configCompressiveMax;

        private static ConfigEntry<bool> configThresholdModeIsEnabled;

        private static ConfigEntry<float> configThresholdModeMinStress;
        private static ConfigEntry<float> configThresholdModeMaxStress;

        private static ConfigEntry<bool> configPausingIsEnabled;

        private static ConfigEntry<bool> configPauseBeforeBreak;

        private static ConfigEntry<bool> configEveryBreak;

        private static ConfigEntry<bool> configHighlightFirstBreak;

        public static int colorId = Shader.PropertyToID("_Color");
        public static int stressId = Shader.PropertyToID("_Stress");

        public static Color minTensileColor;
        public static Color maxTensileColor;

        public static Color minCompressiveColor;
        public static Color maxCompressiveColor;
        public static bool disabled = false;


        public PluginMain()
        {
            // Fallback options
            Color defaultTensMin = Color.green;
            Color defaultCompMin = Color.green;
            Color defaultTensMax = Color.red;
            Color defaultCompMax = Color.blue;

            // Normal stress color and the same skew amount for the shade of blue
            ColorUtility.TryParseHtmlString("#4D993D", out defaultTensMin);
            ColorUtility.TryParseHtmlString("#4D993D", out defaultCompMin);
            ColorUtility.TryParseHtmlString("#B33636", out defaultTensMax);
            ColorUtility.TryParseHtmlString("#4A36B3", out defaultCompMax);

            // Enabling the mod
            configIsEnabled = Config.Bind("General", "Enabled", true,
                new ConfigDescription("Enables the mod", null, new ConfigurationManagerAttributes{Order = 1}));

            // Tensile and Compressive Mode
            configTensileCompressiveModeIsEnabled = Config.Bind("General.Differentiating Stress", "Enabled", true,
                new ConfigDescription("Enables stress differentiation mode", null, new ConfigurationManagerAttributes{Order = 5}));

            configTensileMin = Config.Bind("General.Differentiating Stress", "Tensile Min Stress Color", defaultTensMin,
                new ConfigDescription("The color that will show for max tensile stress", null, new ConfigurationManagerAttributes{Order = 4}));
            configTensileMax = Config.Bind("General.Differentiating Stress", "Tensile Max Stress Color", defaultTensMax,
                new ConfigDescription("The color that will show for max tensile stress", null, new ConfigurationManagerAttributes{Order = 3}));

            configCompressiveMin = Config.Bind("General.Differentiating Stress", "Compressive Min Stress Color", defaultCompMin,
                new ConfigDescription("The color that will show for the min tensile stress", null, new ConfigurationManagerAttributes{Order = 2}));
            configCompressiveMax = Config.Bind("General.Differentiating Stress", "Compressive Max Stress Color", defaultCompMax,
                new ConfigDescription("The color that will show for max tensile stress", null, new ConfigurationManagerAttributes{Order = 1}));

            // Threshold mode
            configThresholdModeIsEnabled = Config.Bind("General.Threshold Mode", "Enabled", false,
                new ConfigDescription("Enables Threshold Mode", null, new ConfigurationManagerAttributes{Order = 2}));
            configThresholdModeMinStress = Config.Bind("General.Threshold Mode", "Min Stress", 0.4f,
                new ConfigDescription("The minimum stress before changes are made", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes{Order = 1}));

            // Pausing options
            configPausingIsEnabled = Config.Bind("General.Stop Simulation On Break", "Enabled", false,
                new ConfigDescription("Enables the pausing options", null, new ConfigurationManagerAttributes{Order = 4}));
            configPauseBeforeBreak = Config.Bind("General.Stop Simulation On Break", "Pause Before First Break", false,
                new ConfigDescription("Makes it so the game will be paused right before the first break occurs.", null, new ConfigurationManagerAttributes{Order = 3}));
            configEveryBreak = Config.Bind("General.Stop Simulation On Break", "Pause Before Every Break", false,
                new ConfigDescription("Pauses the game before a break occurs.", null, new ConfigurationManagerAttributes{Order = 3}));
            configHighlightFirstBreak = Config.Bind("General.Stop Simulation On Break", "Highlight First Break", false,
                new ConfigDescription("Highlights the first breakage immediately as opposed to when inside build mode (Not always accurate)", null, new ConfigurationManagerAttributes{Order = 2}));
        }

        private void Awake()
        {
            PluginMain.staticLogger = base.Logger;
			new Harmony("56kyle.stress_type_visualization").PatchAll(Assembly.GetExecutingAssembly());
        }

        public void RefreshSettings()
        {
            minTensileColor = configTensileMin.Value;
            maxTensileColor = configTensileMax.Value;
            minCompressiveColor = configCompressiveMin.Value;
            maxCompressiveColor = configCompressiveMax.Value;
            if (!configIsEnabled.Value)
            {
                configTensileCompressiveModeIsEnabled.Value = false;
                configPausingIsEnabled.Value = false;
                disabled = true;
            }

            if (!configPausingIsEnabled.Value)
            {
                configPauseBeforeBreak.Value = false;
                configEveryBreak.Value = false;
                configHighlightFirstBreak.Value = false;
            }
        }

        public void Update()
        {
            RefreshSettings();
        }

        private static ManualLogSource staticLogger;
        public static bool firstBreakWasFound = false;
        public static GameState m_GameState;
        public static Dictionary<int, List<Material>> originMaterials = new Dictionary<int, List<Material>>();

        private static void SetShaderColor(BridgeEdge bridgeEdge, float stressNormalized, float stressNormalizedSigned)
        {
            Color minColor;
            Color maxColor;

            if (stressNormalizedSigned <= 0.0f)
            {
                minColor = minTensileColor;
                maxColor = maxTensileColor;
            }
            else
            {
                minColor = minCompressiveColor;
                maxColor = maxCompressiveColor;
            }

            if (minColor == null || maxColor == null) { return; }

            Color desiredColor = Color.Lerp(minColor, maxColor, stressNormalized);

            if (bridgeEdge.m_MeshRenderer.material.HasProperty(stressId))
            {
                originMaterials[bridgeEdge.GetHashCode()] = new List<Material>{bridgeEdge.m_MeshRenderer.material, bridgeEdge.m_MeshRenderer.sharedMaterial};
                bridgeEdge.m_MeshRenderer.material = new Material(Shader.Find("Standard"));
                bridgeEdge.m_MeshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
            }
            bridgeEdge.m_MeshRenderer.material.color = desiredColor;
            bridgeEdge.m_MeshRenderer.sharedMaterial.color = desiredColor;
        }

        public static void CheckMaterial(BridgeEdge bridgeEdge)
        {
            if (!bridgeEdge.m_MeshRenderer.material.HasProperty(stressId))
            {
                Material originalMaterial = null;
                Material originalSharedMaterial = null;
                try
                {
                    int hashCode = bridgeEdge.GetHashCode();
                    List<Material> originalMaterialList = originMaterials[hashCode];
                    if (originalMaterialList != null)
                    {
                        originalMaterial = originalMaterialList[0];
                        originalSharedMaterial = originalMaterialList[1];
                    }

                    originMaterials.Remove(hashCode);
                }
                catch (KeyNotFoundException)
                {
                    staticLogger.LogMessage("Edge has no original material, assuming it's fine");
                }

                if (originalMaterial != null)
                {
                    bridgeEdge.m_MeshRenderer.material = originalMaterial;
                }

                if (originalSharedMaterial != null)
                {
                    bridgeEdge.m_MeshRenderer.sharedMaterial = originalSharedMaterial;
                }
            }
        }

        // Adjusts the stress color
        [HarmonyPatch(typeof(BridgeEdge), "SetStressColor")]
        private static class Patch_BridgeEdge_SetStressColor
        {
            [HarmonyPostfix]
            private static void Postfix(ref BridgeEdge __instance, ref float stressNormalized, ref MaterialPropertyBlock ___m_MaterialPropertyBlock, ref int ___m_ShaderIDForStress, ref int ___m_ShaderIDForColorBlind)
            {

                if (stressNormalized == 0f)
                {
                    CheckMaterial(__instance);
                    return;
                }

                float alteredStress;
                if (configThresholdModeIsEnabled.Value)
                {
                    alteredStress = stressNormalized >= configThresholdModeMinStress.Value ? 1.0f : stressNormalized;
                }
                else
                {
                    alteredStress = stressNormalized;
                }

                if (configTensileCompressiveModeIsEnabled.Value)
                {
                    if (__instance.m_PhysicsEdge)
                    {
                        Edge edge = __instance.m_PhysicsEdge;
                        if (edge.handle)
                        {
                            PluginMain.SetShaderColor(__instance, alteredStress, edge.handle.stressNormalizedSigned);
                        }
                    }
                }
                else
                {
                    ___m_MaterialPropertyBlock.SetFloat(___m_ShaderIDForColorBlind, Profile.m_ColorBlindModeOn ? 1f : 0f);
                    __instance.m_MeshRenderer.SetPropertyBlock(___m_MaterialPropertyBlock);
                }
            }
        }

        // Tracks and pauses at desired breaks
        [HarmonyPatch(typeof(Solver), "CheckImpulseAccumulatorsForBreakage")]
        private static class Patch_Solver_CheckImpulseAccumulatorsForBreakage
        {
            [HarmonyPostfix]
            private static void Postfix(float __result)
            {
                // __result will be equal to the max stress that was present.
                if (!configPausingIsEnabled.Value)
                {
                    return;
                }
                if (__result == 1f)
                {
                    if ((!firstBreakWasFound) || configEveryBreak.Value)
                    {
                        PauseGame();
                        firstBreakWasFound = true;
                        float highestStress = 0f;
                        float stress;
                        BridgeEdge mostLikelyBroken = new BridgeEdge();
                        foreach (BridgeEdge edge in BridgeEdges.m_Edges)
                        {
                            edge.UnHighlight();
                            if (edge.m_PhysicsEdge)
                            {
                                if (edge.m_PhysicsEdge.handle)
                                {
                                    stress = Math.Abs(edge.m_PhysicsEdge.handle.stressNormalizedSigned);
                                    if (highestStress < stress)
                                    {
                                        highestStress = stress;
                                        mostLikelyBroken = edge;
                                    }
                                }
                            }
                        }
                        if (configHighlightFirstBreak.Value)
                        {
                            if (mostLikelyBroken != null)
                            {
                                mostLikelyBroken.Highlight(Color.red);
                            }
                        }
                    }
                }
            }

            // Just an ease of use function for pausing the game in a way that updates the UI
            private static void PauseGame()
            {
                GameStateSim.Pause();
                Panel_TopBar topBar = GameUI.m_Instance.m_TopBar;
                Time.timeScale = 0f;
                topBar.m_PausedSim = true;
                topBar.m_PauseSimButton.gameObject.SetActive(false);
                topBar.m_UnPauseSimButton.gameObject.SetActive(true);
                AudioMixerManager.PauseSimulationSFX();
                InterfaceAudio.Play("ui_simulation_pause");
            }
        }

        // Tracks when the simulation is completed
        [HarmonyPatch(typeof(GameStateManager), "ChangeState")]
        private static class Patch_GameStateManager_ChangeState
        {
            [HarmonyPostfix]
            private static void Postfix(GameState ___m_GameState)
            {
                if (PluginMain.configIsEnabled.Value)
                {
                    m_GameState = ___m_GameState;
                    if (m_GameState != GameState.SIM)
                    {
                        firstBreakWasFound = false;
                        originMaterials.Clear();
                    }
                }
            }
        }
   }
}
