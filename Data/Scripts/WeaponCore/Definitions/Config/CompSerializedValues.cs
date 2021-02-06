﻿using System;
using System.ComponentModel;
using ProtoBuf;
using VRage;
using VRageMath;
using WeaponCore.Platform;
using WeaponCore.Support;
using static WeaponCore.Support.PartDefinition.TargetingDef;
using static WeaponCore.Support.CoreComponent;
using static WeaponCore.WeaponStateValues;

namespace WeaponCore
{
    [ProtoContract]
    public class Repo
    {
        [ProtoMember(1)] public int Version = Session.VersionControl;
        [ProtoMember(2)] public AmmoValues[] Ammos;
        [ProtoMember(3)] public CompBaseValues Base;


        public void ResetToFreshLoadState()
        {
            //Base.Set.Overrides.Control = GroupOverrides.ControlModes.Auto;
            //Base.State.Control = CompStateValues.ControlMode.None;
            //Base.State.PlayerId = -1;
            Base.State.TrackingReticle = false;
            if (Base.State.TerminalAction == TriggerActions.TriggerOnce) 
                Base.State.TerminalAction = TriggerActions.TriggerOff;
            for (int i = 0; i < Ammos.Length; i++)
            {
                var ws = Base.State.Weapons[i];
                var wr = Base.Reloads[i];
                var wa = Ammos[i];

                wa.AmmoCycleId = 0;
                ws.Heat = 0;
                ws.Overheated = false;
                if (ws.Action == TriggerActions.TriggerOnce)
                    ws.Action = TriggerActions.TriggerOff;
                wr.StartId = 0;
            }
            ResetCompBaseRevisions();
        }

        public void ResetCompBaseRevisions()
        {
            Base.Revision = 0;
            Base.State.Revision = 0;
            for (int i = 0; i < Ammos.Length; i++)
            {
                Base.Targets[i].Revision = 0;
                Base.Reloads[i].Revision = 0;
                Ammos[i].Revision = 0;
            }
        }
    }


    [ProtoContract]
    public class AmmoValues
    {
        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public int CurrentAmmo; //save
        [ProtoMember(3)] public float CurrentCharge; //save
        [ProtoMember(4)] public long CurrentMags; // save
        [ProtoMember(5)] public int AmmoTypeId; //save
        [ProtoMember(6)] public int AmmoCycleId; //save

        public void Sync(Weapon w, AmmoValues sync)
        {
            if (sync.Revision > Revision)
            {
                Revision = sync.Revision;
                CurrentAmmo = sync.CurrentAmmo;
                CurrentCharge = sync.CurrentCharge;

                if (sync.CurrentMags <= 0 && CurrentMags != sync.CurrentMags)
                    w.ClientReload(true);

                CurrentMags = sync.CurrentMags;
                AmmoTypeId = sync.AmmoTypeId;

                if (sync.AmmoCycleId > AmmoCycleId)
                    w.ChangeActiveAmmoClient();

                AmmoCycleId = sync.AmmoCycleId;
            }
        }
    }

    [ProtoContract]
    public class CompBaseValues
    {
        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public CompSettingsValues Set;
        [ProtoMember(3)] public CompStateValues State;
        [ProtoMember(4)] public TransferTarget[] Targets;
        [ProtoMember(5)] public WeaponReloadValues[] Reloads;

        public void Sync(CoreComponent comp, CompBaseValues sync)
        {
            if (sync.Revision > Revision) {

                Revision = sync.Revision;
                Set.Sync(comp, sync.Set);
                State.Sync(comp, sync.State, CompStateValues.Caller.CompData);

                for (int i = 0; i < Targets.Length; i++) {
                    var w = comp.Platform.Weapons[i];
                    sync.Targets[i].SyncTarget(w);
                    Reloads[i].Sync(w, sync.Reloads[i]);
                }
            }
            else Log.Line($"CompDynamicValues older revision");

        }

        public void UpdateCompBasePacketInfo(CoreComponent comp, bool clean = false)
        {
            ++Revision;
            ++State.Revision;
            Session.PacketInfo info;
            if (clean && comp.Session.PrunedPacketsToClient.TryGetValue(comp.Data.Repo.Base.State, out info)) {
                comp.Session.PrunedPacketsToClient.Remove(comp.Data.Repo.Base.State);
                comp.Session.PacketStatePool.Return((CompStatePacket)info.Packet);
            }

            for (int i = 0; i < Targets.Length; i++) {

                var t = Targets[i];
                var wr = Reloads[i];
                
                if (clean) {
                    if (comp.Session.PrunedPacketsToClient.TryGetValue(t, out info)) {
                        comp.Session.PrunedPacketsToClient.Remove(t);
                        comp.Session.PacketTargetPool.Return((TargetPacket)info.Packet);
                    }
                    if (comp.Session.PrunedPacketsToClient.TryGetValue(wr, out info)) {
                        comp.Session.PrunedPacketsToClient.Remove(wr);
                        comp.Session.PacketReloadPool.Return((WeaponReloadPacket)info.Packet);
                    }
                }
                ++wr.Revision;
                ++t.Revision;
                t.WeaponRandom.ReInitRandom();
            }
        }
    }

