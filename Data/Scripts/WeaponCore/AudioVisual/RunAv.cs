﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRage.Game;
using VRageMath;
using WeaponCore.Support;

namespace WeaponCore.Support
{
    class RunAv
    {
        internal readonly MyConcurrentPool<AvShot> AvShotPool = new MyConcurrentPool<AvShot>(128, shot => shot.Close());
        internal readonly List<AvShot> AvShots = new List<AvShot>(128);
        internal readonly Stack<AfterGlow> Glows = new Stack<AfterGlow>();

        internal Session Session;

        internal int ExplosionCounter;

        internal bool ExplosionReady
        {
            get
            {
                if (++ExplosionCounter <= 5)
                {
                    return true;
                }
                return false;
            }
        }

        internal RunAv(Session session)
        {
            Session = session;
        }

        private int _onScreens = 0;
        private int _shrinks = 0;
        private int _glows = 0;
        private int _models = 0;


        internal void Run()
        {

            if (Session.Tick300)
            {
                Log.Line($"Total AvShots:{AvShots.Count} - onScreen:{_onScreens} - shrinks:{_shrinks} - glows:{_glows} - models:{_models}");
                _glows = 0;
                _shrinks = 0;
            }

            _onScreens = 0;
            _models = 0;
            for (int i = AvShots.Count - 1; i >= 0; i--)
            {
                var av = AvShots[i];
                if (av.OnScreen != AvShot.Screen.None) _onScreens++;
                var refreshed = av.LastTick == Session.Tick;
                ++av.LifeTime;
                if (av.LifeTime > 5000) Log.Line($"weapon:{av.System.WeaponName} - tracer:{av.Tracer} - tail:{av.Trail} - onScreen:{av.OnScreen} - glowCnt:{av.GlowSteps.Count} - shrinks:{av.TracerShrinks.Count} - Grid:{av.Ai.MyGrid.DisplayName}");
                
                if (refreshed && av.Tracer != AvShot.TracerState.Off && av.OnScreen != AvShot.Screen.None)
                {
                    if (!av.System.OffsetEffect)
                    {
                        if (av.Tracer != AvShot.TracerState.Shrink)
                            MyTransparentGeometry.AddLineBillboard(av.System.TracerMaterial, av.Color, av.TracerStart, av.PointDir, (float)av.TracerLength, (float)av.Thickness);
                    }
                    else
                    {
                        var list = av.Offsets;
                        for (int x = 0; x < list.Count; x++)
                        {
                            Vector3D fromBeam;
                            Vector3D toBeam;

                            if (x == 0)
                            {
                                fromBeam = av.OffsetMatrix.Translation;
                                toBeam = Vector3D.Transform(list[x], av.OffsetMatrix);
                            }
                            else
                            {
                                fromBeam = Vector3D.Transform(list[x - 1], av.OffsetMatrix);
                                toBeam = Vector3D.Transform(list[x], av.OffsetMatrix);
                            }

                            Vector3 dir = (toBeam - fromBeam);
                            var length = dir.Length();
                            var normDir = dir / length;
                            MyTransparentGeometry.AddLineBillboard(av.System.TracerMaterial, av.Color, fromBeam, normDir, length, (float)av.Thickness);

                            if (Vector3D.DistanceSquared(av.OffsetMatrix.Translation, toBeam) > av.TracerLengthSqr) break;
                        }
                        list.Clear();
                        Session.ListOfVectorsPool.Return(list);
                    }
                }

                var shrinkCnt = av.TracerShrinks.Count;
                if (shrinkCnt > _shrinks) _shrinks = shrinkCnt;
                if (shrinkCnt > 0)
                    RunShrinks(av);

                var glowCnt = av.GlowSteps.Count;
                if (glowCnt > _glows)
                {
                    _glows = glowCnt;
                }
                if (av.Trail != AvShot.TrailState.Off)
                {
                    var steps = av.System.Values.Graphics.Line.Trail.DecayTime;
                    var remove = false;
                    for (int j = glowCnt - 1; j >= 0; j--)
                    {
                        var glow = av.GlowSteps[j];

                        if (av.OnScreen != AvShot.Screen.None)
                        {
                            var reduction = (av.GlowShrinkSize * glow.Step);
                            var width = (av.System.Values.Graphics.Line.Tracer.Width - reduction) * av.LineScaler;
                            MyTransparentGeometry.AddLineBillboard(av.System.TrailMaterial, av.System.Values.Graphics.Line.Trail.Color, glow.Line.To, glow.Line.Direction, (float)glow.Line.Length, width);
                        }
                        if (++glow.Step >= steps)
                        {
                            remove = true;
                            glowCnt--;
                            Glows.Push(glow);
                        }
                    }

                    if (remove) av.GlowSteps.Dequeue();
                }

                if (av.PrimeEntity != null)
                {
                    _models++;
                    if (refreshed)
                    {
                        if (av.Model != AvShot.ModelState.Close && !av.PrimeEntity.InScene && !av.Cloaked)
                        {
                            av.PrimeEntity.InScene = true;
                            av.PrimeEntity.Render.UpdateRenderObject(true, false);
                        }

                        if (av.OnScreen != AvShot.Screen.None) av.PrimeEntity.PositionComp.SetWorldMatrix(av.PrimeMatrix, null, false, false, false);
                    }

                    if (av.Model == AvShot.ModelState.Close || refreshed && av.Cloaked && av.PrimeEntity.InScene)
                    {
                        av.PrimeEntity.InScene = false;
                        av.PrimeEntity.Render.RemoveRenderObjects();
                        if (av.Model == AvShot.ModelState.Close) av.Model = AvShot.ModelState.None;
                    }
                }

                if (av.Triggered && av.TriggerEntity != null)
                {
                    if (refreshed)
                    {
                        if ((av.Model != AvShot.ModelState.Close && !av.TriggerEntity.InScene))
                        {
                            av.TriggerEntity.InScene = true;
                            av.TriggerEntity.Render.UpdateRenderObject(true, false);
                        }

                        av.TriggerEntity.PositionComp.SetWorldMatrix(av.TriggerMatrix, null, false, false, false);
                    }

                    if (av.Model == AvShot.ModelState.Close)
                    {
                        av.TriggerEntity.InScene = false;
                        av.TriggerEntity.Render.RemoveRenderObjects();
                        av.Model = AvShot.ModelState.None;
                    }
                }

                if (refreshed)
                {
                    if (av.StartSoundActived)
                    {
                        av.StartSoundActived = false;
                        av.FireEmitter.PlaySound(av.FireSound, true);
                    }

                    if (av.HasTravelSound)
                    {
                        if (!av.AmmoSound)
                        {
                            double distSqr;
                            Vector3D.DistanceSquared(ref av.Position, ref Session.CameraPos, out distSqr);
                            if (distSqr <= av.System.AmmoTravelSoundDistSqr)
                            {
                                Log.Line($"travel sound activated");
                                av.AmmoSoundStart();
                            }
                        }
                        else av.TravelEmitter.SetPosition(av.Position);
                    }

                    if (av.HitSoundActived)
                    {
                        Log.Line($"hit sound activated");
                        av.HitSoundActived = false;
                        av.HitEmitter.SetPosition(av.Position);
                        av.HitEmitter.CanPlayLoopSounds = false;
                        av.HitEmitter.PlaySound(av.HitSound, true);
                        /*
                        var prevPos = t.Position + (-t.Direction * t.Length);
                        IHitInfo hitInfo;
                        Physics.CastRay(prevPos, t.Position, out hitInfo, 15, false);
                        if (hitInfo?.HitEntity != null)
                        {
                            Log.Line("hit");
                            var myHitInfo = new MyHitInfo { Position = hitInfo.Position, Normal = hitInfo.Normal };
                            MyDecals.HandleAddDecal(hitInfo.HitEntity, myHitInfo, new MyStringHash(), new MyStringHash(), null, -1f);
                        }
                        */
                    }

                    if (av.FakeExplosion && refreshed)
                    {
                        av.FakeExplosion = false;
                        if (ExplosionReady)
                            SUtils.CreateFakeExplosion(Session, av.System.Values.Ammo.AreaEffect.AreaEffectRadius, av.Position, av.System);
                    }
                }

                var noNextStep = glowCnt == 0 && shrinkCnt == 0 && av.Model == AvShot.ModelState.None;
                if (noNextStep && (!refreshed || av.System.IsBeamWeapon))
                {
                    AvShotPool.Return(av);
                    AvShots.RemoveAtFast(i);
                }
            }

        }

