﻿using System;
using HA4IoT.Actuators;
using HA4IoT.Actuators.Animations;
using HA4IoT.Contracts.Actuators;
using HA4IoT.Contracts.Hardware;
using HA4IoT.Contracts.WeatherStation;
using HA4IoT.Hardware;
using HA4IoT.Hardware.CCTools;
using HA4IoT.Hardware.DHT22;
using HA4IoT.Hardware.GenericIOBoard;

namespace HA4IoT.Controller.Main.Rooms
{
    internal class FloorConfiguration
    {
        private enum Floor
        {
            StairwayMotionDetector,

            StairwayLampWall,
            StairwayLampCeiling,
            CombinedStairwayLamp,

            StairwayRollerShutter,

            LowerFloorTemperatureSensor,
            LowerFloorHumiditySensor,
            LowerFloorMotionDetector,

            StairsLowerMotionDetector,
            StairsUpperMotionDetector,

            ButtonLowerFloorUpper,
            ButtonLowerFloorLower,
            ButtonLowerFloorAtBathroom,
            ButtonLowerFloorAtKitchen,
            ButtonStairway,
            ButtonStairsLowerUpper,
            ButtonStairsLowerLower,
            ButtonStairsUpper,

            Lamp1,
            Lamp2,
            Lamp3,
            CombinedLamps,

            LampStairsCeiling,
            LampStairs
        }

        public void Setup(Home home, CCToolsBoardController ccToolsController, IOBoardCollection ioBoardManager, DHT22Accessor dht22Accessor)
        {
            var hsrel5Stairway = ccToolsController.CreateHSREL5(Device.StairwayHSREL5, new I2CSlaveAddress(60));
            var hspe8UpperFloor = (HSPE8)ioBoardManager.GetOutputBoard(Device.UpperFloorAndOfficeHSPE8);
            var hspe16FloorAndLowerBathroom = ccToolsController.CreateHSPE16OutputOnly(Device.LowerFloorAndLowerBathroomHSPE16, new I2CSlaveAddress(17));

            var input1 = ioBoardManager.GetInputBoard(Device.Input1);
            var input2 = ioBoardManager.GetInputBoard(Device.Input2);
            var input4 = ioBoardManager.GetInputBoard(Device.Input4);

            const int SensorPin = 5;

            var floor = home.AddRoom(Room.Floor)
                .WithMotionDetector(Floor.StairwayMotionDetector, input2.GetInput(1))
                .WithMotionDetector(Floor.StairsLowerMotionDetector, input4.GetInput(7))
                .WithMotionDetector(Floor.StairsUpperMotionDetector, input4.GetInput(6))
                .WithMotionDetector(Floor.LowerFloorMotionDetector, input1.GetInput(4))
                .WithTemperatureSensor(Floor.LowerFloorTemperatureSensor, dht22Accessor.GetTemperatureSensor(SensorPin))
                .WithHumiditySensor(Floor.LowerFloorHumiditySensor, dht22Accessor.GetHumiditySensor(SensorPin))
                .WithLamp(Floor.Lamp1, hspe16FloorAndLowerBathroom.GetOutput(5).WithInvertedState())
                .WithLamp(Floor.Lamp2, hspe16FloorAndLowerBathroom.GetOutput(6).WithInvertedState())
                .WithLamp(Floor.Lamp3, hspe16FloorAndLowerBathroom.GetOutput(7).WithInvertedState())
                .WithLamp(Floor.StairwayLampCeiling, hsrel5Stairway.GetOutput(0))
                .WithLamp(Floor.StairwayLampWall, hsrel5Stairway.GetOutput(1))
                .WithRollerShutter(Floor.StairwayRollerShutter, hsrel5Stairway.GetOutput(4), hsrel5Stairway.GetOutput(3), RollerShutter.DefaultMaxMovingDuration, 20000)
                .WithButton(Floor.ButtonLowerFloorUpper, input1.GetInput(0))
                .WithButton(Floor.ButtonLowerFloorLower, input1.GetInput(5))
                .WithButton(Floor.ButtonLowerFloorAtBathroom, input1.GetInput(1))
                .WithButton(Floor.ButtonLowerFloorAtKitchen, input1.GetInput(3))
                .WithButton(Floor.ButtonStairsLowerUpper, input4.GetInput(5))
                .WithButton(Floor.ButtonStairsLowerLower, input1.GetInput(2))
                .WithButton(Floor.ButtonStairsUpper, input4.GetInput(4))
                .WithButton(Floor.ButtonStairway, input1.GetInput(6));

            floor.CombineActuators(Floor.CombinedStairwayLamp)
                .WithActuator(floor.Lamp(Floor.StairwayLampCeiling))
                .WithActuator(floor.Lamp(Floor.StairwayLampWall));

            floor.SetupAutomaticTurnOnAndOffAction()
                .WithTrigger(floor.MotionDetector(Floor.StairwayMotionDetector))
                .WithTrigger(floor.Button(Floor.ButtonStairway))
                .WithTarget(floor.BinaryStateOutput(Floor.CombinedStairwayLamp))
                .WithEnabledAtNight(home.WeatherStation)
                .WithOnDuration(TimeSpan.FromSeconds(30));

            floor.CombineActuators(Floor.CombinedLamps)
                .WithActuator(floor.Lamp(Floor.Lamp1))
                .WithActuator(floor.Lamp(Floor.Lamp2))
                .WithActuator(floor.Lamp(Floor.Lamp3));

            floor.SetupAutomaticTurnOnAndOffAction()
                .WithTrigger(floor.MotionDetector(Floor.LowerFloorMotionDetector))
                .WithTrigger(floor.Button(Floor.ButtonLowerFloorUpper))
                .WithTrigger(floor.Button(Floor.ButtonLowerFloorAtBathroom))
                .WithTrigger(floor.Button(Floor.ButtonLowerFloorAtKitchen))
                .WithTarget(floor.BinaryStateOutput(Floor.CombinedLamps))
                .WithEnabledAtNight(home.WeatherStation)
                .WithTurnOffIfButtonPressedWhileAlreadyOn()
                .WithOnDuration(TimeSpan.FromSeconds(20));

            SetupStairsCeilingLamps(floor, hspe8UpperFloor);
            SetupStairsLamps(floor, home.WeatherStation, hspe16FloorAndLowerBathroom);
            
            floor.SetupAutomaticRollerShutters().WithRollerShutters(floor.RollerShutter(Floor.StairwayRollerShutter));
        }

