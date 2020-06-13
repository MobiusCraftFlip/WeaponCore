﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.WeaponDefinition.AnimationDef.PartAnimationSetDef;
using static WeaponCore.Support.WeaponDefinition.HardPointDef.HardwareDef;

namespace WeaponCore.Platform
{
    public partial class Weapon
    {
        internal int NextMuzzle;
        internal volatile bool Casting;
        
        private readonly int _numOfBarrels;
        private readonly HashSet<string> _muzzlesToFire = new HashSet<string>();
        private readonly HashSet<string> _muzzlesFiring = new HashSet<string>();
        internal readonly Dictionary<int, string> MuzzleIdToName = new Dictionary<int, string>();
        internal Dictionary<EventTriggers, ParticleEvent[]> ParticleEvents;
        internal Action<object> CancelableReloadAction = (o) => {};
        private readonly int _numModelBarrels;
        private int _nextVirtual;
        private uint _ticksUntilShoot;
        private uint _spinUpTick;
        private uint _ticksBeforeSpinUp;
        internal bool HeatLoopRunning;
        internal bool PreFired;
        internal bool FinishBurst;
        internal bool FirstSync = true;
        internal bool LockOnFireState;
        internal bool SendTarget;
        internal bool SendSync;
        internal bool ReloadSubscribed;
        internal bool CanHoldMultMags;
        internal uint GravityTick;
        internal uint ShootTick;
        internal uint TicksPerShot;
        internal uint LastSyncTick;
        internal uint PosChangedTick = 1;
        internal uint ElevationTick;
        internal uint AzimuthTick;

        internal double TimePerShot;
        internal float HeatPerc;

        //internal int LoadId;
        internal int ShortLoadId;
        internal int BarrelRate;
        internal int ArmorHits;

        internal PartInfo MuzzlePart;
        internal PartInfo AzimuthPart;
        internal PartInfo ElevationPart;
        internal List<MyEntity> HeatingParts;
        internal Vector3D GravityPoint;
        internal Vector3D MyPivotPos;
        internal Vector3D MyPivotDir;
        internal Vector3D MyPivotUp;
        internal Vector3D AimOffset;
        internal MatrixD WeaponConstMatrix;
        internal LineD MyCenterTestLine;
        internal LineD MyBarrelTestLine;
        internal LineD MyPivotTestLine;
        internal LineD MyAimTestLine;
        internal LineD MyShootAlignmentLine;
        internal WeaponSystem System;
        internal Dummy[] Dummies;
        internal Muzzle[] Muzzles;
        internal uint[] BeamSlot;
        internal WeaponComponent Comp;

        internal WeaponFrameCache WeaponCache;

        internal MyOrientedBoundingBoxD TargetBox;
        internal LineD LimitLine;

        internal WeaponAcquire Acquire;
        internal Target Target;
        internal Target NewTarget;
        internal MathFuncs.Cone AimCone = new MathFuncs.Cone();
        internal Matrix[] BarrelRotationPerShot = new Matrix[10];
        internal MyParticleEffect[] BarrelEffects1;
        internal MyParticleEffect[] BarrelEffects2;
        internal MyParticleEffect[] HitEffects;
        internal MySoundPair ReloadSound;
        internal MySoundPair PreFiringSound;
        internal MySoundPair FiringSound;
        internal MySoundPair RotateSound;
        internal WeaponSettingsValues Set;
        internal WeaponStateValues State;
        internal WeaponTimings Timings;
        internal WeaponSystem.WeaponAmmoTypes ActiveAmmoDef;
        internal ParallelRayCallBack RayCallBack;

        internal readonly MyEntity3DSoundEmitter ReloadEmitter;
        internal readonly MyEntity3DSoundEmitter PreFiringEmitter;
        internal readonly MyEntity3DSoundEmitter FiringEmitter;
        internal readonly MyEntity3DSoundEmitter RotateEmitter;
        internal readonly Dictionary<EventTriggers, PartAnimation[]> AnimationsSet;
        internal readonly Dictionary<string, PartAnimation> AnimationLookup = new Dictionary<string, PartAnimation>();
        internal readonly bool TrackProjectiles;
        internal readonly bool PrimaryWeaponGroup;

