﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Havok;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using WeaponCore.Platform;
using WeaponCore.Support;
using static WeaponCore.Platform.Weapon;

namespace WeaponCore
{
    public partial class Session
    {
        internal struct DebugLine
        {
            internal enum LineType
            {
                MainTravel,
                MainHit,
                ShrapnelTravel,
                ShrapnelHit,
            }

            internal LineType Type;
            internal LineD Line;
            internal uint StartTick;

            internal bool Draw(uint tick)
            {
                Color color = new Color();
                switch (Type) {
                    case LineType.MainTravel:
                        color = Color.Blue;
                        break;
                    case LineType.MainHit:
                        color = Color.Red;
                        break;
                    case LineType.ShrapnelTravel:
                        color = Color.Green;
                        break;
                    case LineType.ShrapnelHit:
                        color = Color.Orange;
                        break;
                }

                DsDebugDraw.DrawLine(Line, color, 0.1f);

                return tick - StartTick < 1200;
            }
        }

        internal class ProblemReport
        {
            internal readonly Dictionary<string, Dictionary<string, Func<string>>> AllDicts = new Dictionary<string, Dictionary<string, Func<string>>>();
            internal readonly Dictionary<string, Func<string>> SessionFields;
            internal readonly Dictionary<string, Func<string>> AiFields;
            internal readonly Dictionary<string, Func<string>> CompFields;
            internal readonly Dictionary<string, Func<string>> PlatformFields;
            internal readonly Dictionary<string, Func<string>> WeaponFields;

            internal Session Session;
            internal bool Generating;
            internal MyCubeBlock TargetBlock;
            internal DataReport MyData;
            internal DataReport RemoteData;
            internal MyWeaponPlatform TmpPlatform;
            internal string Report;
            internal uint RequestTime = 1800;
            internal uint LastRequestTick = uint.MaxValue - 7200;

            internal ProblemReport(Session session)
            {
                Session = session;
                SessionFields = InitSessionFields();
                AiFields = InitAiFields();
                CompFields = InitCompFields();
                PlatformFields = InitPlatformFields();
                WeaponFields = InitWeaponFields();

                AllDicts.Add("Session", SessionFields);
                AllDicts.Add("Ai", AiFields);
                AllDicts.Add("Comp", CompFields);
                AllDicts.Add("Platform", PlatformFields);
                AllDicts.Add("Weapon", WeaponFields);
            }

            internal void GenerateReport(MyCubeBlock targetBlock)
            {
                if (Generating || Session.Tick - LastRequestTick < RequestTime) {
                    return;
                }
                Log.Line($"Generate User Weapon Report");
                Generating = true;
                LastRequestTick = Session.Tick;
                TargetBlock = targetBlock;
                MyData = new DataReport();

                if (!Session.DedicatedServer)
                    MyAPIGateway.Utilities.ShowNotification($"Generating a error report for WC Block: {TargetBlock.BlockDefinition.Id.SubtypeName} - with id: {TargetBlock.EntityId}", 7000, "Red");

                if (Session.IsServer) {

                    Compile();
                    if (Session.MpActive) {
                        foreach (var player in Session.Players)
                            NetworkTransfer(false, player.Value.SteamUserId);
                    }
                }
                else {
                    Compile();
                    NetworkTransfer(true);
                }
                Session.FutureEvents.Schedule(CompleteReport, null, 300);
            }

            internal DataReport PullData(MyCubeBlock targetBlock)
            {
                MyData = new DataReport();
                TargetBlock = targetBlock;

                Compile();
                
                return MyData;
            }

            internal void Compile()
            {
                try
                {
                    BuildData(MyData);
                }
                catch (Exception ex) { Log.Line($"Exception in ReportCompile: {ex}"); }
            }

            internal void BuildData(DataReport data)
            {
                foreach (var d in AllDicts) {
                    foreach (var f in d.Value) {
                        var value = f.Value.Invoke();
                        GetStorage(data, d.Key)[f.Key] = value;
                    }
                }
            }