    [ProtoContract]
    public class WeaponReloadValues
    {
        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public int StartId; //save
        [ProtoMember(3)] public int EndId; //save

        public void Sync(Weapon w, WeaponReloadValues sync)
        {
            if (sync.Revision > Revision)
            {
                Revision = sync.Revision;
                StartId = sync.StartId;
                EndId = sync.EndId;

                w.ClientReload(true);
            }
        }
    }

    [ProtoContract]
    public class CompSettingsValues
    {
        [ProtoMember(1), DefaultValue(true)] public bool Guidance = true;
        [ProtoMember(2), DefaultValue(1)] public int Overload = 1;
        [ProtoMember(3), DefaultValue(1)] public float DpsModifier = 1;
        [ProtoMember(4), DefaultValue(1)] public float RofModifier = 1;
        [ProtoMember(5), DefaultValue(100)] public float Range = 100;
        [ProtoMember(6)] public GroupOverrides Overrides;


        public CompSettingsValues()
        {
            Overrides = new GroupOverrides();
        }

        public void  Sync(CoreComponent comp, CompSettingsValues sync)
        {
            Guidance = sync.Guidance;
            Range = sync.Range;
            SetRange(comp);

            Overrides.Sync(sync.Overrides);

            var rofChange = Math.Abs(RofModifier - sync.RofModifier) > 0.0001f;
            var dpsChange = Math.Abs(DpsModifier - sync.DpsModifier) > 0.0001f;

            if (Overload != sync.Overload || rofChange || dpsChange) {
                Overload = sync.Overload;
                RofModifier = sync.RofModifier;
                DpsModifier = sync.DpsModifier;
                if (rofChange) SetRof(comp);
            }
        }

    }

    [ProtoContract]
    public class CompStateValues
    {
        public enum Caller
        {
            Direct,
            CompData,
        }

        public enum ControlMode
        {
            None,
            Ui,
            Toolbar,
            Camera
        }

        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public WeaponStateValues[] Weapons;
        [ProtoMember(3)] public bool TrackingReticle; //don't save
        [ProtoMember(4), DefaultValue(-1)] public long PlayerId = -1;
        [ProtoMember(5), DefaultValue(ControlMode.None)] public ControlMode Control = ControlMode.None;
        [ProtoMember(6)] public TriggerActions TerminalAction;

        public void Sync(CoreComponent comp, CompStateValues sync, Caller caller)
        {
            if (sync.Revision > Revision)
            {
                Revision = sync.Revision;
                TrackingReticle = sync.TrackingReticle;
                PlayerId = sync.PlayerId;
                Control = sync.Control;
                TerminalAction = sync.TerminalAction;
                for (int i = 0; i < sync.Weapons.Length; i++)
                {
                    var w = comp.Platform.Weapons[i];
                    w.State.Sync(sync.Weapons[i]);
                }
            }
            //else Log.Line($"CompStateValues older revision: {sync.Revision} > {Revision} - caller:{caller}");
        }

        public void TerminalActionSetter(CoreComponent comp, TriggerActions action, bool syncWeapons = false, bool updateWeapons = true)
        {
            TerminalAction = action;
            
            if (updateWeapons) {
                for (int i = 0; i < Weapons.Length; i++)
                    Weapons[i].Action = action;
            }

            if (syncWeapons)
                comp.Session.SendCompState(comp);
        }

    }

    [ProtoContract]
    public class WeaponStateValues
    {
        [ProtoMember(1)] public float Heat; // don't save
        [ProtoMember(2)] public bool Overheated; //don't save
        [ProtoMember(3), DefaultValue(TriggerActions.TriggerOff)] public TriggerActions Action = TriggerActions.TriggerOff; // save

        public void Sync(WeaponStateValues sync)
        {
            Heat = sync.Heat;
            Overheated = sync.Overheated;
            Action = sync.Action;
        }

        public void WeaponMode(CoreComponent comp, TriggerActions action, bool resetTerminalAction = true, bool syncCompState = true)
        {
            if (resetTerminalAction)
                comp.Data.Repo.Base.State.TerminalAction = TriggerActions.TriggerOff;

            Action = action;
            if (comp.Session.MpActive && comp.Session.IsServer && syncCompState)
                comp.Session.SendCompState(comp);
        }

    }

