﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.Target;
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

        //private int _shotsInCycle;
        private int _nextVirtual;
        private int _barrelRate;
        private int _fakeHeatTick;
        private uint _ticksUntilShoot;
        private uint _shootTick;
        private uint _posChangedTick = 1;
        private uint _prefiredTick;
        internal bool PreFired;
        internal uint TicksPerShot;
        internal double TimePerShot;
        internal int LoadId;

        //private bool _newCycle;
        //private bool _firstRun = true;
        internal MyTuple<MyEntity, Matrix, Matrix, Vector3> MuzzlePart;
        internal MyTuple<MyEntity, Matrix, Matrix, Matrix, Matrix, Vector3> AzimuthPart;
        internal MyTuple<MyEntity, Matrix, Matrix, Matrix, Matrix, Vector3> ElevationPart;
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

        internal Target Target;
        internal Target NewTarget;
        internal Vector3D TargetPos;
        internal MathFuncs.Cone AimCone = new MathFuncs.Cone();
        internal Matrix[] BarrelRotationPerShot = new Matrix[10];
        internal MyParticleEffect[] BarrelEffects1;
        internal MyParticleEffect[] BarrelEffects2;
        internal MyParticleEffect[] HitEffects;
        internal MySoundPair ReloadSound;
        internal MySoundPair FiringSound;
        internal MySoundPair RotateSound;
        internal readonly MyEntity3DSoundEmitter ReloadEmitter;
        internal readonly MyEntity3DSoundEmitter FiringEmitter;
        internal readonly MyEntity3DSoundEmitter RotateEmitter;
        internal readonly CachingDictionary<Muzzle, uint> BarrelAvUpdater = new CachingDictionary<Muzzle, uint>();
        internal readonly Dictionary<EventTriggers, PartAnimation[]> AnimationsSet;
        internal readonly Dictionary<string, PartAnimation> AnimationLookup = new Dictionary<string, PartAnimation>();
        internal readonly bool TrackProjectiles;

        internal Targets TargetState = Targets.Expired;
        internal EventTriggers LastEvent;
        internal PartAnimation CurLgstAnimPlaying;
        internal float RequiredPower;
        internal float UseablePower;
        internal float OldUseablePower;
        internal float BaseDamage;
        internal float ShotEnergyCost;
        internal float Dps;
        internal float AreaEffectDmg;
        internal float DetonateDmg;
        internal float LastHeat;
        internal float CurrentCharge;
        internal uint ShotCounter;
        internal uint LastTargetTick;
        internal uint LastTrackedTick;
        internal uint ChargeDelayTicks;
        internal uint ChargeUntilTick;
        internal uint AnimationDelayTick;
        internal uint OffDelay;
        internal uint ShootDelayTick;
        internal int RateOfFire;
        internal int BarrelSpinRate;
        internal int DelayFireCount;
        internal int WeaponId;
        internal int HsRate;
        internal int EnergyPriority;
        internal int LastBlockCount;
        internal float HeatPShot;
        internal float CurrentAmmoVolume;
        internal double Azimuth;
        internal double Elevation;
        internal double AimingTolerance;
        internal double RotationSpeed;
        internal double ElevationSpeed;
        internal double MaxAzimuthRadians;
        internal double MinAzimuthRadians;
        internal double MaxElevationRadians;
        internal double MinElevationRadians;
        internal bool IsTurret;
        internal bool TurretMode;
        internal bool TrackTarget;
        internal bool AiReady;
        internal bool SeekTarget;
        internal bool TrackingAi;
        internal bool IsTracking;
        internal bool IsAligned;
        internal bool IsShooting;
        internal bool PlayTurretAv;
        internal bool AvCapable;
        internal bool DelayCeaseFire;
        internal bool Scheduled;
        internal bool Reloading;
        internal bool OutOfAmmo;
        internal bool ReturnHome;
        internal bool CurrentlyDegrading;
        internal bool HitOther;
        internal bool FixedOffset;
        internal bool AiOnlyWeapon;
        internal bool Charging;
        internal bool DrawingPower;
        internal bool RequestedPower;
        internal bool ResetPower;
        internal bool RecalcPower;

        public enum TerminalActionState
        {
            ShootOn,
            ShootOff,
            ShootOnce,
            ShootClick,
        }

        public enum EventTriggers
        {
            Reloading,
            Firing,
            Tracking,
            Overheated,
            TurnOn,
            TurnOff,
            BurstReload,
            OutOfAmmo,
            PreFire,
            EmptyOnGameLoad,
            StopFiring,
            StopTracking
        }

        public Weapon(MyEntity entity, WeaponSystem system, int weaponId, WeaponComponent comp, Dictionary<EventTriggers, PartAnimation[]> animationSets)
        {
            LoadId = comp.Session.LoadAssigner();
            MuzzlePart = new MyTuple<MyEntity, Matrix, Matrix, Vector3> {Item1 = entity };
            AnimationsSet = animationSets;

            if (AnimationsSet != null)
            {
                foreach (var set in AnimationsSet)
                {
                    for (int j = 0; j < set.Value.Length; j++)
                    {
                        var animation = set.Value[j];
                        AnimationLookup.Add(animation.AnimationId, animation);
                        animation.Weapon = this;
                    }
                }
            }
            

            System = system;
            Comp = comp;
            comp.HasEnergyWeapon = comp.HasEnergyWeapon || System.EnergyAmmo || System.IsHybrid;

            AvCapable = System.HasBarrelShootAv && !Comp.Session.DedicatedServer;

            if (AvCapable && system.FiringSound == WeaponSystem.FiringSoundState.WhenDone)
            {
                FiringEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                FiringSound = new MySoundPair();
                FiringSound.Init(System.Values.Audio.HardPoint.FiringSound);
            }

            if (AvCapable && system.WeaponReloadSound)
            {
                ReloadEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                ReloadSound = new MySoundPair();
                ReloadSound.Init(System.Values.Audio.HardPoint.ReloadSound);
            }

            if (AvCapable && system.BarrelRotationSound && system.Values.HardPoint.RotateBarrelAxis != 0)
            {
                RotateEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                RotateSound = new MySoundPair();
                RotateSound.Init(System.Values.Audio.HardPoint.BarrelRotationSound);
            }

            if (AvCapable)
            {
                if (System.BarrelEffect1) BarrelEffects1 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (System.BarrelEffect2) BarrelEffects2 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (System.HitParticle && System.IsBeamWeapon) HitEffects = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
            }

            WeaponId = weaponId;
            IsTurret = System.Values.HardPoint.Block.TurretAttached;
            TurretMode = System.Values.HardPoint.Block.TurretController;
            TrackTarget = System.Values.HardPoint.Block.TrackTargets;
            HsRate = System.Values.HardPoint.Loading.HeatSinkRate;
            EnergyPriority = System.Values.HardPoint.EnergyPriority;
            var toleranceInRadians = MathHelperD.ToRadians(System.Values.HardPoint.AimingTolerance);
            AimCone.ConeAngle = toleranceInRadians;
            AimingTolerance = Math.Cos(toleranceInRadians);

            _numOfBarrels = System.Barrels.Length;
            DelayCeaseFire = System.TimeToCeaseFire > 0;
            BeamSlot = new uint[_numOfBarrels];
            Target = new Target(comp.MyCube);
            NewTarget = new Target(comp.MyCube);
            WeaponCache = new WeaponFrameCache(System.Values.Assignments.Barrels.Length);
            TrackProjectiles = System.TrackProjectile;
        }
    }
}