            internal string[] IndexToString = { "Session", "Ai", "Platform", "Comp", "Weapon" };
            internal Dictionary<string, string> GetStorage(DataReport data, string storageName)
            {
                switch (storageName)
                {
                    case "Session":
                        return data.Session;
                    case "Ai":
                        return data.Ai;
                    case "Comp":
                        return data.Comp;
                    case "Platform":
                        return data.Platform;
                    case "Weapon":
                        return data.Weapon;
                    default:
                        return null;
                }
            }

            internal void NetworkTransfer(bool toServer, ulong clientId = 0, DataReport data = null)
            {
                if (toServer) {
                    Session.PacketsToServer.Add(new ProblemReportPacket
                    {
                        SenderId = Session.MultiplayerId,
                        PType = PacketType.ProblemReport,
                        EntityId = TargetBlock.EntityId,
                        Type = ProblemReportPacket.RequestType.RequestServerReport,
                    });
                }
                else {
                    Session.PacketsToClient.Add(new PacketInfo {
                        Packet = new ProblemReportPacket {
                            SenderId = clientId,
                            PType = PacketType.ProblemReport,
                            Data = data,
                            Type = ProblemReportPacket.RequestType.SendReport,

                        },
                        SingleClient = true,
                    });
                }
            }

            internal void CompleteReport(object o)
            {
                if (Session.MpActive && (RemoteData == null || MyData == null))
                {
                    Log.Line($"RemoteData:{RemoteData !=null} - MyData:{MyData!= null}, null data detected, waiting 10 second");
                    Clean();
                    return;
                }
                CompileReport();

                Log.CleanLine($"{Report}", "report");

                Clean();
            }

            internal void CompileReport()
            {
                Report = string.Empty;
                var myRole = !Session.MpActive ? "" : Session.IsClient ? "Client:" : "Server:";
                var otherRole = !Session.MpActive ? "" : Session.IsClient ? "Server:" : "Client:";
                var loopCnt = Session.MpActive ? 2 : 1;
                var lastLoop = loopCnt > 1 ? 1 : 0;

                for (int x = 0; x < loopCnt; x++)
                {
                    
                    if (x != lastLoop)
                        Report += $"\n== Mismatched variables ==\n";
                    else if (x == lastLoop && lastLoop > 0)
                        Report += $"== End of mismatch section ==\n\n";

                    for (int i = 0; i < 5; i++)
                    {
                        var indexString = IndexToString[i];
                        var myStorage = GetStorage(MyData, indexString);
                        var storageCnt = Session.MpActive ? 2 : 1;
                        Report += $"Class: {indexString}\n";

                        foreach (var p in myStorage)
                        {
                            if (storageCnt > 1)
                            {
                                var remoteStorage = GetStorage(RemoteData, indexString);
                                var remoteValue = remoteStorage[p.Key];
                                if (x == lastLoop) Report += $"    [{p.Key}]\n      {myRole}{p.Value} - {otherRole}{remoteValue} - Matches:{p.Value == remoteValue}\n";
                                else if (p.Value != remoteValue && !MatchSkip.Contains(p.Key)) Report += $"    [{p.Key}]\n      {myRole}{p.Value} - {otherRole}{remoteValue}\n";
                            }
                            else
                            {
                                if (x == lastLoop) Report += $"    [{p.Key}]\n      {myRole}{p.Value}\n";
                            }
                        }
                    }
                }
            }
            
            internal HashSet<string> MatchSkip = new HashSet<string> { "AcquireEnabled", "AcquireAsleep", "WeaponReadyTick", "AwakeComps" };
        
            internal Dictionary<string, Func<string>> InitSessionFields()
            {
                var sessionFields = new Dictionary<string, Func<string>>
                {
                    {"HasFatMap", () => (GetComp() != null && Session.GridToFatMap.ContainsKey(GetComp().MyCube.CubeGrid)).ToString()},
                    {"HasGridAi", () => (GetComp() != null && Session.GridTargetingAIs.ContainsKey(GetComp().MyCube.CubeGrid)).ToString()},
                };

                return sessionFields;
            }