        private void RunShrinks(AvShot av)
        {
            if (!av.System.OffsetEffect)
            {
                var s = av.TracerShrinks.Dequeue();
                if (av.OnScreen != AvShot.Screen.None)
                {
                    MyTransparentGeometry.AddLineBillboard(av.System.TracerMaterial, s.Color, s.Start, -av.PointDir, s.Length, s.Thickness);
                    if (av.Trail != AvShot.TrailState.Off)
                        av.RunGlow();
                }
            }
            else
            {
                var s = av.TracerShrinks.Dequeue();
                var list = av.ShrinkOffsets.Dequeue();
                if (av.OnScreen != AvShot.Screen.None)
                {
                    for (int x = 0; x < list.Count; x++)
                    {
                        Vector3D fromBeam;
                        Vector3D toBeam;

                        if (x == 0)
                        {
                            fromBeam = av.OffsetMatrix.Translation;
                            toBeam = Vector3D.Transform(list[x], av.OffsetMatrix);
                        }
                        else
                        {
                            fromBeam = Vector3D.Transform(list[x - 1], av.OffsetMatrix);
                            toBeam = Vector3D.Transform(list[x], av.OffsetMatrix);
                        }

                        Vector3 dir = (toBeam - fromBeam);
                        var length = dir.Length();
                        var normDir = dir / length;
                        MyTransparentGeometry.AddLineBillboard(av.System.TracerMaterial, s.Color, s.Start, normDir, s.Length, s.Thickness);
                        if (Vector3D.DistanceSquared(av.OffsetMatrix.Translation, toBeam) > av.TracerLengthSqr) break;
                    }
                }
                list.Clear();
                Session.ListOfVectorsPool.Return(list);
            }
        }
    }
}