﻿using System.Diagnostics;
using Sandbox.ModAPI;
using VRage.Input;
using WeaponCore.Support;
namespace WeaponCore
{
    internal class UiInput
    {
        internal int PreviousWheel;
        internal int CurrentWheel;
        internal int ShiftTime;
        internal bool MouseButtonPressed;
        internal bool MouseButtonLeft;
        internal bool MouseButtonMiddle;
        internal bool MouseButtonRight;
        internal bool WheelForward;
        internal bool WheelBackward;
        internal bool ShiftReleased;
        internal bool ShiftPressed;
        internal bool LongShift;
        internal bool AltPressed;
        internal bool AnyKeyPressed;
        internal bool PlayerCamera;
        private readonly Session _session;

        internal UiInput(Session session)
        {
            _session = session;
        }

        internal void UpdateInputState()
        {
            var s = _session;
            MouseButtonPressed = MyAPIGateway.Input.IsAnyMousePressed();
            WheelForward = false;
            WheelBackward = false;
            if (MouseButtonPressed)
            {
                MouseButtonLeft = MyAPIGateway.Input.IsMousePressed(MyMouseButtonsEnum.Left);
                MouseButtonMiddle = MyAPIGateway.Input.IsMousePressed(MyMouseButtonsEnum.Middle);
                MouseButtonRight = MyAPIGateway.Input.IsMousePressed(MyMouseButtonsEnum.Right);
            }
            else
            {
                MouseButtonLeft = false;
                MouseButtonMiddle = false;
                MouseButtonRight = false;
            }

            if (!s.InGridAiCockPit && !PlayerCamera) s.UpdateLocalAiAndCockpit();

            if (s.InGridAiCockPit)
            {
                PreviousWheel = MyAPIGateway.Input.PreviousMouseScrollWheelValue();
                CurrentWheel = MyAPIGateway.Input.MouseScrollWheelValue();
                ShiftReleased = MyAPIGateway.Input.IsNewKeyReleased(MyKeys.LeftShift);
                ShiftPressed = MyAPIGateway.Input.IsKeyPress(MyKeys.LeftShift);

                if (ShiftPressed)
                {
                    ShiftTime++;
                    LongShift = ShiftTime > 59;
                }
                else
                {
                    if (LongShift) ShiftReleased = false;
                    ShiftTime = 0;
                    LongShift = false;
                }

                AltPressed = MyAPIGateway.Input.IsAnyAltKeyPressed();
                AnyKeyPressed = MyAPIGateway.Input.IsAnyKeyPress();
                PlayerCamera = MyAPIGateway.Session.IsCameraControlledObject;
            }
            if (CurrentWheel != PreviousWheel && CurrentWheel > PreviousWheel)
                WheelForward = true;
            else if (s.UiInput.CurrentWheel != s.UiInput.PreviousWheel)
                WheelBackward = true;
        }
    }
}
