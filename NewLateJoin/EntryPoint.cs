using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace LateJoin
{
    [BepInPlugin("omniscye.latejoin-fork", "Late Join (Fork)", "0.1.3-fix1")]
    public sealed class Entry : BaseUnityPlugin
    {
        internal static Entry Instance { get; private set; }
        internal Harmony Harmony { get; private set; }
        internal static ManualLogSource Log => Instance._log;
        private ManualLogSource _log => base.Logger;

        private void Awake()
        {
            Instance = this;
            Harmony = new Harmony(Info.Metadata.GUID);
            Harmony.PatchAll();
            Log.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} loaded");
        }

        private static void ClearPhotonCacheFor(PhotonView pv)
        {
            if (pv == null) return;
            PhotonNetwork.RemoveBufferedRPCs(pv.ViewID);
        }

        private static bool CanJoinNow()
        {
            try
            {
                return SemiFunc.RunIsLobbyMenu() || SemiFunc.RunIsLobby();
            }
            catch
            {
                return false;
            }
        }

        private static void SetLobbyOpen(bool open)
        {
            try
            {
                if (SteamManager.instance != null)
                {
                    var t = typeof(SteamManager);
                    var inst = SteamManager.instance;
                    var m = t.GetMethod("UnlockLobby", new Type[] { typeof(bool) });
                    if (m != null)
                    {
                        m.Invoke(inst, new object[] { open });
                    }
                    else
                    {
                        m = t.GetMethod("UnlockLobby", Type.EmptyTypes);
                        if (m != null)
                        {
                            m.Invoke(inst, null);
                        }
                        else
                        {
                            m = t.GetMethod("SetLobbyLocked", new Type[] { typeof(bool) });
                            if (m != null)
                            {
                                m.Invoke(inst, new object[] { !open });
                            }
                            else
                            {
                                m = t.GetMethod("LockLobby", Type.EmptyTypes);
                                if (m != null && !open)
                                {
                                    m.Invoke(inst, null);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) PhotonNetwork.CurrentRoom.IsOpen = open;
        }

        private static bool HasPunRpc(PhotonView pv, string name)
        {
            if (pv == null) return false;
            var list = pv.GetComponents<MonoBehaviour>();
            for (int i = 0; i < list.Length; i++)
            {
                var mb = list[i];
                if (mb == null) continue;
                var mi = mb.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;
                var attrs = mi.GetCustomAttributes(typeof(PunRPC), true);
                if (attrs != null && attrs.Length > 0) return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(RunManager), nameof(RunManager.ChangeLevel))]
        static class Patch_RunManager_ChangeLevel
        {
            static void Prefix(RunManager __instance, ref bool _completedLevel, ref bool _levelFailed, ref RunManager.ChangeLevelType _changeLevelType)
            {
                if (!PhotonNetwork.IsMasterClient) return;
                if (_levelFailed) return;
                foreach (var pv in UnityEngine.Object.FindObjectsOfType<PhotonView>())
                {
                    if (pv == null) continue;
                    if (pv.gameObject == null) continue;
                    if (pv.gameObject.scene.buildIndex == -1) continue;
                    var parent = pv.transform.parent;
                    if (parent == null) continue;
                    if (parent.name != "Enemies") continue;
                    ClearPhotonCacheFor(pv);
                }
            }

            static void Postfix(RunManager __instance, bool _completedLevel, bool _levelFailed, RunManager.ChangeLevelType _changeLevelType)
            {
                var open = CanJoinNow();
                SetLobbyOpen(open);
            }
        }

        [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.Spawn))]
        static class Patch_PlayerAvatar_Spawn
        {
            static void Postfix(PlayerAvatar __instance)
            {
                if (!PhotonNetwork.IsMasterClient) return;
                if (__instance == null) return;
                var pv = __instance.photonView;
                if (pv == null) return;
                PhotonNetwork.RemoveBufferedRPCs(pv.ViewID);
            }
        }

        [HarmonyPatch(typeof(LevelGenerator), "Start")]
        static class Patch_LevelGenerator_Start
        {
            static void Postfix(LevelGenerator __instance)
            {
                if (__instance == null) return;
                if (!PhotonNetwork.IsMasterClient) return;
                var pv = __instance.photonView;
                if (pv == null) return;
                if (HasPunRpc(pv, "LoadingLevelAnimationCompletedRPC"))
                    pv.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
            }
        }
    }
}