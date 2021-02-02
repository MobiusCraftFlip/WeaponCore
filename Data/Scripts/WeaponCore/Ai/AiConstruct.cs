﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using WeaponCore.Platform;
using static WeaponCore.FocusData;
namespace WeaponCore.Support
{
    public partial class Ai
    {
        public void SubGridDetect()
        {
            if (PrevSubGrids.Count == 0) return;

            AddSubGrids.Clear();
            foreach (var sub in PrevSubGrids)
            {
                var grid = (MyCubeGrid)sub;
                AddSubGrids.Add(grid);
                TmpSubGrids.Add(grid);
            }

            TmpSubGrids.IntersectWith(RemSubGrids);
            RemSubGrids.ExceptWith(AddSubGrids);
            AddSubGrids.ExceptWith(TmpSubGrids);
            TmpSubGrids.Clear();

            SubGridsChanged = AddSubGrids.Count != 0 || RemSubGrids.Count != 0;
        }

        public void SubGridChanges(bool clean = false, bool dupCheck = false)
        {
            foreach (var grid in AddSubGrids) {
                
                if (grid == TopEntity) continue;
                RegisterSubGrid(grid, dupCheck);

            }
            AddSubGrids.Clear();

            foreach (var grid in RemSubGrids) {
                
                if (grid == TopEntity) continue;
                UnRegisterSubGrid(grid);
            }
            RemSubGrids.Clear();

            if (!clean)
                UpdateRoot();
        }

        public void UpdateRoot()
        {
            Construct.Refresh(this, Constructs.RefreshCaller.SubGridChange);
            
            foreach (var grid in SubGrids) {
                
                if (Construct.RootAi != null)
                    Session.GridToMasterAi[grid] = Construct.RootAi;
                else Log.Line($"Construct.RootAi is null");
            }
        }

        public void RegisterSubGrid(MyCubeGrid grid, bool dupCheck = false)
        {
            if (dupCheck && SubGridsRegistered.Contains(grid))
                Log.Line($"sub Grid Already Registered: [Main]:{grid == TopEntity}");

            grid.Flags |= (EntityFlags)(1 << 31);
            grid.OnFatBlockAdded += FatBlockAdded;
            grid.OnFatBlockRemoved += FatBlockRemoved;

            SubGridsRegistered.Add(grid);

            foreach (var cube in grid.GetFatBlocks()) {

                var battery = cube as MyBatteryBlock;
                if (battery != null || cube.HasInventory)
                {
                    FatBlockAdded(cube);
                }
            }
        }

        public void UnRegisterSubGrid(MyCubeGrid grid, bool clean = false)
        {
            if (!SubGridsRegistered.Contains(grid)) {
                Log.Line($"sub Grid Already UnRegistered: [Main]:{grid == TopEntity}");
            }

            if (!clean) SubGrids.Remove(grid);

            SubGridsRegistered.Remove(grid);
            grid.OnFatBlockAdded -= FatBlockAdded;
            grid.OnFatBlockRemoved -= FatBlockRemoved;

            foreach (var cube in grid.GetFatBlocks()) {
                
                var battery = cube as MyBatteryBlock;
                if (InventoryMonitor.ContainsKey(cube) || battery != null && Batteries.Contains(battery))
                {
                    FatBlockRemoved(cube);
                }
            }

            Ai removeAi;
            if (!Session.GridTargetingAIs.ContainsKey(grid))
                Session.GridToMasterAi.TryRemove(grid, out removeAi);
        }

        public void CleanSubGrids()
        {
            foreach (var grid in SubGrids) {
                if (grid == TopEntity) continue;
                UnRegisterSubGrid(grid, true);
            }

            SubGrids.Clear();
            RemSubGrids.Clear();
            AddSubGrids.Clear();
            TmpSubGrids.Clear();
            SubGridsChanged = false;
        } 

        public class Constructs
        {
            internal readonly HashSet<MyDefinitionId> RecentItems = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
            internal readonly HashSet<Unit> OutOfAmmoWeapons = new HashSet<Unit>();
            internal readonly List<Ai> RefreshedAis = new List<Ai>();
            internal readonly Dictionary<MyStringHash, int> Counter = new Dictionary<MyStringHash, int>(MyStringHash.Comparer);
            internal readonly Focus Focus = new Focus();
            internal readonly ConstructData Data = new ConstructData();
            internal float OptimalDps;
            internal int BlockCount;
            internal Ai RootAi;
            internal Ai LargestAi;
            internal bool NewInventoryDetected;
            
            internal enum RefreshCaller
            {
                Init,
                SubGridChange,
            }

            internal enum UpdateType
            {
                Full,
                Focus,
                None,
            }