    [ProtoContract]
    public class TransferTarget
    {
        [ProtoMember(1)] public uint Revision;
        [ProtoMember(2)] public long EntityId;
        [ProtoMember(3)] public Vector3 TargetPos;
        [ProtoMember(4)] public int PartId;
        [ProtoMember(5)] public WeaponRandomGenerator WeaponRandom; // save

        internal void SyncTarget(Weapon w)
        {
            if (Revision > w.TargetData.Revision)
            {
                w.TargetData.Revision = Revision;
                w.TargetData.EntityId = EntityId;
                w.TargetData.TargetPos = TargetPos;
                w.PartId = PartId;
                w.TargetData.WeaponRandom.Sync(WeaponRandom);

                var target = w.Target;
                target.IsProjectile = EntityId == -1;
                target.IsFakeTarget = EntityId == -2;
                target.TargetPos = TargetPos;
                target.ClientDirty = true;
            }
            //else Log.Line($"TransferTarget older revision:  {Revision}  > {w.TargetData.Revision}");
        }

        public void WeaponInit(Weapon w)
        {
            WeaponRandom.Init(w.UniqueId);

            var rand = WeaponRandom;
            rand.CurrentSeed = w.UniqueId;
            rand.ClientProjectileRandom = new Random(rand.CurrentSeed);

            rand.TurretRandom = new Random(rand.CurrentSeed);
            rand.AcquireRandom = new Random(rand.CurrentSeed);
        }

        public void PartRefreshClient(Weapon w)
        {
            try
            {
                var rand = WeaponRandom;

                rand.ClientProjectileRandom = new Random(rand.CurrentSeed);
                rand.TurretRandom = new Random(rand.CurrentSeed);
                rand.AcquireRandom = new Random(rand.CurrentSeed);

                for (int j = 0; j < rand.TurretCurrentCounter; j++)
                    rand.TurretRandom.Next();

                for (int j = 0; j < rand.ClientProjectileCurrentCounter; j++)
                    rand.ClientProjectileRandom.Next();

                for (int j = 0; j < rand.AcquireCurrentCounter; j++)
                    rand.AcquireRandom.Next();

                return;
            }
            catch (Exception e) { Log.Line($"Client Weapon Values Failed To load re-initing... how?"); }

            WeaponInit(w);
        }
        internal void ClearTarget()
        {
            ++Revision;
            EntityId = 0;
            TargetPos = Vector3.Zero;
        }

        public TransferTarget() { }
    }

    [ProtoContract]
    public class GroupOverrides
    {
        public enum MoveModes
        {
            Any,
            Moving,
            Mobile,
            Moored,
        }

        public enum ControlModes
        {
            Auto,
            Manual,
            Painter,
        }

        [ProtoMember(1)] public bool Neutrals;
        [ProtoMember(2)] public bool Unowned;
        [ProtoMember(3)] public bool Friendly;
        [ProtoMember(4)] public bool FocusTargets;
        [ProtoMember(5)] public bool FocusSubSystem;
        [ProtoMember(6)] public int MinSize;
        [ProtoMember(7), DefaultValue(ControlModes.Auto)] public ControlModes Control = ControlModes.Auto;
        [ProtoMember(8), DefaultValue(BlockTypes.Any)] public BlockTypes SubSystem = BlockTypes.Any;
        [ProtoMember(9), DefaultValue(true)] public bool Meteors = true;
        [ProtoMember(10), DefaultValue(true)] public bool Biologicals = true;
        [ProtoMember(11), DefaultValue(true)] public bool Projectiles = true;
        [ProtoMember(12), DefaultValue(16384)] public int MaxSize = 16384;
        [ProtoMember(13), DefaultValue(MoveModes.Any)] public MoveModes MoveMode = MoveModes.Any;
        [ProtoMember(14), DefaultValue(true)] public bool Grids = true;
        [ProtoMember(15), DefaultValue(true)] public bool ArmorShowArea;

        public GroupOverrides() { }

        public void Sync(GroupOverrides syncFrom)
        {
            MoveMode = syncFrom.MoveMode;
            MaxSize = syncFrom.MaxSize;
            MinSize = syncFrom.MinSize;
            Neutrals = syncFrom.Neutrals;
            Unowned = syncFrom.Unowned;
            Friendly = syncFrom.Friendly;
            Control = syncFrom.Control;
            FocusTargets = syncFrom.FocusTargets;
            FocusSubSystem = syncFrom.FocusSubSystem;
            SubSystem = syncFrom.SubSystem;
            Meteors = syncFrom.Meteors;
            Grids = syncFrom.Grids;
            ArmorShowArea = syncFrom.ArmorShowArea;
            Biologicals = syncFrom.Biologicals;
            Projectiles = syncFrom.Projectiles;
        }
    }
}
