using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Reflection;
using PolyTechFramework;
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
using Rewired.Demos;
using Vectrosity;

namespace StressTypeVisualization
{
    [BepInPlugin("polytech.stress_type_visualization", "Stress Type Visualization", "1.2.0")]
	[BepInDependency("polytech.polytechframework", "0.7.5")]
	[BepInProcess("Poly Bridge 2.exe")]
    public class PluginMain : PolyTechMod
    {
        public new const string PluginGuid = "polytech.stress_type_visualization";
        public new const string PluginName = "Stress Type Visualization";
        public new const string PluginVersion = "1.2.0";
        private static ConfigEntry<bool> configIsEnabled;
        private static ConfigEntry<bool> configPauseBeforeBreak;
        private static ConfigEntry<bool> configEveryBreak;
        private static ConfigEntry<bool> configHighlightFirstBreak;

        public static bool pauseBeforeBreak = true;
        public static bool highlightFirstBreak = true;
        public static bool everyBreak = false;

        public PluginMain()
        {
            configIsEnabled = base.Config.Bind("General", "Enabled", true, "Enables the mod");
            configPauseBeforeBreak = base.Config.Bind("General", "Pause Before First Break", true, "Makes it so the game will be paused right before the first break occurs.");
            configEveryBreak = base.Config.Bind("General", "Pause Before Every Break", false, "Pauses the game before a break occurs.");
            configHighlightFirstBreak = base.Config.Bind("General", "Highlight First Break", true, "Highlights the first breakage immediately as opposed to when inside build mode immediately as opposed to when inside build mode");
        }

        private void Awake()
        {
            PluginMain.staticLogger = base.Logger;
			new Harmony("polytech.stress_type_visualization").PatchAll(Assembly.GetExecutingAssembly());
            this.setSettings(this.getSettings());
            PolyTechMain.registerMod(this);
        }

        public virtual void enableMod()
        {
            this.isEnabled = true;
            configIsEnabled.Value = true;
        }

        public virtual void disableMod()
        {
            this.isEnabled = false;
            configIsEnabled.Value = false;
        }

        public override void setSettings(string settings)
        {
            this.isEnabled = PluginMain.configIsEnabled.Value;
            pauseBeforeBreak = PluginMain.configPauseBeforeBreak.Value;
            highlightFirstBreak = PluginMain.configHighlightFirstBreak.Value;
            everyBreak = PluginMain.configEveryBreak.Value;
        }

        public override string getSettings()
        {
            return base.Config.ToString();
        }

        public void Update()
        {
            this.setSettings("");
        }

        private static ManualLogSource staticLogger;
        public static bool firstBreakWasFound = false;
        public static GameState m_GameState;

        // Adjusts the stress color
        [HarmonyPatch(typeof(BridgeEdge), "SetStressColor")]
        private static class Patch_BridgeEdge_SetStressColor
        {
            [HarmonyPostfix]
            private static void Postfix(ref BridgeEdge __instance, ref float stressNormalized, ref MaterialPropertyBlock ___m_MaterialPropertyBlock, ref int ___m_ShaderIDForStress, ref int ___m_ShaderIDForColorBlind)
            {
                if (PluginMain.configIsEnabled.Value)
                {
                    if (stressNormalized == 0f)
                    {
                        return;
                    }
                    if (__instance.m_PhysicsEdge)
                    {
                        Edge edge = __instance.m_PhysicsEdge;
                        if (edge.handle)
                        {
                            ___m_MaterialPropertyBlock.SetFloat(___m_ShaderIDForStress, stressNormalized);
                            if (edge.handle.stressNormalizedSigned < 0)
                            {
                                ___m_MaterialPropertyBlock.SetFloat(___m_ShaderIDForColorBlind, Profile.m_ColorBlindModeOn ? 1f : 0f);
                            }
                            else
                            {
                                ___m_MaterialPropertyBlock.SetFloat(___m_ShaderIDForColorBlind, Profile.m_ColorBlindModeOn ? 0f : 1f);
                            }
                            __instance.m_MeshRenderer.SetPropertyBlock(___m_MaterialPropertyBlock);
                        }
                    }
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
                if (!configIsEnabled.Value)
                {
                    return;
                }
                if (__result == 1f)
                {
                    if ((!firstBreakWasFound) || everyBreak)
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
                        if (highlightFirstBreak)
                        {
                            if (mostLikelyBroken != null)
                            {
                                mostLikelyBroken.Highlight(Color.red);
                            }
                        }
                    }
                }
            }

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
                    }
                }
            }
        }
   }
}