            internal void Refresh(Ai ai, RefreshCaller caller)
            {
                if (RootAi.Session.IsServer && RootAi.Construct.RecentItems.Count > 0) 
                    CheckEmptyWeapons();

                OptimalDps = 0;
                BlockCount = 0;
                GridMap gridMap;
                if (ai.Session.GridToInfoMap.TryGetValue(ai.TopEntity, out gridMap)) {
                    Ai leadingAi = null;
                    Ai largestAi = null;
                    int leadingBlocks = 0;
                    foreach (var grid in ai.SubGrids) {

                        Ai thisAi;
                        if (ai.Session.GridTargetingAIs.TryGetValue(grid, out thisAi)) {
                            
                            if (leadingAi == null)
                                leadingAi = thisAi;
                            else  {

                                if (leadingAi.TopEntity.EntityId > grid.EntityId)
                                    leadingAi = thisAi;
                            }
                        } 
                        if (ai.Session.GridToInfoMap.TryGetValue(grid, out gridMap)) {
                            var blockCount = ai.Session.GridToInfoMap[grid].MostBlocks;
                            if (blockCount > leadingBlocks)
                            {
                                leadingBlocks = blockCount;
                                largestAi = thisAi;
                            }
                            BlockCount += blockCount;
                            if (thisAi != null) OptimalDps += thisAi.OptimalDps;
                        }
                        else Log.Line($"ConstructRefresh Failed sub no GridMap, sub is caller:{grid == ai.TopEntity}");
                    }
                    RootAi = leadingAi;
                    LargestAi = largestAi;
                    if (RootAi == null) {
                        Log.Line($"[rootAi is null in Update] - caller:{caller}, forcing rootAi to caller - inGridTarget:{ai.Session.GridTargetingAIs.ContainsKey(ai.TopEntity)} -  myGridMarked:{ai.TopEntity.MarkedForClose} - aiMarked:{ai.MarkedForClose} - lastClosed:{ai.AiCloseTick} - aiSpawned:{ai.AiSpawnTick} - diff:{ai.AiSpawnTick - ai.AiCloseTick} - sinceSpawn:{ai.Session.Tick - ai.AiSpawnTick}");
                        RootAi = ai;
                    }
                    
                    if (LargestAi == null)
                        LargestAi = ai;
                    
                    UpdateWeaponCounters(ai);
                    return;
                }
                Log.Line($"ConstructRefresh Failed main Ai no GridMap: {caller} - Marked: {ai.TopEntity.MarkedForClose}");
                RootAi = null;
                LargestAi = null;
            }


            internal void UpdateConstruct(UpdateType type, bool sync = true)
            {
                switch (type)
                {
                    case UpdateType.Full:
                    {
                        UpdateLeafs();
                        if (RootAi.Session.MpActive && RootAi.Session.IsServer && sync)
                            RootAi.Session.SendConstruct(RootAi);
                        break;
                    }
                    case UpdateType.Focus:
                    {
                        UpdateLeafFoci();
                        if (RootAi.Session.MpActive && RootAi.Session.IsServer && sync)
                            RootAi.Session.SendConstructFoci(RootAi);
                        break;
                    }
                }
            }

            internal void UpdateConstructsPlayers(MyEntity entity, long playerId, bool updateAdd)
            {
                foreach (var sub in RootAi.SubGrids)
                {
                    Ai ai;
                    if (RootAi.Session.GridTargetingAIs.TryGetValue(sub, out ai))
                    {
                        UpdateActiveControlDictionary(ai, entity, playerId, updateAdd);
                    }
                }
            }

            public static void UpdateActiveControlDictionary(Ai ai, MyEntity entity, long playerId, bool updateAdd)
            {
                if (updateAdd) //update/add
                {
                    ai.Data.Repo.ControllingPlayers[playerId] = entity.EntityId;
                    ai.AiSleep = false;
                }
                else //remove
                {
                    if (ai.Data.Repo.ControllingPlayers.Remove(playerId) && ai.Data.Repo.ControllingPlayers.Count == 0)
                    {
                        if (ai.Session.MpActive)
                            ai.Session.SendConstruct(ai);
                    }
                    ai.AiSleep = false;
                }
                if (ai.Session.MpActive)
                    ai.Session.SendAiData(ai);
            }

            internal static void UpdateWeaponCounters(Ai cAi)
            {
                cAi.Construct.RefreshedAis.Clear();
                cAi.Construct.RefreshedAis.Add(cAi);

                if (cAi.SubGrids.Count > 1) {
                    foreach (var sub in cAi.SubGrids) {
                        if (sub == null || sub == cAi.TopEntity)
                            continue;

                        Ai subAi;
                        if (cAi.Session.GridTargetingAIs.TryGetValue(sub, out subAi))
                            cAi.Construct.RefreshedAis.Add(subAi);
                    }
                }

                for (int i = 0; i < cAi.Construct.RefreshedAis.Count; i++) {

                    var checkAi = cAi.Construct.RefreshedAis[i];
                    checkAi.Construct.Counter.Clear();

                    for (int x = 0; x < cAi.Construct.RefreshedAis.Count; x++) {
                        foreach (var wc in cAi.Construct.RefreshedAis[x].UnitCounter)
                            checkAi.Construct.AddWeaponCount(wc.Key, wc.Value.Current);
                    }
                }
            }

