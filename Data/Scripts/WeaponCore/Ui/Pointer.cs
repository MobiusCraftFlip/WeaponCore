﻿
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using WeaponCore.Support;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace WeaponCore
{

    internal class Pointer
    {
        private GridAi Ai;
        private MyCockpit _cockPit;
        private MyEntity _target;
        private bool _inView;
        private readonly MyStringId _centerCross = MyStringId.GetOrCompute("Crosshair_Center");
        private readonly MyStringId _cross = MyStringId.GetOrCompute("Crosshair");
        private readonly List<IHitInfo> _hitInfo = new List<IHitInfo>();
        internal bool GetAi()
        {
            _cockPit = Session.Instance.Session.ControlledObject as MyCockpit;
            return _cockPit != null && Session.Instance.GridTargetingAIs.TryGetValue(_cockPit.CubeGrid, out Ai);
        }

        internal void PointingAt()
        {
            return;
            if (!GetAi()) return;
            var gridPos = _cockPit.CubeGrid.PositionComp.WorldAABB.Center;
            var cockpitPos = _cockPit.PositionComp.WorldAABB.Center;
            var cameraPos = Session.Instance.CameraPos;
            var crosshairPos = (cameraPos - cockpitPos);
            var cockDir = Vector3.Normalize(_cockPit.PositionComp.WorldMatrix.Forward);
            DsDebugDraw.DrawLine(cockpitPos, crosshairPos, Color.Yellow, 0.5f);
            DsDebugDraw.DrawLine(cockpitPos, cockpitPos + (cockDir * 5000000), Color.Blue, 0.5f);
            DsDebugDraw.DrawLine(gridPos, gridPos + (cockDir * 5000000), Color.Red, 0.5f);
            Session.Instance.Physics.CastRay(cockpitPos, cockpitPos + (cockDir * 5000000), _hitInfo);
            for (int i = 0; i < _hitInfo.Count; i++)
            {
                var hit = _hitInfo[i].HitEntity as MyCubeGrid;
                if (hit == null) continue;
                Log.Line($"{hit.DebugName}");
            }
        }

        private void SetTarget(MyEntity target)
        {
            _inView = true;
            var width = (float)target.PositionComp.WorldAABB.Extents.X;
            var height = (float)target.PositionComp.WorldAABB.Extents.Z;
            var box = _target.PositionComp.WorldAABB;
            box.Translate(_target.PositionComp.WorldMatrix);
            var center = _target.PositionComp.WorldAABB.Min;
            center.X += box.HalfExtents.X;
            center.Y += box.HalfExtents.Y;
            center.Z += box.HalfExtents.Z;

            MyTransparentGeometry.AddBillboardOriented(_cross, Color.WhiteSmoke.ToVector4(), center, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, height, BlendTypeEnum.PostPP);
        }

        private void updateCamera()
        {
            var targetPos = _target.PositionComp.WorldAABB.Center;
            var startPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            var dirA = targetPos - startPos;
            var distance = dirA.Length();
            dirA.Normalize();

            float scale;
            float dis;

            if (distance < 150)
            {
                scale = 2.5f;
                dis = 50f;
            }
            else
            {
                scale = 7.5f;
                dis = 150f;
            }

            var drawAt = startPos + dirA * dis;

            if (_inView)
                MyTransparentGeometry.AddBillboardOriented(_centerCross, Color.Red.ToVector4(), drawAt, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, scale, BlendTypeEnum.SDR);
            else
                MyTransparentGeometry.AddBillboardOriented(_centerCross, Color.White.ToVector4(), drawAt, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, scale, BlendTypeEnum.SDR);

            _inView = false;
        }
    }
}