        internal EventTriggers LastEvent;
        internal float RequiredPower;
        internal float MaxCharge;
        internal float UseablePower;
        internal float OldUseablePower;
        internal float BaseDamage;
        internal float Dps;
        internal float ShotEnergyCost;
        internal float LastHeat;
        internal uint CeaseFireDelayTick = uint.MaxValue / 2;
        internal uint LastTargetTick;
        internal uint LastTrackedTick;
        internal uint LastMuzzleCheck;
        internal uint LastSmartLosCheck;
        internal uint LastLoadedTick;
        internal int FireCounter;
        internal int UniqueId;
        internal int RateOfFire;
        internal int BarrelSpinRate;
        internal int WeaponId;
        internal int EnergyPriority;
        internal int LastBlockCount;
        internal float HeatPShot;
        internal float HsRate;
        internal float CurrentAmmoVolume;
        internal double Azimuth;
        internal double Elevation;
        internal double AimingTolerance;
        internal double RotationSpeed;
        internal double ElevationSpeed;
        internal double MaxAzToleranceRadians;
        internal double MinAzToleranceRadians;
        internal double MaxElToleranceRadians;
        internal double MinElToleranceRadians;
        internal double MaxAzimuthRadians;
        internal double MinAzimuthRadians;
        internal double MaxElevationRadians;
        internal double MinElevationRadians;
        internal double MaxTargetDistance;
        internal double MaxTargetDistanceSqr;
        internal double MinTargetDistance;
        internal double MinTargetDistanceSqr;

        internal bool IsTurret;
        internal bool TurretMode;
        internal bool TrackTarget;
        internal bool AiShooting;
        internal bool SeekTarget;
        internal bool AiEnabled;
        internal bool IsShooting;
        internal bool PlayTurretAv;
        internal bool AvCapable;
        internal bool OutOfAmmo;
        internal bool CurrentlyDegrading;
        internal bool FixedOffset;
        internal bool AiOnlyWeapon;
        internal bool DrawingPower;
        internal bool RequestedPower;
        internal bool ResetPower;
        internal bool RecalcPower;
        internal bool ProjectilesNear;
        internal bool StopBarrelAv;
        internal bool AcquiringTarget;
        internal bool BarrelSpinning;
        internal bool AzimuthOnBase;
        internal bool ReturingHome;
        internal bool IsHome = true;
        internal bool CanUseEnergyAmmo;
        internal bool CanUseHybridAmmo;
        internal bool CanUseChargeAmmo;
        internal bool CanUseBeams;
        internal bool PauseShoot;
        internal bool LastEventCanDelay;

        public enum ManualShootActionState
        {
            ShootOn,
            ShootOff,
            ShootOnce,
            ShootClick,
        }


        internal bool ShotReady
        {
            get
            {
                var reloading = (!ActiveAmmoDef.AmmoDef.Const.EnergyAmmo || ActiveAmmoDef.AmmoDef.Const.MustCharge) && (State.Sync.Reloading || OutOfAmmo);
                var canShoot = !State.Sync.Overheated && !reloading && !System.DesignatorWeapon;
                var shotReady = canShoot && !State.Sync.Charging && (ShootTick <= Comp.Session.Tick) && (Timings.AnimationDelayTick <= Comp.Session.Tick || !LastEventCanDelay);
                return shotReady;
            }
        }

        internal bool CanReload
        {
            get
            {
                try
                {
                    if (!Comp.State.Value.Online || State.Sync.Reloading || !ActiveAmmoDef.AmmoDef.Const.Reloadable || System.DesignatorWeapon || (Timings.AnimationDelayTick > Comp.Session.Tick && (LastEventCanDelay || LastEvent == EventTriggers.Firing)) || State.Sync.CurrentAmmo > 0)
                        return false;

                    if (Comp.Session.IsCreative) return true;

                    return !CheckOutOfAmmo();
                }
                catch (Exception ex) { Log.Line($"Exception in CanReload: {ex} - CompStateNull:{Comp.State == null} - StateNull{State?.Sync == null} - AmmoDefNull:{ActiveAmmoDef?.AmmoDef == null} TimingsNull{Timings == null} - AiNull:{Comp?.Ai == null} - SessionNull:{Comp?.Session == null} - targetNull:{Target == null}"); }

                return false;
            }
        }