            internal void AddWeaponCount(MyStringHash weaponHash, int incrementBy = 1)
            {
                if (!Counter.ContainsKey(weaponHash))
                    Counter.Add(weaponHash, incrementBy);
                else Counter[weaponHash] += incrementBy;
            }

            internal int GetUnitCount(MyStringHash weaponHash)
            {
                int value;
                return Counter.TryGetValue(weaponHash, out value) ? value : 0;
            }

            internal void UpdateLeafs()
            {
                foreach (var sub in RootAi.SubGrids)
                {
                    if (RootAi.TopEntity == sub)
                        continue;

                    Ai ai;
                    if (RootAi.Session.GridTargetingAIs.TryGetValue(sub, out ai))
                    {
                        ai.Construct.Data.Repo.Sync(ai.Construct, RootAi.Construct.Data.Repo, true);
                    }
                }
            }

            internal void UpdateLeafFoci()
            {
                foreach (var sub in RootAi.SubGrids)
                {
                    if (RootAi.TopEntity == sub)
                        continue;

                    Ai ai;
                    if (RootAi.Session.GridTargetingAIs.TryGetValue(sub, out ai))
                        ai.Construct.Data.Repo.FocusData.Sync(ai, RootAi.Construct.Data.Repo.FocusData);
                }
            }

            internal void CheckEmptyWeapons()
            {
                foreach (var w in OutOfAmmoWeapons)
                {
                    if (RecentItems.Contains(w.ActiveAmmoDef.AmmoDefinitionId))
                        w.CheckInventorySystem = true;
                }
                RecentItems.Clear();
            }

            internal void CheckForMissingAmmo()
            {
                NewInventoryDetected = false;
                foreach (var w in RootAi.Construct.OutOfAmmoWeapons)
                    w.CheckInventorySystem = true;
            }
            
            internal void Init(Ai ai)
            {
                RootAi = ai;
                Data.Init(ai);
            }
            
            internal void Clean()
            {
                Data.Clean();
                OptimalDps = 0;
                BlockCount = 0;
                RootAi = null;
                LargestAi = null;
                Counter.Clear();
                RefreshedAis.Clear();
            }
        }
    }

    public class Focus
    {
        public readonly long[] OldTarget = new long[2];
        public readonly LockModes[] OldLocked = new LockModes[2];

        public int OldActiveId;
        public bool OldHasFocus;
        public float OldDistToNearestFocusSqr;
        
        public bool ChangeDetected(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;
            if (fd.Target[0] != OldTarget[0] || fd.Target[1] != OldTarget[1] || fd.Locked[0] != OldLocked[0] || fd.Locked[1] != OldLocked[1] || fd.ActiveId != OldActiveId || fd.HasFocus != OldHasFocus || Math.Abs(fd.DistToNearestFocusSqr - OldDistToNearestFocusSqr) > 0)  {

                OldTarget[0] = fd.Target[0];
                OldTarget[1] = fd.Target[1];
                OldLocked[0] = fd.Locked[0];
                OldLocked[1] = fd.Locked[1];
                OldActiveId = fd.ActiveId;
                OldHasFocus = fd.HasFocus;
                OldDistToNearestFocusSqr = fd.DistToNearestFocusSqr;

                return true;
            }

            return false;
        }

        internal void ServerAddFocus(MyEntity target, Ai ai)
        {
            var session = ai.Session;
            var fd = ai.Construct.Data.Repo.FocusData;

            fd.Target[fd.ActiveId] = target.EntityId;
            ai.TargetResetTick = session.Tick + 1;
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestAddFocus(MyEntity target, Ai ai)
        {
            if (ai.Session.IsServer)
                ServerAddFocus(target, ai);
            else
                ai.Session.SendFocusTargetUpdate(ai, target.EntityId);
        }

        internal void ServerCycleLock(Ai ai)
        {
            var session = ai.Session;
            var fd = ai.Construct.Data.Repo.FocusData;
            var currentMode = fd.Locked[fd.ActiveId];
            var modeCount = Enum.GetNames(typeof(LockModes)).Length;

            var nextMode = (int)currentMode + 1 < modeCount ? currentMode + 1 : (LockModes)0;
            fd.Locked[fd.ActiveId] = nextMode;
            ai.TargetResetTick = session.Tick + 1;
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestAddLock(Ai ai)
        {
            if (ai.Session.IsServer)
                ServerCycleLock(ai);
            else
                ai.Session.SendFocusLockUpdate(ai);
        }

        internal void ServerNextActive(bool addSecondary, Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            var prevId = fd.ActiveId;
            var newActiveId = prevId;
            if (newActiveId + 1 > fd.Target.Length - 1) newActiveId -= 1;
            else newActiveId += 1;

            if (addSecondary && fd.Target[newActiveId] <= 0)
            {
                fd.Target[newActiveId] = fd.Target[prevId];
                fd.ActiveId = newActiveId;
            }
            else if (!addSecondary && fd.Target[newActiveId] > 0)
                fd.ActiveId = newActiveId;

            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));

        }