            internal Dictionary<string, Func<string>> InitAiFields()
            {
                var aiFields = new Dictionary<string, Func<string>>
                {
                    {"Version", () => GetAi()?.Version.ToString() ?? string.Empty },
                    {"RootAiId", () => GetAi()?.Construct.RootAi?.MyGrid.EntityId.ToString() ?? string.Empty },
                    {"SubGrids", () => GetAi()?.SubGrids.Count.ToString() ?? string.Empty },
                    {"BlockGroups", () => GetAi()?.Construct.Data.Repo.BlockGroups.Count.ToString() ?? string.Empty },
                    {"AiSleep", () => GetAi()?.AiSleep.ToString() ?? string.Empty },
                    {"ControllingPlayers", () => GetAi()?.Data.Repo.ControllingPlayers.Count.ToString() ?? string.Empty },
                    {"Inventories", () => GetAi()?.Inventories.Count.ToString() ?? string.Empty },
                    {"SortedTargets", () => GetAi()?.SortedTargets.Count.ToString() ?? string.Empty },
                    {"Obstructions", () => GetAi()?.Obstructions.Count.ToString() ?? string.Empty },
                    {"NearByEntities", () => GetAi()?.NearByEntities.ToString() ?? string.Empty },
                    {"TargetAis", () => GetAi()?.TargetAis.Count.ToString() ?? string.Empty },
                    {"WeaponBase", () => GetAi()?.WeaponBase.Count.ToString() ?? string.Empty },
                    {"ThreatRangeSqr", () => GetAi()?.TargetingInfo.ThreatRangeSqr.ToString(CultureInfo.InvariantCulture).Substring(0, 6) ?? string.Empty },
                    {"MyOwner", () => GetAi()?.MyOwner.ToString() ?? string.Empty },
                    {"AwakeComps", () => GetAi()?.AwakeComps.ToString() ?? string.Empty },
                    {"BlockCount", () => GetAi()?.BlockCount.ToString() ?? string.Empty },
                    {"WeaponsTracking", () => GetAi()?.WeaponsTracking.ToString() ?? string.Empty },
                    {"GridAvailablePower", () => GetAi()?.GridAvailablePower.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
                    {"MaxTargetingRange", () => GetAi()?.MaxTargetingRange.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
                };

                return aiFields;
            }

            internal Dictionary<string, Func<string>> InitCompFields()
            {
                var compFields = new Dictionary<string, Func<string>>
                {
                    {"IsAsleep", () => GetComp()?.IsAsleep.ToString() ?? string.Empty },
                    {"GridId", () => GetComp()?.MyCube.CubeGrid.EntityId.ToString() ?? string.Empty },
                    {"BaseType", () => GetComp()?.BaseType.ToString() ?? string.Empty },
                    {"AiGridMatchCubeGrid", () => (GetComp()?.Ai?.MyGrid == GetComp()?.MyCube.CubeGrid).ToString() ?? string.Empty },
                    {"IsWorking", () => GetComp()?.IsWorking.ToString() ?? string.Empty },
                    {"cubeIsWorking", () => GetComp()?.MyCube.IsWorking.ToString() ?? string.Empty },
                    {"MaxTargetDistance", () => GetComp()?.MaxTargetDistance.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
                    {"Status", () => GetComp()?.Status.ToString() ?? string.Empty },
                    {"ControlType", () => GetComp()?.Data.Repo.State.Control.ToString() ?? string.Empty },
                    {"PlayerId", () => GetComp()?.Data.Repo.State.PlayerId.ToString() ?? string.Empty },
                    {"Activate", () => GetComp()?.Data.Repo.Set.Overrides.Activate.ToString() ?? string.Empty },
                    {"FocusSubSystem", () => GetComp()?.Data.Repo.Set.Overrides.FocusSubSystem.ToString() ?? string.Empty },
                    {"FocusTargets", () => GetComp()?.Data.Repo.Set.Overrides.FocusTargets.ToString() ?? string.Empty },
                };

                return compFields;
            }

            internal Dictionary<string, Func<string>> InitPlatformFields()
            {
                var platformFields = new Dictionary<string, Func<string>>
                {
                    {"State", () => GetPlatform()?.State.ToString() ?? string.Empty },
                };

                return platformFields;
            }

            internal Dictionary<string, Func<string>> InitWeaponFields()
            {
                var weaponFields = new Dictionary<string, Func<string>>
                {
                    {"AiEnabled", () => {
                        var message = string.Empty;
                        return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.AiEnabled}"); }
                    },
                    {"AcquireEnabled", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Acquire.Enabled}"); }
                    },
                    {"AcquireAsleep", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Acquire.Asleep}"); }
                    },
                    {"MaxTargetDistance", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.MaxTargetDistance}"); }
                    },
                    {"AmmoName", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.ActiveAmmoDef.AmmoName}"); }
                    },
                    {"RateOfFire", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.RateOfFire}"); }
                    },
                    {"Dps", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Dps}"); }
                    },
                    {"ShotReady", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.ShotReady}"); }
                    },
                    {"LastHeat", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.LastHeat}"); }
                    },
                    {"HasTarget", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Target.HasTarget}"); }
                    },
                    {"TargetCurrentState", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Target.CurrentState}"); }
                    },
                    {"TargetIsEntity", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Target.Entity != null}"); }
                    },
                    {"TargetEntityId", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.TargetData.EntityId}"); }
                    },
                    {"IsShooting", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.IsShooting}"); }
                    },
                    {"NoMagsToLoad", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.NoMagsToLoad}"); }
                    },
                    {"Reloading", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Reloading}"); }
                    },
                    {"WeaponReadyTick", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.System.Session.Tick - w.WeaponReadyTick}"); }
                    },
                    {"Charging", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Charging}"); }
                    },
                    {"AmmoTypeId", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Reload.AmmoTypeId}"); }
                    },
                    {"Action", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.State.Action}"); }
                    },
                    {"ShotsFired", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.ShotsFired}"); }
                    },
                    {"ShootOnce", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.ShootOnce}"); }
                    },
                    {"Overheated", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.State.Overheated}"); }
                    },
                    {"Heat", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.State.Heat}"); }
                    },
                    {"CurrentAmmo", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Reload.CurrentAmmo}"); }
                    },
                    {"CurrentCharge", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Reload.CurrentCharge}"); }
                    },
                    {"CurrentMags", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.Reload.CurrentMags}"); }
                    },
                    {"LastEventCanDelay", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.LastEventCanDelay}"); }
                    },
                    {"AnimationDelay", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.AnimationDelayTick <= w.Comp.Session.Tick}"); }
                    },
                    {"ShootDelay", () => {
                            var message = string.Empty;
                            return !TryGetValidPlatform(out TmpPlatform) ? string.Empty : TmpPlatform.Weapons.Aggregate(message, (current, w) => current + $"{w.ShootTick <= w.Comp.Session.Tick}"); }
                    },

                };

                return weaponFields;
            }


            internal GridAi GetAi()
            {
                GridAi ai;
                if (Session.GridTargetingAIs.TryGetValue(TargetBlock.CubeGrid, out ai))
                {
                    return ai;
                }
                return null;

            }

            internal WeaponComponent GetComp()
            {
                GridAi ai;
                if (Session.GridTargetingAIs.TryGetValue(TargetBlock.CubeGrid, out ai))
                {
                    WeaponComponent comp;
                    if (ai.WeaponBase.TryGetValue(TargetBlock, out comp))
                    {
                        return comp;
                    }
                }
                return null;

            }

            internal MyWeaponPlatform GetPlatform()
            {
                GridAi ai;
                if (Session.GridTargetingAIs.TryGetValue(TargetBlock.CubeGrid, out ai))
                {
                    WeaponComponent comp;
                    if (ai.WeaponBase.TryGetValue(TargetBlock, out comp))
                    {
                        return comp.Platform;
                    }
                }
                return null;

            }

            internal bool TryGetValidPlatform(out MyWeaponPlatform platform)
            {
                platform = GetPlatform();
                return platform != null;
            }

            internal void Clean()
            {
                MyData = null;
                RemoteData = null;
                TargetBlock = null;
                Generating = false;
                Log.Line("Clean");
            }
        }

        internal class TerminalMonitor
        {
            internal readonly Dictionary<WeaponComponent, long> ServerTerminalMaps = new Dictionary<WeaponComponent, long>();
            internal Session Session;
            internal WeaponComponent Comp;
            internal int OriginalAiVersion;
            internal bool Active;

            internal TerminalMonitor(Session session)
            {
                Session = session;
            }
            internal void Monitor()
            {
                if (IsActive()) {
                    if (Session.Tick20)
                        Comp.TerminalRefresh();
                }
                else if (Active)
                    Clean();
            }
            internal bool IsActive()
            {
                if (Comp?.Ai == null) return false;

                var sameVersion = Comp.Ai.Version == OriginalAiVersion;
                var nothingMarked = !Comp.MyCube.MarkedForClose && !Comp.Ai.MyGrid.MarkedForClose && !Comp.Ai.MyGrid.MarkedForClose;
                var sameGrid = Comp.MyCube.CubeGrid == Comp.Ai.MyGrid;
                var inTerminalWindow = Session.InMenu && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel;
                var compReady = Comp.Platform.State == MyWeaponPlatform.PlatformState.Ready;
                var sameTerminalBlock = Session.LastTerminal != null && (Session.LastTerminal.EntityId == Comp.Ai.Data.Repo.ActiveTerminal || Session.IsClient && (Comp.Ai.Data.Repo.ActiveTerminal == 0 || Comp.Ai.Data.Repo.ActiveTerminal ==  Comp.MyCube.EntityId));
                var isActive = (sameVersion && nothingMarked && sameGrid && compReady && inTerminalWindow && sameTerminalBlock);
                return isActive;
            }

            internal void HandleInputUpdate(WeaponComponent comp)
            {
                if (Active && (Comp != comp || OriginalAiVersion !=  comp.Ai.Version))
                    Clean();

                Session.LastTerminal = (IMyTerminalBlock)comp.MyCube;

                Comp = comp;

                OriginalAiVersion = comp.Ai.Version;

                if (comp.IsAsleep)
                    comp.WakeupComp();

                if (Session.IsClient && !Active)  
                    Session.SendActiveTerminal(comp);
                else if (Session.IsServer)
                    ServerUpdate(Comp);

                Active = true;
            }

            internal void Clean(bool purge = false)
            {
                if (Session.MpActive && Session.HandlesInput && Comp != null && !purge) {
                    uint[] mIds;
                    if (Session.PlayerMIds.TryGetValue(Session.MultiplayerId, out mIds))
                    {
                        Session.PacketsToServer.Add(new TerminalMonitorPacket
                        {
                            SenderId = Session.MultiplayerId,
                            PType = PacketType.TerminalMonitor,
                            EntityId = Comp.MyCube.EntityId,
                            State = TerminalMonitorPacket.Change.Clean,
                            MId = ++mIds[(int)PacketType.TerminalMonitor],
                        });
                    }
                }

                if (!purge && Session.IsServer)
                    ServerClean(Comp);

                Comp = null;
                OriginalAiVersion = -1;
                Active = false;
            }


            internal void ServerUpdate(WeaponComponent comp)
            {
                long aTermId;
                if (!ServerTerminalMaps.TryGetValue(comp, out aTermId)) {
                    ServerTerminalMaps[comp] = comp.MyCube.EntityId;
                    //if (!Session.LocalVersion) Log.Line($"ServerUpdate added Id");
                }
                else {

                    var cube = MyEntities.GetEntityByIdOrDefault(aTermId) as MyCubeBlock;
                    if (cube != null && cube.CubeGrid.EntityId != comp.Ai.MyGrid.EntityId)
                    {
                        ServerTerminalMaps[comp] = 0;
                        //if (!Session.LocalVersion) Log.Line($"ServerUpdate reset Id");
                    }
                }

                comp.Ai.Data.Repo.ActiveTerminal = comp.MyCube.EntityId;

                if (comp.IsAsleep)
                    comp.WakeupComp();

                if (Session.MpActive)
                    Session.SendAiData(comp.Ai);
            }

            internal void ServerClean(WeaponComponent comp)
            {
                if (ServerTerminalMaps.ContainsKey(comp))
                {
                    ServerTerminalMaps[comp] = 0;
                    comp.Ai.Data.Repo.ActiveTerminal = 0;

                    if (Session.MpActive)
                        Session.SendAiData(comp.Ai);
                }
                else
                    Log.Line($"ServerClean failed ");
            }

            internal void Purge()
            {
                Clean(true);
                Session = null;
            }
        }

        internal class AcquireManager
        {
            internal Session Session;
            internal readonly HashSet<WeaponAcquire> Awake = new HashSet<WeaponAcquire>();
            internal readonly HashSet<WeaponAcquire> Asleep = new HashSet<WeaponAcquire>();

            internal readonly List<WeaponAcquire> Collector = new List<WeaponAcquire>();
            internal readonly List<WeaponAcquire> Removal = new List<WeaponAcquire>();

            internal int LastSleepSlot = -1;
            internal int LastAwakeSlot = -1;
            internal int WasAwake;
            internal int WasAsleep;

            internal AcquireManager(Session session)
            {
                Session = session;
            }

            internal void Awaken(WeaponAcquire wa)
            {
                var notValid = !wa.Weapon.Comp.IsWorking || !wa.Weapon.Comp.Data.Repo.Set.Overrides.Activate || !wa.Weapon.TrackTarget || Session.IsClient;
                if (notValid)
                {
                    if (!Session.IsClient) Log.Line($"[Awaken] isAsleep:{wa.Asleep} - cWorking:{wa.Weapon.Comp.IsWorking} - cOverride:{wa.Weapon.Comp.Data.Repo.Set.Overrides.Activate} - tracking:{wa.Weapon.TrackTarget} - isClient:{Session.IsClient}");
                    return;
                }

                wa.CreatedTick = Session.Tick;

                if (!wa.Asleep)
                    return;

                Asleep.Remove(wa);

                AddAwake(wa);
            }

            internal void AddAwake(WeaponAcquire wa)
            {
                var notValid = !wa.Weapon.Comp.IsWorking || !wa.Weapon.Comp.Data.Repo.Set.Overrides.Activate || !wa.Weapon.TrackTarget || Session.IsClient;
                if (notValid)
                {
                    if (!Session.IsClient) Log.Line($"[AddAwake] isAsleep:{wa.Asleep} - cWorking:{wa.Weapon.Comp.IsWorking} - cOverride:{wa.Weapon.Comp.Data.Repo.Set.Overrides.Activate} - tracking:{wa.Weapon.TrackTarget} - isClient:{Session.IsClient}");
                    return;
                }

                wa.Enabled = true;
                wa.Asleep = false;
                wa.CreatedTick = Session.Tick;

                if (LastAwakeSlot < AwakeBuckets - 1)
                {

                    wa.SlotId = ++LastAwakeSlot;

                    Awake.Add(wa);
                }
                else
                {

                    wa.SlotId = LastAwakeSlot = 0;

                    Awake.Add(wa);
                }
            }

            internal void Remove(WeaponAcquire wa)
            {
                wa.Enabled = false;

                if (wa.Asleep)
                {

                    wa.Asleep = false;
                    Asleep.Remove(wa);
                }
                else
                {
                    Awake.Remove(wa);
                }
            }


            internal void UpdateAsleep()
            {
                WasAwake = 0;
                WasAwake += Awake.Count;

                foreach (var wa in Awake)
                {

                    if (wa.Weapon.Target.HasTarget)
                    {
                        Removal.Add(wa);
                        continue;
                    }

                    if (Session.Tick - wa.CreatedTick > 599)
                    {

                        if (LastSleepSlot < AsleepBuckets - 1)
                        {

                            wa.SlotId = ++LastSleepSlot;
                            wa.Asleep = true;

                            Asleep.Add(wa);
                            Removal.Add(wa);
                        }
                        else
                        {

                            wa.SlotId = LastSleepSlot = 0;
                            wa.Asleep = true;

                            Asleep.Add(wa);
                            Removal.Add(wa);
                        }
                    }
                }

                for (int i = 0; i < Removal.Count; i++)
                    Awake.Remove(Removal[i]);

                Removal.Clear();
            }


            internal void ReorderSleep()
            {
                foreach (var wa in Asleep)
                {

                    var remove = wa.Weapon.Target.HasTarget || wa.Weapon.Comp.IsAsleep || !wa.Weapon.Comp.IsWorking || !wa.Weapon.Comp.Data.Repo.Set.Overrides.Activate || Session.IsClient || !wa.Weapon.TrackTarget;

                    if (remove)
                    {
                        Removal.Add(wa);
                        continue;
                    }
                    Collector.Add(wa);
                }

                Asleep.Clear();

                for (int i = 0; i < Removal.Count; i++)
                    Remove(Removal[i]);

                WasAsleep = Collector.Count;

                ShellSort(Collector);

                LastSleepSlot = -1;
                for (int i = 0; i < Collector.Count; i++)
                {

                    var wa = Collector[i];
                    if (LastSleepSlot < AsleepBuckets - 1)
                    {

                        wa.SlotId = ++LastSleepSlot;

                        Asleep.Add(wa);
                    }
                    else
                    {

                        wa.SlotId = LastSleepSlot = 0;

                        Asleep.Add(wa);
                    }
                }
                Collector.Clear();
                Removal.Clear();
            }

            static void ShellSort(List<WeaponAcquire> list)
            {
                int length = list.Count;

                for (int h = length / 2; h > 0; h /= 2)
                {
                    for (int i = h; i < length; i += 1)
                    {
                        var tempValue = list[i];
                        var temp = list[i].Weapon.UniqueId;

                        int j;
                        for (j = i; j >= h && list[j - h].Weapon.UniqueId > temp; j -= h)
                        {
                            list[j] = list[j - h];
                        }

                        list[j] = tempValue;
                    }
                }
            }

            internal void Clean()
            {
                Awake.Clear();
                Asleep.Clear();
                Collector.Clear();
                Removal.Clear();
            }

        }

        public class InventoryUpdate
        {
            public readonly Dictionary<uint, int> Add = new Dictionary<uint, int>();
            public readonly Dictionary<uint, int> Remove = new Dictionary<uint, int>();
            public readonly Dictionary<uint, int> Change = new Dictionary<uint, int>();
            public readonly List<Weapon> WeaponsToUpdate = new List<Weapon>();

            public void Update(MyInventory inventory, Session session)
            {
                ComputeChange();
                foreach (var c in Change)  {

                    Log.Line($"change:{c.Value}");
                    if (c.Value > 0)  {
                        foreach (var a in Add)
                            if (a.Key == c.Key)
                            {
                                Log.Line($"added: {c.Key} - {c.Value} - {session.AmmoItems[c.Key].Content == null}");
                                inventory.Add(session.AmmoItems[c.Key], c.Value);
                            }
                    }
                    else if (c.Value < 0)
                    {
                        Log.Line($"remove: {c.Key} - {c.Value}");
                        inventory.RemoveItems(c.Key, c.Value);
                    }
                }

                foreach (var w in WeaponsToUpdate)  {
                    w.Reload.CurrentMags = w.Comp.BlockInventory.GetItemAmount(w.ActiveAmmoDef.AmmoDefinitionId);
                    w.PullingAmmo = false;
                }
                //session.InventoryUpdatePool.Return(this);
            }

            public void ComputeChange()
            {
                foreach (var a in Add)
                {
                    int oldAmount;
                    if (Change.TryGetValue(a.Key, out oldAmount))
                        Change[a.Key] = oldAmount + a.Value;
                    else
                        Change[a.Key] = a.Value;
                }

                foreach (var r in Remove)
                {
                    int oldAmount;
                    if (Change.TryGetValue(r.Key, out oldAmount))
                        Change[r.Key] = oldAmount - r.Value;
                    else
                        Change[r.Key] = r.Value * -1;
                }
            }

            public void Clean()
            {
                Add.Clear();
                Remove.Clear();
                Change.Clear();
                WeaponsToUpdate.Clear();
            }
        }

        public class WeaponAmmoMoveRequest
        {
            public Weapon Weapon;
            public List<InventoryMags> Inventories = new List<InventoryMags>();

            public void Clean()
            {
                Weapon = null;
                Inventories.Clear();
            }
        }

        public struct InventoryMags
        {
            public MyInventory Inventory;
            public BetterInventoryItem Item;
            public int Amount;
        }

        public class BetterInventoryItem
        {
            private int _amount;
            public MyObjectBuilder_PhysicalObject Content;
            public uint ItemId;
            public MyDefinitionId DefId;

            public int Amount
            {
                get { return _amount; }
                set { Interlocked.Exchange(ref _amount, value);  }
            }
        }
    }
}