        internal Weapon(MyEntity entity, WeaponSystem system, int weaponId, WeaponComponent comp, Dictionary<EventTriggers, PartAnimation[]> animationSets)
        {

            MuzzlePart = new PartInfo { Entity = entity };
            AnimationsSet = animationSets;
            Timings = new WeaponTimings();
            if (AnimationsSet != null)
            {
                foreach (var set in AnimationsSet)
                {
                    for (int j = 0; j < set.Value.Length; j++)
                    {
                        var animation = set.Value[j];
                        AnimationLookup.Add(animation.AnimationId, animation);
                    }
                }
            }
            
            System = system;
            Comp = comp;

            MyStringHash subtype;
            if (comp.MyCube.DefinitionId.HasValue && comp.Session.VanillaIds.TryGetValue(comp.MyCube.DefinitionId.Value, out subtype))
            {
                if (subtype.String.Contains("Gatling"))
                    _numModelBarrels = 6;
                else
                    _numModelBarrels = System.Barrels.Length;
            }
            else
                _numModelBarrels = System.Barrels.Length;


            bool hitParticle = false;
            foreach (var ammoType in System.AmmoTypes)
            {
                var c = ammoType.AmmoDef.Const;
                if (c.EnergyAmmo) CanUseEnergyAmmo = true;
                if (c.IsHybrid) CanUseHybridAmmo = true;
                if (c.MustCharge) CanUseChargeAmmo = true;
                if (c.IsBeamWeapon) CanUseBeams = true;
                if (c.HitParticle) hitParticle = true;
            }

            comp.HasEnergyWeapon = comp.HasEnergyWeapon || CanUseEnergyAmmo || CanUseHybridAmmo;

            AvCapable = System.HasBarrelShootAv && !Comp.Session.DedicatedServer || hitParticle;

            if (AvCapable && system.FiringSound == WeaponSystem.FiringSoundState.WhenDone)
            {
                FiringEmitter = System.Session.Emitters.Count > 0 ? System.Session.Emitters.Pop() : new MyEntity3DSoundEmitter(null, true, 1f);
                FiringEmitter.CanPlayLoopSounds = true;
                FiringEmitter.Entity = Comp.MyCube;
                FiringSound = System.Session.SoundPairs.Count > 0 ? System.Session.SoundPairs.Pop() : new MySoundPair();
                FiringSound.Init(System.Values.HardPoint.Audio.FiringSound);
            }

            if (AvCapable && system.PreFireSound)
            {
                PreFiringEmitter = System.Session.Emitters.Count > 0 ? System.Session.Emitters.Pop() : new MyEntity3DSoundEmitter(null, true, 1f);
                PreFiringEmitter.CanPlayLoopSounds = true;

                PreFiringEmitter.Entity = Comp.MyCube;
                PreFiringSound = System.Session.SoundPairs.Count > 0 ? System.Session.SoundPairs.Pop() : new MySoundPair();
                PreFiringSound.Init(System.Values.HardPoint.Audio.PreFiringSound);
            }

            if (AvCapable && system.WeaponReloadSound)
            {
                ReloadEmitter = System.Session.Emitters.Count > 0 ? System.Session.Emitters.Pop() : new MyEntity3DSoundEmitter(null, true, 1f);
                ReloadEmitter.CanPlayLoopSounds = true;

                ReloadEmitter.Entity = Comp.MyCube;
                ReloadSound = System.Session.SoundPairs.Count > 0 ? System.Session.SoundPairs.Pop() : new MySoundPair();
                ReloadSound.Init(System.Values.HardPoint.Audio.ReloadSound);
            }

            if (AvCapable && system.BarrelRotationSound)
            {
                RotateEmitter = System.Session.Emitters.Count > 0 ? System.Session.Emitters.Pop() : new MyEntity3DSoundEmitter(null, true, 1f);
                RotateEmitter.CanPlayLoopSounds = true;

                RotateEmitter.Entity = Comp.MyCube;
                RotateSound = System.Session.SoundPairs.Count > 0 ? System.Session.SoundPairs.Pop() : new MySoundPair();
                RotateSound.Init(System.Values.HardPoint.Audio.BarrelRotationSound);
            }

            if (AvCapable)
            {
                if (System.BarrelEffect1) BarrelEffects1 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (System.BarrelEffect2) BarrelEffects2 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (hitParticle && CanUseBeams) HitEffects = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
            }

            if (System.Armor != ArmorState.IsWeapon)
                Comp.HasArmor = true;
            
            WeaponId = weaponId;
            PrimaryWeaponGroup = WeaponId % 2 == 0;
            IsTurret = System.Values.HardPoint.Ai.TurretAttached;
            TurretMode = System.Values.HardPoint.Ai.TurretController;
            TrackTarget = System.Values.HardPoint.Ai.TrackTargets;
            
            if (System.Values.HardPoint.Ai.TurretController)
                AiEnabled = true;

            AimOffset = System.Values.HardPoint.HardWare.Offset;
            FixedOffset = System.Values.HardPoint.HardWare.FixedOffset;

            HsRate = System.Values.HardPoint.Loading.HeatSinkRate / 3;
            EnergyPriority = System.Values.HardPoint.Other.EnergyPriority;
            var toleranceInRadians = MathHelperD.ToRadians(System.Values.HardPoint.AimingTolerance);
            AimCone.ConeAngle = toleranceInRadians;
            AimingTolerance = Math.Cos(toleranceInRadians);

            _numOfBarrels = System.Barrels.Length;
            BeamSlot = new uint[_numOfBarrels];
            Target = new Target(this, true);
            NewTarget = new Target(this);
            WeaponCache = new WeaponFrameCache(System.Values.Assignments.Barrels.Length);
            RayCallBack = new ParallelRayCallBack(this);
            Acquire = new WeaponAcquire(this);
            TrackProjectiles = System.TrackProjectile;

            //LoadId = comp.Session.LoadAssigner();
            UniqueId = comp.Session.UniqueWeaponId;
            ShortLoadId = comp.Session.ShortLoadAssigner();
        }
    }
}
