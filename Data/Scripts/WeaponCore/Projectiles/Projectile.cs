﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using WeaponCore.Data.Scripts.WeaponCore.Support;
using WeaponCore.Support;
using static WeaponCore.Support.AreaDamage;
using static WeaponCore.Support.VisualShot;
using static WeaponCore.Support.ProInfo;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace WeaponCore.Projectiles
{
    internal class Projectile
    {
        internal const float StepConst = MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        internal ulong Id;
        internal ProjectileState State;
        internal EntityState ModelState;
        internal MyEntityQueryType PruneQuery;
        internal AmmoTrajectory.GuidanceType Guidance;
        internal Vector3D Direction;
        internal Vector3D AccelDir;
        internal Vector3D VisualDir;
        internal Vector3D Position;
        internal Vector3D LastPosition;
        internal Vector3D StartSpeed;
        internal Vector3D Velocity;
        internal Vector3D AccelVelocity;
        internal Vector3D MaxVelocity;
        internal Vector3D TravelMagnitude;
        internal Vector3D LastEntityPos;
        internal Vector3D OriginTargetPos;
        internal Vector3D PredictedTargetPos;
        internal Vector3D PrevTargetPos;
        internal Vector3D TargetOffSet;
        internal Vector3D PrevTargetOffset;
        internal Vector3 PrevTargetVel;
        internal Vector3D? LastHitPos;
        internal Vector3? LastHitEntVel;
        internal BoundingSphereD TestSphere = new BoundingSphereD(Vector3D.Zero, 200f);
        internal BoundingSphereD ModelSphereCurrent;
        internal BoundingSphereD ModelSphereLast;
        internal BoundingSphereD PruneSphere;
        internal double AccelLength;
        internal double DistanceToTravelSqr;
        internal double TracerLength;
        internal double VelocityLengthSqr;
        internal double DistanceFromCameraSqr;
        internal double OffsetSqr;
        internal double AccelPerSec;
        internal double MaxSpeedSqr;
        internal double MaxSpeed;
        internal double VisualStep;
        internal double DeadZone = 3;
        internal double MaxTrajectorySqr;
        internal float DesiredSpeed;
        internal float MaxTrajectory;
        internal float BaseAmmoParticleScale;
        internal int ChaseAge;
        internal int FieldTime;
        internal int EndStep;
        internal int ZombieLifeTime;
        internal int LastOffsetTime;
        internal int PruningProxyId = -1;
        internal int CachedId;
        internal int MaxChaseAge;
        internal int NewTargets;
        internal bool PickTarget;
        internal bool EnableAv;
        internal bool FirstOffScreen;
        internal bool ConstantSpeed;
        internal bool PositionChecked;
        internal bool MoveToAndActivate;
        internal bool LockedTarget;
        internal bool DynamicGuidance;
        internal bool ParticleStopped;
        internal bool ParticleLateStart;
        internal bool GenerateShrapnel;
        internal bool Colliding;
        internal bool LinePlanetCheck;
        internal bool SmartsOn;
        internal bool EwarActive;
        internal bool MineSeeking;
        internal bool MineActivated;
        internal bool MineTriggered;
        internal bool Miss;
        internal bool Active;
        internal bool HitParticleActive;
        internal bool CachedPlanetHit;
        internal bool ForceHitParticle;
        internal bool Gunner;
        internal readonly ProInfo Info = new ProInfo();
        internal MyParticleEffect AmmoEffect;
        internal MyParticleEffect HitEffect;
        internal readonly List<MyLineSegmentOverlapResult<MyEntity>> SegmentList = new List<MyLineSegmentOverlapResult<MyEntity>>();
        internal readonly List<VirtualProjectile> VrPros = new List<VirtualProjectile>();
        internal readonly List<Projectile> EwaredProjectiles = new List<Projectile>();
        internal readonly List<GridAi> Watchers = new List<GridAi>();
        internal readonly HashSet<Projectile> Seekers = new HashSet<Projectile>();
        internal readonly List<IHitInfo> RayHits = new List<IHitInfo>();

        internal void Start()
        {
            Info.VisualShot = Info.Ai.Session.VisualShotPool.Get();
            Id = Info.Ai.Session.Projectiles.CurrentProjectileId++;
            Position = Info.Origin;
            AccelDir = Direction;
            VisualDir = Direction;
            var cameraStart = Info.Ai.Session.CameraPos;
            Vector3D.DistanceSquared(ref cameraStart, ref Info.Origin, out DistanceFromCameraSqr);
            GenerateShrapnel = Info.System.Values.Ammo.Shrapnel.Fragments > 0;
            var noSAv = Info.IsShrapnel && Info.System.Values.Ammo.Shrapnel.NoAudioVisual;
            var probability = Info.System.Values.Graphics.VisualProbability;
            EnableAv = !Info.Ai.Session.DedicatedServer && !noSAv && DistanceFromCameraSqr <= Info.Ai.Session.SyncDistSqr && (probability >= 1 || probability >= MyUtils.GetRandomDouble(0.0f, 1f));

            Info.PrimeMatrix = MatrixD.Identity;
            Info.TriggerMatrix = MatrixD.Identity;
            ModelState = EntityState.None;
            LastEntityPos = Position;

            LastHitPos = null;
            LastHitEntVel = null;
            Info.Age = 0;
            ChaseAge = 0;
            NewTargets = 0;
            ZombieLifeTime = 0;
            LastOffsetTime = 0;
            PruningProxyId = -1;
            Colliding = false;
            Active = false;
            CachedPlanetHit = false;
            ParticleStopped = false;
            ParticleLateStart = false;
            FirstOffScreen = true;
            PositionChecked = false;
            EwarActive = false;
            MineSeeking = false;
            MineActivated = false;
            MineTriggered = false;
            Info.Cloaked = false;
            HitParticleActive = false;
            LinePlanetCheck = false;
            EndStep = 0;
            Info.PrevDistanceTraveled = 0;
            Info.DistanceTraveled = 0;
            CachedId = Info.MuzzleId == -1 ? Info.WeaponCache.VirutalId : Info.MuzzleId;

            Guidance = !(Info.System.Values.Ammo.Shrapnel.NoGuidance && Info.IsShrapnel) ? Info.System.Values.Ammo.Trajectory.Guidance : AmmoTrajectory.GuidanceType.None;
            DynamicGuidance = Guidance != AmmoTrajectory.GuidanceType.None && Guidance != AmmoTrajectory.GuidanceType.TravelTo && !Info.System.IsBeamWeapon && Info.EnableGuidance;
            if (DynamicGuidance) DynTrees.RegisterProjectile(this);

            if (Guidance == AmmoTrajectory.GuidanceType.Smart && DynamicGuidance)
            {
                SmartsOn = true;
                MaxChaseAge = Info.System.Values.Ammo.Trajectory.Smarts.MaxChaseTime;
            }
            else
            {
                MaxChaseAge = int.MaxValue;
                SmartsOn = false;
            }

            if (Info.Target.IsProjectile)
            {
                OriginTargetPos = Info.Target.Projectile.Position;
                Info.Target.Projectile.Seekers.Add(this);
            }
            else if (Info.Target.Entity != null) OriginTargetPos = Info.Target.Entity.PositionComp.WorldAABB.Center;
            else OriginTargetPos = Vector3D.Zero;
            LockedTarget = OriginTargetPos != Vector3D.Zero;

            if (SmartsOn && Info.System.TargetOffSet && LockedTarget)
            {
                OffSetTarget();
                OffsetSqr = Info.System.Values.Ammo.Trajectory.Smarts.Inaccuracy * Info.System.Values.Ammo.Trajectory.Smarts.Inaccuracy;
            }
            else
            {
                TargetOffSet = Vector3D.Zero;
                OffsetSqr = 0;
            }
            PrevTargetOffset = Vector3D.Zero;

            if (Info.System.RangeVariance)
            {
                var min = Info.System.Values.Ammo.Trajectory.RangeVariance.Start;
                var max = Info.System.Values.Ammo.Trajectory.RangeVariance.End;
                MaxTrajectory = Info.System.Values.Ammo.Trajectory.MaxTrajectory - MyUtils.GetRandomFloat(min, max);
            }
            else MaxTrajectory = Info.System.Values.Ammo.Trajectory.MaxTrajectory;
            if (PredictedTargetPos == Vector3D.Zero) PredictedTargetPos = Position + (Direction * MaxTrajectory);
            PrevTargetPos = PredictedTargetPos;
            PrevTargetVel = Vector3D.Zero;
            Info.ObjectsHit = 0;
            Info.BaseHealthPool = Info.System.Values.Ammo.Health;
            TracerLength = Info.System.TracerLength;

            if (Info.IsShrapnel)
            {
                var shrapnel = Info.System.Values.Ammo.Shrapnel;
                Info.BaseDamagePool = shrapnel.BaseDamage;
                Info.DetonationDamage = Info.System.Values.Ammo.AreaEffect.Detonation.DetonationDamage;
                Info.AreaEffectDamage = Info.System.Values.Ammo.AreaEffect.AreaEffectDamage;

                MaxTrajectory = shrapnel.MaxTrajectory;
                TracerLength = TracerLength / shrapnel.Fragments >= 1 ? TracerLength / shrapnel.Fragments : 1;
            }

            MaxTrajectorySqr = MaxTrajectory * MaxTrajectory;

            if (!Info.IsShrapnel) StartSpeed = Info.ShooterVel;

            if (Info.System.SpeedVariance && !Info.System.IsBeamWeapon)
            {
                var min = Info.System.Values.Ammo.Trajectory.SpeedVariance.Start;
                var max = Info.System.Values.Ammo.Trajectory.SpeedVariance.End;
                DesiredSpeed = Info.System.Values.Ammo.Trajectory.DesiredSpeed - MyUtils.GetRandomFloat(min, max);
            }
            else DesiredSpeed = Info.System.Values.Ammo.Trajectory.DesiredSpeed;

            MoveToAndActivate = LockedTarget && !Info.System.IsBeamWeapon && Guidance == AmmoTrajectory.GuidanceType.TravelTo;

            if (MoveToAndActivate)
            {
                var distancePos = PredictedTargetPos != Vector3D.Zero ? PredictedTargetPos : OriginTargetPos;
                Vector3D.DistanceSquared(ref Info.Origin, ref distancePos, out DistanceToTravelSqr);
            }
            else DistanceToTravelSqr = MaxTrajectorySqr;

            PickTarget = LockedTarget && Info.System.Values.Ammo.Trajectory.Smarts.OverideTarget;
            if (PickTarget || LockedTarget) NewTargets++;

            PruneQuery = DynamicGuidance || Info.Ai.ShieldNear ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
            if (!DynamicGuidance && Info.Ai.StaticEntitiesInRange)
                StaticEntCheck();
            else if (Info.Ai.PlanetSurfaceInRange) LinePlanetCheck = true;

            if (EnableAv)
            {
                Info.SetupSounds(DistanceFromCameraSqr);
                if (Info.System.HitParticle && !Info.System.IsBeamWeapon || Info.System.AreaEffect == AreaEffectType.Explosive && !Info.System.Values.Ammo.AreaEffect.Explosions.NoVisuals)
                {
                    var hitPlayChance = Info.System.Values.Graphics.Particles.Hit.Extras.HitPlayChance;
                    HitParticleActive = hitPlayChance >= 1 || hitPlayChance >= MyUtils.GetRandomDouble(0.0f, 1f);
                }
            }

            if (Info.System.PrimeModelId == -1 && Info.System.TriggerModelId == -1 || Info.IsShrapnel) ModelState = EntityState.None;
            else
            {
                if (EnableAv)
                {
                    ModelState = EntityState.Exists;

                    double triggerModelSize = 0;
                    double primeModelSize = 0;
                    if (Info.System.TriggerModelId != -1) triggerModelSize = Info.TriggerEntity.PositionComp.WorldVolume.Radius;
                    if (Info.System.PrimeModelId != -1) primeModelSize = Info.PrimeEntity.PositionComp.WorldVolume.Radius;
                    var largestSize = triggerModelSize > primeModelSize ? triggerModelSize : primeModelSize;

                    ModelSphereCurrent.Radius = largestSize * 2;
                    ModelSphereLast.Radius = largestSize * 2;
                }
            }

            var accelPerSec = Info.System.Values.Ammo.Trajectory.AccelPerSec;
            ConstantSpeed = accelPerSec <= 0;
            AccelPerSec = accelPerSec > 0 ? accelPerSec : DesiredSpeed; 
            MaxVelocity = StartSpeed + (Direction * DesiredSpeed);
            MaxSpeed = MaxVelocity.Length();
            MaxSpeedSqr = MaxSpeed * MaxSpeed;
            AccelLength = Info.System.Values.Ammo.Trajectory.AccelPerSec * StepConst;
            AccelVelocity = (Direction * AccelLength);

            if (ConstantSpeed)
            {
                Velocity = MaxVelocity;
                VelocityLengthSqr = MaxSpeed * MaxSpeed;
            }
            else Velocity = StartSpeed + AccelVelocity;

            TravelMagnitude = Velocity * StepConst;
            if (EnableAv)
                Info.VisualShot.Init(Info.System, Info.Ai, AccelPerSec * StepConst, MaxSpeed);

            FieldTime = Info.System.Values.Ammo.Trajectory.FieldTime;

            State = !Info.System.IsBeamWeapon ? ProjectileState.Alive : ProjectileState.OneAndDone;

            if (Info.System.AmmoParticle && EnableAv && !Info.System.IsBeamWeapon)
            {
                BaseAmmoParticleScale = !Info.IsShrapnel ? 1 : 0.5f;
                PlayAmmoParticle();
            }
        }

        internal void StaticEntCheck()
        {
            var ai = Info.Ai;
            LinePlanetCheck = ai.PlanetSurfaceInRange && DynamicGuidance;
            for (int i = 0; i < Info.Ai.StaticsInRange.Count; i++)
            {
                var staticEnt = ai.StaticsInRange[i];
                var rotMatrix = Quaternion.CreateFromRotationMatrix(staticEnt.WorldMatrix);
                var obb = new MyOrientedBoundingBoxD(staticEnt.PositionComp.WorldAABB.Center, staticEnt.PositionComp.LocalAABB.HalfExtents, rotMatrix);
                var lineTest = new LineD(Position, Position + (Direction * MaxTrajectory));
                var voxel = staticEnt as MyVoxelBase;
                var grid = staticEnt as MyCubeGrid;

                if (obb.Intersects(ref lineTest) != null || voxel != null && voxel.PositionComp.WorldAABB.Contains(Position) == ContainmentType.Contains)
                {
                    if (voxel != null && voxel == voxel.RootVoxel)
                    {
                        if (voxel == ai.MyPlanet)
                        {
                            if (!Info.System.IsBeamWeapon)
                            {
                                Info.Ai.Session.Physics.CastRayParallel(ref lineTest.From, ref lineTest.To, RayHits, CollisionLayers.VoxelCollisionLayer, CouldHitPlanet);
                            }
                            else if (!Info.WeaponCache.VoxelHits[CachedId].Cached(lineTest, Info))
                            {
                                LinePlanetCheck = true;
                            }
                            else CachedPlanetHit = true;

                            PruneQuery = MyEntityQueryType.Both;
                        }
                        else
                        {
                            LinePlanetCheck = true;
                            PruneQuery = MyEntityQueryType.Both;
                        }
                        break;
                    }
                    if (grid != null && grid.IsSameConstructAs(Info.Ai.MyGrid)) continue;
                    PruneQuery = MyEntityQueryType.Both;
                    if (LinePlanetCheck || !ai.PlanetSurfaceInRange) break;
                }
            }
        }

        internal void CouldHitPlanet(List<IHitInfo> hitInfos)
        {
            for (int i = 0; i < hitInfos.Count; i++)
            {
                var hit = hitInfos[i];
                var voxel = hit.HitEntity as MyVoxelBase;
                if (voxel?.RootVoxel is MyPlanet)
                {
                    LinePlanetCheck = true;
                    break;
                }
            }
            hitInfos.Clear();
        }

        internal bool Intersected(DrawHit? drawHit, bool queue = true)
        {
            var hitPos = drawHit?.HitPos;
            if (!hitPos.HasValue) return false;
            if (EnableAv && (Info.System.DrawLine || Info.System.PrimeModelId != -1 || Info.System.TriggerModelId != -1))
            {
                TestSphere.Center = hitPos.Value;
                
                var length = Vector3D.Distance(LastPosition, hitPos.Value);
                var shrink = !Info.System.IsBeamWeapon;
                var reSize = shrink ? ReSize.Shrink : ReSize.None;
                //Info.UpdateShape(hitPos.Value, Direction, length, reSize);
                if (Info.VisualShot.OnScreen == Screen.None) CameraCheck();
                //Info.VisualShot.Update(Info.DistanceTraveled - Info.PrevDistanceTraveled, TracerLength, Info.ShooterVel, hitPos.Value, Direction);

                if (Info.MuzzleId != -1)
                {
                    Info.VisualShot.DrawHit = drawHit;
                    //Info.Complete(drawHit);
                    //Info.VisualShot.Complete(Info, drawHit);
                    //Info.Ai.Session.Projectiles.DrawProjectiles.Add(Info);
                }
            }

            Colliding = true;
            if (!Info.System.VirtualBeams && queue) Info.Ai.Session.Hits.Enqueue(this);
            else
            {
                Info.WeaponCache.VirtualHit = true;
                Info.WeaponCache.HitEntity.Entity = drawHit.Value.Entity;
                Info.WeaponCache.HitEntity.HitPos = drawHit.Value.HitPos;
                Info.WeaponCache.Hits = VrPros.Count;
                Info.WeaponCache.HitDistance = Vector3D.Distance(LastPosition, hitPos.Value);

                if (drawHit.Value.Entity is MyCubeGrid) Info.WeaponCache.HitBlock = drawHit.Value.Block;
                if (queue) Info.Ai.Session.Hits.Enqueue(this);
                if (EnableAv && Info.VisualShot.OnScreen == Screen.Tracer) CreateFakeBeams(drawHit, Info.Ai.Session.Projectiles.DrawProjectiles);
            }

            if (EnableAv)
                HitEffects();

            return true;
        }

        internal void CreateFakeBeams(DrawHit? drawHit, List<ProInfo> drawList, bool miss = false)
        {
            Vector3D? hitPos = null;
            if (drawHit?.HitPos != null) hitPos = drawHit.Value.HitPos;
            for (int i = 0; i < VrPros.Count; i++)
            {
                var vp = VrPros[i];
                var info = vp.Info;
                var vs = vp.VisualShot;
                vs.OnScreen = Info.VisualShot.OnScreen;
                if (info.System.ConvergeBeams)
                {
                    var beam = !miss ? new LineD(info.Origin, hitPos ?? Position) : new LineD(info.LineStart, Position);
                    //vt.UpdateShape(beam.To, beam.Direction, beam.Length, ReSize.None);
                    if (miss) vs.Update(beam.Length, beam.Length, info.ShooterVel, beam.To, beam.Direction);

                }
                else
                {
                    Vector3D beamEnd;
                    var hit = !miss && hitPos.HasValue;
                    if (!hit)
                        beamEnd = info.Origin + (vs.Direction * MaxTrajectory);
                    else
                        beamEnd = info.Origin + (vs.Direction * Info.WeaponCache.HitDistance);
                    var line = new LineD(info.Origin, beamEnd);
                    //DsDebugDraw.DrawSingleVec(vt.PrevPosition, 0.5f, Color.Red);
                    if (!miss && hitPos.HasValue)
                    {
                        //vt.UpdateShape(beamEnd, line.Direction, line.Length, ReSize.None);
                        //vs.Update(line.Length, line.Length, info.ShooterVel, beamEnd, line.Direction);

                    }

                    else
                    {
                        //vt.UpdateShape(line.To, line.Direction, line.Length, ReSize.None);
                        vs.Update(line.Length, line.Length, info.ShooterVel, line.To, line.Direction);
                    }
                }

                if (miss) vs.Complete(Info, drawHit);
                else vs.DrawHit = drawHit;

                info.Ai.Session.VisualShots.Add(vs);
                //drawList.Add(vt);
            }
        }

        private void CameraCheck()
        {
            if (ModelState == EntityState.Exists)
            {
                ModelSphereLast.Center = LastEntityPos;
                ModelSphereCurrent.Center = Position;
                if (Info.Ai.Session.Camera.IsInFrustum(ref ModelSphereLast) || Info.Ai.Session.Camera.IsInFrustum(ref ModelSphereCurrent) || FirstOffScreen)
                {
                    Info.VisualShot.OnScreen = Screen.Tracer;
                    FirstOffScreen = false;
                    LastEntityPos = Position;
                }
            }

            if (Info.VisualShot.OnScreen == Screen.None && Info.System.DrawLine)
            {

                var bb = new BoundingBoxD(Vector3D.Min(Info.VisualShot.TracerStart, Info.VisualShot.Position), Vector3D.Max(Info.VisualShot.TracerStart, Info.VisualShot.Position));
                if (Info.Ai.Session.Camera.IsInFrustum(ref bb)) Info.VisualShot.OnScreen = Screen.Tracer;

                if (Info.VisualShot.OnScreen == Screen.None && Info.System.Trail)
                    Info.VisualShot.OnScreen = Screen.Tail;
            }
        }

        private void SpawnShrapnel()
        {
            var shrapnel = Info.Ai.Session.Projectiles.ShrapnelPool.Get();
            shrapnel.Init(this, Info.Ai.Session.Projectiles.FragmentPool);
            Info.Ai.Session.Projectiles.ShrapnelToSpawn.Add(shrapnel);
        }

        internal bool NewTarget()
        {
            var giveUp = !PickTarget && ++NewTargets > Info.System.MaxTargets && Info.System.MaxTargets != 0;
            ChaseAge = Info.Age;
            PickTarget = false;

            if (giveUp || !GridAi.ReacquireTarget(this))
            {
                Info.Target.Entity = null;
                if (Info.Target.IsProjectile) UnAssignProjectile(true);
                return false;
            }

            if (Info.Target.IsProjectile) UnAssignProjectile(false);

            return true;
        }

        internal void ForceNewTarget()
        {
            ChaseAge = Info.Age;
            PickTarget = false;
        }

        internal void ActivateMine()
        {
            var ent = Info.Target.Entity;
            MineActivated = true;
            var targetPos = ent.PositionComp.WorldAABB.Center;
            var deltaPos = targetPos - Position;
            var targetVel = ent.Physics?.LinearVelocity ?? Vector3.Zero;
            var deltaVel = targetVel - Vector3.Zero;
            var timeToIntercept = MathFuncs.Intercept(deltaPos, deltaVel, Info.System.Values.Ammo.Trajectory.DesiredSpeed);
            var predictedPos = targetPos + (float)timeToIntercept * deltaVel;
            PredictedTargetPos = predictedPos;
            PrevTargetPos = predictedPos;
            PrevTargetVel = targetVel;
            LockedTarget = true;

            if (Guidance == AmmoTrajectory.GuidanceType.DetectFixed) return;

            Vector3D.DistanceSquared(ref Info.Origin, ref predictedPos, out DistanceToTravelSqr);
            Info.DistanceTraveled = 0;
            Info.PrevDistanceTraveled = 0;

            Direction = Vector3D.Normalize(predictedPos - Position);
            AccelDir = Direction;
            VisualDir = Direction;
            VelocityLengthSqr = 0;

            MaxVelocity = (Direction * DesiredSpeed);
            MaxSpeed = MaxVelocity.Length();
            MaxSpeedSqr = MaxSpeed * MaxSpeed;
            AccelVelocity = (Direction * AccelLength);

            if (ConstantSpeed)
            {
                Velocity = MaxVelocity;
                VelocityLengthSqr = MaxSpeed * MaxSpeed;
            }
            else Velocity = AccelVelocity;

            if (Guidance == AmmoTrajectory.GuidanceType.DetectSmart)
            {
                SmartsOn = true;
                MaxChaseAge = Info.System.Values.Ammo.Trajectory.Smarts.MaxChaseTime;
                if (SmartsOn && Info.System.TargetOffSet && LockedTarget)
                {
                    OffSetTarget();
                    OffsetSqr = Info.System.Values.Ammo.Trajectory.Smarts.Inaccuracy * Info.System.Values.Ammo.Trajectory.Smarts.Inaccuracy;
                }
                else
                {
                    TargetOffSet = Vector3D.Zero;
                    OffsetSqr = 0;
                }
            }

            TravelMagnitude = Velocity * StepConst;
        }

        internal void TriggerMine(bool startTimer)
        {
            DistanceToTravelSqr = double.MinValue;
            if (Info.System.Ewar)
            {
                Info.Triggered = true;
                if (startTimer) FieldTime = Info.System.Values.Ammo.Trajectory.Mines.FieldTime;
            }
            else if (startTimer) FieldTime = 0;
            MineTriggered = true;
            Log.Line($"[Mine] Ewar:{Info.System.Ewar} - Activated:{MineActivated} - active:{EwarActive} - Triggered:{Info.Triggered} - IdleTime:{FieldTime}");
        }

        internal void RunSmart()
        {
            Vector3D newVel;
            if ((AccelLength <= 0 || Vector3D.DistanceSquared(Info.Origin, Position) >= Info.System.SmartsDelayDistSqr))
            {
                var gaveUpChase = Info.Age - ChaseAge > MaxChaseAge;
                var validTarget = Info.Target.IsProjectile || Info.Target.Entity != null && !Info.Target.Entity.MarkedForClose;
                var isZombie = !Info.System.IsMine && ZombieLifeTime > 0 && ZombieLifeTime % 30 == 0;
                if ((gaveUpChase || PickTarget || isZombie) && NewTarget() || validTarget)
                {
                    if (ZombieLifeTime > 0) UpdateZombie(true);
                    var targetPos = Vector3D.Zero;
                    if (Info.Target.IsProjectile) targetPos = Info.Target.Projectile.Position;
                    else if (Info.Target.Entity != null) targetPos = Info.Target.Entity.PositionComp.WorldAABB.Center;


                    if (Info.System.TargetOffSet)
                    {
                        if (Info.Age - LastOffsetTime > 300)
                        {
                            double dist;
                            Vector3D.DistanceSquared(ref Position, ref targetPos, out dist);
                            if (dist < OffsetSqr + VelocityLengthSqr && Vector3.Dot(Direction, Position - targetPos) > 0)
                                OffSetTarget();
                        }
                        targetPos += TargetOffSet;
                    }

                    var physics = Info.Target.Entity?.Physics ?? Info.Target.Entity?.Parent?.Physics;

                    if (!Info.Target.IsProjectile && (physics == null || targetPos == Vector3D.Zero))
                        PrevTargetPos = PredictedTargetPos;
                    else PrevTargetPos = targetPos;

                    var tVel = Vector3.Zero;
                    if (Info.Target.IsProjectile) tVel = Info.Target.Projectile.Velocity;
                    else if (physics != null) tVel = physics.LinearVelocity;

                    if (Info.System.TargetLossDegree > 0 && Info.Ai.Session.Tick60)
                    {
                        if (!MyUtils.IsZero(tVel, 1E-02F))
                        {
                            var targetDir = Vector3D.Normalize(tVel);
                            var refDir = Vector3D.Normalize(Position - targetPos);
                            if (!MathFuncs.IsDotProductWithinTolerance(ref targetDir, ref refDir, Info.System.TargetLossDegree))
                            {
                                Log.Line($"not in targetlossDegree");
                                PickTarget = true;
                            }
                        }
                    }

                    PrevTargetVel = tVel;
                }
                else UpdateZombie();
                var commandedAccel = MathFuncs.CalculateMissileIntercept(PrevTargetPos, PrevTargetVel, Position, Velocity, AccelPerSec, Info.System.Values.Ammo.Trajectory.Smarts.Aggressiveness, Info.System.Values.Ammo.Trajectory.Smarts.MaxLateralThrust);
                newVel = Velocity + (commandedAccel * StepConst);
                AccelDir = commandedAccel / AccelPerSec;
                Vector3D.Normalize(ref Velocity, out Direction);
            }
            else newVel = Velocity += (Direction * AccelLength);
            VelocityLengthSqr = newVel.LengthSquared();

            if (VelocityLengthSqr > MaxSpeedSqr) newVel = Direction * MaxSpeed;
            Velocity = newVel;
        }

        internal void UpdateZombie(bool reset = false)
        {
            if (reset)
            {
                ZombieLifeTime = 0;
                OffSetTarget();
            }
            else
            {
                PrevTargetPos = PredictedTargetPos;
                if (ZombieLifeTime++ > Info.System.TargetLossTime)
                {
                    DistanceToTravelSqr = Info.DistanceTraveled * Info.DistanceTraveled;
                }
                if (Info.Age - LastOffsetTime > 300)
                {
                    double dist;
                    Vector3D.DistanceSquared(ref Position, ref PrevTargetPos, out dist);
                    if (dist < OffsetSqr + VelocityLengthSqr && Vector3.Dot(Direction, Position - PrevTargetPos) > 0)
                    {
                        OffSetTarget(true);
                        PrevTargetPos += TargetOffSet;
                        PredictedTargetPos = PrevTargetPos;
                    }
                }
            }
        }

        internal void RunEwar()
        {
            if (VelocityLengthSqr <= 0 && !Info.Triggered && !Info.System.IsMine)
                Info.Triggered = true;

            if (Info.Triggered)
            {
                var areaSize = Info.System.AreaEffectSize;
                if (Info.TriggerGrowthSteps < areaSize)
                {
                    const int expansionPerTick = 100 / 60;
                    var nextSize = (double)++Info.TriggerGrowthSteps * expansionPerTick;
                    if (nextSize <= areaSize)
                    {
                        var nextRound = nextSize + 1;
                        if (nextRound > areaSize)
                        {
                            if (nextSize < areaSize)
                            {
                                nextSize = areaSize;
                                ++Info.TriggerGrowthSteps;
                            }
                        }
                        MatrixD.Rescale(ref Info.TriggerMatrix, nextSize);
                    }
                }
            }

            if (Info.Age % Info.System.PulseInterval == 0 || State == ProjectileState.OneAndDone)
                PulseEffect();
            else EwarActive = false;
        }

        internal void PulseEffect()
        {
            switch (Info.System.AreaEffect)
            {
                case AreaEffectType.AntiSmart:
                    var eWarSphere = new BoundingSphereD(Position, Info.System.AreaEffectSize);
                    DynTrees.GetAllProjectilesInSphere(Info.Ai.Session, ref eWarSphere, EwaredProjectiles, false);
                    for (int j = 0; j < EwaredProjectiles.Count; j++)
                    {
                        var netted = EwaredProjectiles[j];
                        if (netted.Info.Ai == Info.Ai || netted.Info.Target.IsProjectile) continue;
                        //Log.Line("netted");
                        if (MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                        {
                            EwarActive = true;
                            //Log.Line($"change course: {netted.Info.Target.Projectile != null}");
                            netted.Info.Target.Projectile = this;
                            netted.Info.Target.IsProjectile = true;
                            Seekers.Add(netted);
                        }
                    }
                    EwaredProjectiles.Clear();
                    break;
                case AreaEffectType.JumpNullField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"jumpNullField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
                case AreaEffectType.AnchorField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"jumpAnchorFieldNullField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
                case AreaEffectType.EnergySinkField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"EnergySinkField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
                case AreaEffectType.EmpField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"EmpField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
                case AreaEffectType.OffenseField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"OffenseField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
                case AreaEffectType.NavField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"NavField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }

                    break;
                case AreaEffectType.DotField:
                    if (Info.Triggered && MyUtils.GetRandomInt(0, 100) < Info.System.PulseChance)
                    {
                        //Log.Line($"DotField Pulse - Time:{IdleTime} - distTravel:{Info.DistanceTraveled}({Info.DistanceTraveled * Info.DistanceTraveled} >= {DistanceToTravelSqr})");
                        EwarActive = true;
                    }
                    break;
            }
        }


        internal void SeekEnemy()
        {
            var mineInfo = Info.System.Values.Ammo.Trajectory.Mines;
            var detectRadius = mineInfo.DetectRadius;
            var deCloakRadius = mineInfo.DeCloakRadius;

            var wakeRadius = detectRadius > deCloakRadius ? detectRadius : deCloakRadius;
            PruneSphere = new BoundingSphereD(Position, wakeRadius);
            var checkList = Info.Ai.Session.Projectiles.CheckPool.Get();
            var inRange = false;
            var activate = false;
            var minDist = double.MaxValue;
            if (!MineActivated)
            {
                MyEntity closestEnt = null;
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref PruneSphere, checkList, MyEntityQueryType.Dynamic);
                for (int i = 0; i < checkList.Count; i++)
                {
                    var ent = checkList[i];
                    var grid = ent as MyCubeGrid;
                    var character = ent as IMyCharacter;
                    if (grid == null && character == null || ent.MarkedForClose || !ent.InScene) continue;
                    Sandbox.ModAPI.Ingame.MyDetectedEntityInfo entInfo;
                    if (!Info.Ai.CreateEntInfo(ent, Info.Ai.MyOwner, out entInfo)) continue;
                    switch (entInfo.Relationship)
                    {
                        case MyRelationsBetweenPlayerAndBlock.Owner:
                            continue;
                        case MyRelationsBetweenPlayerAndBlock.FactionShare:
                            continue;
                    }
                    var entSphere = ent.PositionComp.WorldVolume;
                    entSphere.Radius += Info.System.CollisionSize;
                    var dist = MyUtils.GetSmallestDistanceToSphereAlwaysPositive(ref Position, ref entSphere);
                    if (dist >= minDist) continue;
                    minDist = dist;
                    closestEnt = ent;
                }

                if (closestEnt != null)
                {
                    ForceNewTarget();
                    Info.Target.Entity = closestEnt;
                }
            }
            else if (Info.Target.Entity != null && !Info.Target.Entity.MarkedForClose)
            {
                var entSphere = Info.Target.Entity.PositionComp.WorldVolume;
                entSphere.Radius += Info.System.CollisionSize;
                minDist = MyUtils.GetSmallestDistanceToSphereAlwaysPositive(ref Position, ref entSphere);
            }
            else
                TriggerMine(true);

            if (Info.Cloaked && minDist <= deCloakRadius) Info.Cloaked = false;
            else if (!Info.Cloaked && minDist > deCloakRadius) Info.Cloaked = true;

            if (minDist <= Info.System.CollisionSize) activate = true;
            if (minDist <= detectRadius) inRange = true;
            if (MineActivated)
            {
                if (!inRange)
                    TriggerMine(true);
            }
            else if (inRange) ActivateMine();

            if (activate)
            {
                TriggerMine(false);
                SegmentList.Add(new MyLineSegmentOverlapResult<MyEntity> { Distance = minDist, Element = Info.Target.Entity });
            }

            checkList.Clear();
            Info.Ai.Session.Projectiles.CheckPool.Return(checkList);
        }

        internal void OffSetTarget(bool roam = false)
        {
            var randAzimuth = MyUtils.GetRandomDouble(0, 1) * 2 * Math.PI;
            var randElevation = (MyUtils.GetRandomDouble(0, 1) * 2 - 1) * 0.5 * Math.PI;

            var offsetAmount = roam ? 100 : Info.System.Values.Ammo.Trajectory.Smarts.Inaccuracy;
            Vector3D randomDirection;
            Vector3D.CreateFromAzimuthAndElevation(randAzimuth, randElevation, out randomDirection); // this is already normalized
            PrevTargetOffset = TargetOffSet;
            TargetOffSet = (randomDirection * offsetAmount);
            VisualStep = 0;
            if (Info.Age != 0) LastOffsetTime = Info.Age;
        }

        internal void HitEffects()
        {
            if (Colliding || ForceHitParticle)
            {
                var distToCameraSqr = Vector3D.DistanceSquared(Position, Info.Ai.Session.CameraPos);
                var closeToCamera = distToCameraSqr < 360000;
                if (ForceHitParticle) LastHitPos = Position;

                if (Info.VisualShot.OnScreen == Screen.Tracer && HitParticleActive && Info.System.HitParticle) PlayHitParticle();
                else if (HitParticleActive && (Info.VisualShot.OnScreen == Screen.Tracer || closeToCamera)) Info.FakeExplosion = true;
                Info.HitSoundActived = Info.System.HitSound && (Info.HitSoundActive && (ForceHitParticle || distToCameraSqr < Info.System.HitSoundDistSqr || LastHitPos.HasValue && (!Info.LastHitShield || Info.System.Values.Audio.Ammo.HitPlayShield)));

                if (Info.HitSoundActived) Info.HitEmitter.Entity = Info.VisualShot.DrawHit?.Entity;
                Info.LastHitShield = false;
            }
            Colliding = false;
        }

        internal void PlayAmmoParticle()
        {
            if (Info.Age == 0 && !ParticleLateStart)
            {
                TestSphere.Center = Position;
                if (!Info.Ai.Session.Camera.IsInFrustum(ref TestSphere))
                {
                    ParticleLateStart = true;
                    return;
                }
            }
            MatrixD matrix;
            if (ModelState == EntityState.Exists)
            {
                matrix = MatrixD.CreateWorld(Position, AccelDir, Info.PrimeEntity.PositionComp.WorldMatrix.Up);
                if (Info.IsShrapnel) MatrixD.Rescale(ref matrix, 0.5f);
                var offVec = Position + Vector3D.Rotate(Info.System.Values.Graphics.Particles.Ammo.Offset, matrix);
                matrix.Translation = offVec;
                Info.PrimeMatrix = matrix;
            }
            else
            {
                matrix = MatrixD.CreateWorld(Position, AccelDir, Info.OriginUp);
                var offVec = Position + Vector3D.Rotate(Info.System.Values.Graphics.Particles.Ammo.Offset, matrix);
                matrix.Translation = offVec;
            }

            MyParticlesManager.TryCreateParticleEffect(Info.System.Values.Graphics.Particles.Ammo.Name, ref matrix, ref Position, uint.MaxValue, out AmmoEffect); // 15, 16, 24, 25, 28, (31, 32) 211 215 53
            if (AmmoEffect == null) return;
            AmmoEffect.DistanceMax = Info.System.Values.Graphics.Particles.Ammo.Extras.MaxDistance;
            AmmoEffect.UserColorMultiplier = Info.System.Values.Graphics.Particles.Ammo.Color;
            //var reScale = (float)Math.Log(195312.5, DistanceFromCameraSqr); // wtf is up with particles and camera distance
            var scaler = !Info.IsShrapnel ? 1 : 0.5f;

            AmmoEffect.UserRadiusMultiplier = Info.System.Values.Graphics.Particles.Ammo.Extras.Scale * scaler;
            AmmoEffect.UserEmitterScale = 1 * scaler;
            if (ConstantSpeed) AmmoEffect.Velocity = Velocity;
            ParticleStopped = false;
            ParticleLateStart = false;
        }

        internal void PlayHitParticle()
        {
            if (HitEffect != null) DisposeHitEffect(false);
            if (LastHitPos.HasValue)
            {
                if (!Info.System.Values.Graphics.Particles.Hit.ApplyToShield && Info.LastHitShield)
                    return;

                var pos = LastHitPos.Value;
                var matrix = MatrixD.CreateTranslation(pos);
                MyParticlesManager.TryCreateParticleEffect(Info.System.Values.Graphics.Particles.Hit.Name, ref matrix, ref pos, uint.MaxValue, out HitEffect);
                if (HitEffect == null) return;
                HitEffect.Loop = false;
                HitEffect.DurationMax = Info.System.Values.Graphics.Particles.Hit.Extras.MaxDuration;
                HitEffect.DistanceMax = Info.System.Values.Graphics.Particles.Hit.Extras.MaxDistance;
                HitEffect.UserColorMultiplier = Info.System.Values.Graphics.Particles.Hit.Color;
                var reScale = 1;
                var scaler = reScale < 1 ? reScale : 1;

                HitEffect.UserRadiusMultiplier = Info.System.Values.Graphics.Particles.Hit.Extras.Scale * scaler;
                var scale = Info.System.HitParticleShrinks ? MathHelper.Clamp(MathHelper.Lerp(BaseAmmoParticleScale, 0, Info.DistanceToLine / Info.System.Values.Graphics.Particles.Hit.Extras.MaxDistance), 0, BaseAmmoParticleScale) : 1;
                HitEffect.UserEmitterScale = scale * scaler;
                var hitVel = LastHitEntVel ?? Vector3.Zero;
                Vector3.ClampToSphere(ref hitVel, (float)MaxSpeed);
                HitEffect.Velocity = hitVel;
            }
        }

        internal void DisposeAmmoEffect(bool instant, bool pause)
        {
            if (AmmoEffect != null)
            {
                AmmoEffect.Stop(instant);
                AmmoEffect = null;
            }

            if (pause) ParticleStopped = true;
        }

        private void DisposeHitEffect(bool instant)
        {
            if (HitEffect != null)
            {
                HitEffect.Stop(instant);
                HitEffect = null;
            }
        }

        internal void PauseAv()
        {
            DisposeAmmoEffect(true, true);
            DisposeHitEffect(true);
        }

        internal void DestroyProjectile()
        {
            if (State == ProjectileState.Destroy)
            {
                ForceHitParticle = true;
                Intersected(new DrawHit(null, null, null, Position), false);
            }

            State = ProjectileState.Depleted;
        }

        internal void UnAssignProjectile(bool clear)
        {
            Info.Target.Projectile.Seekers.Remove(this);
            if (clear) Info.Target.Reset();
            else
            {
                Info.Target.IsProjectile = false;
                Info.Target.Projectile = null;
            }
        }

        internal void ProjectileClose()
        {
            if (!Info.IsShrapnel && GenerateShrapnel) SpawnShrapnel();
            else Info.IsShrapnel = false;

            for (int i = 0; i < Watchers.Count; i++) Watchers[i].DeadProjectiles.Enqueue(this);
            Watchers.Clear();

            foreach (var seeker in Seekers) seeker.Info.Target.Reset();
            Seekers.Clear();

            if (EnableAv)
            {
                if (Info.System.AmmoParticle) DisposeAmmoEffect(false, false);
                HitEffects();
            }
            State = ProjectileState.Dead;
            Info.Ai.Session.Projectiles.CleanUp.Add(this);

            if (ModelState == EntityState.Exists)
            {
                Info.PrimeMatrix = MatrixD.Identity;
                Info.TriggerMatrix = MatrixD.Identity;
                Info.VisualShot.Complete(Info);
                Info.Ai.Session.Projectiles.DrawProjectiles.Add(Info);
                if (Info.System.PrimeModelId != -1) Info.Ai.Session.Projectiles.EntityPool[Info.System.PrimeModelId].MarkForDeallocate(Info.PrimeEntity);
                if (Info.System.TriggerModelId != -1) Info.Ai.Session.Projectiles.EntityPool[Info.System.TriggerModelId].MarkForDeallocate(Info.TriggerEntity);
                Info.Ai.Session.VisualShotPool.Return(Info.VisualShot);
                ModelState = EntityState.None;
            }
        }

        internal enum ProjectileState
        {
            Start,
            Alive,
            Detonate,
            OneAndDone,
            Dead,
            Depleted,
            Destroy,
        }

        internal enum EntityState
        {
            Exists,
            None
        }
    }
}