        private void SetupStairsCeilingLamps(Actuators.Room floor, HSPE8 hspe8UpperFloor)
        {
            var output = new LogicalBinaryOutput()
                .WithOutput(hspe8UpperFloor[HSPE8Pin.GPIO4])
                .WithOutput(hspe8UpperFloor[HSPE8Pin.GPIO5])
                .WithOutput(hspe8UpperFloor[HSPE8Pin.GPIO7])
                .WithOutput(hspe8UpperFloor[HSPE8Pin.GPIO6])
                .WithInvertedState();

            floor.WithLamp(Floor.LampStairsCeiling, output);

            floor.SetupAutomaticTurnOnAndOffAction()
                .WithTrigger(floor.MotionDetector(Floor.StairsLowerMotionDetector), new AnimateParameter())
                .WithTrigger(floor.MotionDetector(Floor.StairsUpperMotionDetector))
                //.WithTrigger(floor.Button(Floor.ButtonStairsUpper))
                .WithTarget(floor.BinaryStateOutput(Floor.LampStairsCeiling))
                .WithOnDuration(TimeSpan.FromSeconds(10));

            var lamp = floor.Lamp(Floor.LampStairsCeiling);

            floor.Button(Floor.ButtonStairsUpper).PressedShort += (s, e) =>
            {
                if (lamp.GetState() == BinaryActuatorState.On)
                {
                    lamp.TurnOff(new AnimateParameter().WithReversedOrder());
                }
                else
                {
                    lamp.TurnOn(new AnimateParameter());
                }
            };

            floor.Button(Floor.ButtonStairsLowerUpper).PressedShort += (s, e) =>
            {
                if (lamp.GetState() == BinaryActuatorState.On)
                {
                    lamp.TurnOff(new AnimateParameter());
                }
                else
                {
                    lamp.TurnOn(new AnimateParameter().WithReversedOrder());
                }
            };
        }

        private void SetupStairsLamps(Actuators.Room floor, IWeatherStation weatherStation, HSPE16OutputOnly hspe16FloorAndLowerBathroom)
        {
            var output = new LogicalBinaryOutput()
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO8])
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO9])
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO10])
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO11])
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO13])
                .WithOutput(hspe16FloorAndLowerBathroom[HSPE16Pin.GPIO12])
                .WithInvertedState();

            floor.WithLamp(Floor.LampStairs, output);

            floor.SetupAlwaysOn()
                .WithActuator(floor.Lamp(Floor.LampStairs))
                .WithOnAtNightRange(weatherStation)
                .WithOffBetweenRange(TimeSpan.FromHours(23), TimeSpan.FromHours(4));
        }
    }
}