        internal void RequestNextActive(bool addSecondary, Ai ai)
        {
            if (ai.Session.IsServer)

                ServerNextActive(addSecondary, ai);
            else
                ai.Session.SendNextActiveUpdate(ai, addSecondary);
        }

        internal void ServerReleaseActive(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            fd.Target[fd.ActiveId] = -1;
            fd.Locked[fd.ActiveId] = LockModes.None;

            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestReleaseActive(Ai ai)
        {
            if (ai.Session.IsServer)
                ServerReleaseActive(ai);
            else
                ai.Session.SendReleaseActiveUpdate(ai);

        }

        internal bool ServerIsFocused(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            fd.HasFocus = false;
            for (int i = 0; i < fd.Target.Length; i++)
            {

                if (fd.Target[i] > 0)
                {

                    if (MyEntities.GetEntityById(fd.Target[fd.ActiveId]) != null)
                        fd.HasFocus = true;
                    else
                    {
                        fd.Target[i] = -1;
                        fd.Locked[i] = LockModes.None;
                    }
                }

                if (fd.Target[0] <= 0 && fd.HasFocus)
                {

                    fd.Target[0] = fd.Target[i];
                    fd.Locked[0] = fd.Locked[i];
                    fd.Target[i] = -1;
                    fd.Locked[i] = LockModes.None;
                    fd.ActiveId = 0;
                }
            }

            return fd.HasFocus;
        }

        internal bool ClientIsFocused(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (ai.Session.IsServer)
                return ServerIsFocused(ai);

            bool focus = false;
            for (int i = 0; i < fd.Target.Length; i++)
            {

                if (fd.Target[i] > 0)
                    if (MyEntities.GetEntityById(fd.Target[fd.ActiveId]) != null)
                        focus = true;
            }

            return focus;
        }

        internal bool GetPriorityTarget(Ai ai, out MyEntity target, out int focusId)
        {

            var fd = ai.Construct.Data.Repo.FocusData;

            if (fd.Target[fd.ActiveId] > 0 && MyEntities.TryGetEntityById(fd.Target[fd.ActiveId], out target, true))
            {
                focusId = fd.ActiveId;
                return true;
            }

            for (int i = 0; i < fd.Target.Length; i++)
                if (MyEntities.TryGetEntityById(fd.Target[i], out target, true))
                {
                    focusId = i;
                    return true;
                }

            focusId = -1;
            target = null;
            return false;
        }

        internal void ReassignTarget(MyEntity target, int focusId, Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (focusId >= fd.Target.Length || target == null || target.MarkedForClose) return;
            fd.Target[focusId] = target.EntityId;
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal bool FocusInRange(Unit w)
        {
            var fd = w.Comp.Ai.Construct.Data.Repo.FocusData;

            fd.DistToNearestFocusSqr = float.MaxValue;
            for (int i = 0; i < fd.Target.Length; i++)
            {
                if (fd.Target[i] <= 0)
                    continue;

                MyEntity target;
                if (MyEntities.TryGetEntityById(fd.Target[i], out target))
                {
                    var sphere = target.PositionComp.WorldVolume;
                    var distSqr = (float)MyUtils.GetSmallestDistanceToSphere(ref w.MyPivotPos, ref sphere);
                    distSqr *= distSqr;
                    if (distSqr < fd.DistToNearestFocusSqr)
                        fd.DistToNearestFocusSqr = distSqr;
                }

            }
            return fd.DistToNearestFocusSqr <= w.MaxTargetDistanceSqr;
        }

        internal bool EntityIsFocused(Ai ai, MyEntity entToCheck)
        {
            var targets = ai.Construct?.Data?.Repo?.FocusData?.Target;

            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var tId = targets[i];
                    if (tId == 0)
                        continue;

                    MyEntity target;
                    if (MyEntities.TryGetEntityById(tId, out target) && target == entToCheck)
                        return true;
                }
            }
            return false;
        }
    }
}