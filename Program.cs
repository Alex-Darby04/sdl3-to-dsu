/* 
BUGS:
Re-connection doesn't work - if controller is disconnected, SDL doesnt try to reconnect it?

*/
using SDL3;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

GamepadTest gamepad = new GamepadTest();

DSUserver dsuServer = new DSUserver();

_ = Task.Run(() => dsuServer.CreateClient());

gamepad.MotionUpdated += dsuServer.OnMotionUpdated;


gamepad.StartUp();

public class GamepadTest
{

    public struct MotionData
    {
        public float AccelX, AccelY, AccelZ;
        public float GyroX, GyroY, GyroZ;
        public ulong Timestamp;
    }


    private IntPtr _physicalGamepad = IntPtr.Zero;
    private uint _activeInstanceId = 0;
    private IXbox360Controller _virtualXbox;

    private float[] _gyro = new float[3];
    private float[] _accel = new float[3];
    private bool _hasGyro = false;
    private bool _hasAccel = false;
    public event Action<MotionData> MotionUpdated;
    
    private static readonly Dictionary<SDL.GamepadButton, Xbox360Button> _buttonMap = new()
    {
        // Face Buttons
        [SDL.GamepadButton.South] = Xbox360Button.A,
        [SDL.GamepadButton.East] = Xbox360Button.B,
        [SDL.GamepadButton.West] = Xbox360Button.X,
        [SDL.GamepadButton.North] = Xbox360Button.Y,
        // Bumpers
        [SDL.GamepadButton.LeftShoulder] = Xbox360Button.LeftShoulder,
        [SDL.GamepadButton.RightShoulder] = Xbox360Button.RightShoulder,
        // Thumbsticks
        [SDL.GamepadButton.LeftStick] = Xbox360Button.LeftThumb,
        [SDL.GamepadButton.RightStick] = Xbox360Button.RightThumb,
        //Dpad
        [SDL.GamepadButton.DPadDown] = Xbox360Button.Down,
        [SDL.GamepadButton.DPadLeft] = Xbox360Button.Left,
        [SDL.GamepadButton.DPadRight] = Xbox360Button.Right,
        [SDL.GamepadButton.DPadUp] = Xbox360Button.Up,
        //Menu
        [SDL.GamepadButton.Back] = Xbox360Button.Back,
        [SDL.GamepadButton.Guide] = Xbox360Button.Guide,
        [SDL.GamepadButton.Start] = Xbox360Button.Start,
        //Paddles (if supported)
        [SDL.GamepadButton.LeftPaddle1] = Xbox360Button.A,
        [SDL.GamepadButton.RightPaddle1] = Xbox360Button.B,
        [SDL.GamepadButton.LeftPaddle2] = Xbox360Button.X,
        [SDL.GamepadButton.RightPaddle2] = Xbox360Button.Y,

        
    };
    public void StartUp()
    {
        ViGEmClient client = new ViGEmClient();

        _virtualXbox = client.CreateXbox360Controller();
        
        _virtualXbox.Connect();

        if (SDL.Init(SDL.InitFlags.Gamepad) == false)
        {
            Console.WriteLine($"Failed to init SDL: {SDL.GetError()}");
            return;
        }

        bool running = true;

        // 2. The Main Event Loop
        while (running)
        {
            // Pump events
            while (SDL.WaitEvent(out SDL.Event e))
            {
                switch ((SDL.EventType)e.Type)
                { 
                    case SDL.EventType.Quit:
                        running = false;
                        if (_physicalGamepad != IntPtr.Zero)
                        {
                            _virtualXbox.FeedbackReceived -= OnVirtualXboxFeedback;
                            SDL.CloseGamepad(_physicalGamepad);
                        }
                        _virtualXbox.Disconnect();
                        break;

                    // --- CONNECTION EVENTS ---
                    case SDL.EventType.GamepadAdded:
                        uint instanceId = e.GDevice.Which;
                        
                        if(_physicalGamepad != IntPtr.Zero)
                        {
                            IntPtr temp = SDL.OpenGamepad(instanceId);
                            if (temp != IntPtr.Zero) SDL.CloseGamepad(temp);
                            break;
                        }

                        IntPtr newGamepad = SDL.OpenGamepad(instanceId);
  
                        if (newGamepad != IntPtr.Zero)
                        {
                            string name = SDL.GetGamepadName(newGamepad);
                           
                            if (name.Contains("Nintendo") || name.Contains("Pro Controller"))
                            {
                                _physicalGamepad = newGamepad;
                                _activeInstanceId = instanceId;
                                SDL.SetGamepadSensorEnabled(_physicalGamepad, SDL.SensorType.Gyro, true);
                                SDL.SetGamepadSensorEnabled(_physicalGamepad, SDL.SensorType.Accel, true);
                                _virtualXbox.FeedbackReceived += OnVirtualXboxFeedback;
                            }
                            else
                            {
                                SDL.CloseGamepad(newGamepad);
                            }
                        }
                        break;

                    case SDL.EventType.GamepadRemoved:
                        uint removedId = e.GDevice.Which;
                        if (_physicalGamepad != IntPtr.Zero && removedId == _activeInstanceId)
                        {
                            _virtualXbox.FeedbackReceived -= OnVirtualXboxFeedback;

                            _virtualXbox.ResetReport();
                            _virtualXbox.SubmitReport();
                            SDL.CloseGamepad(_physicalGamepad);

                            _physicalGamepad = IntPtr.Zero;
                            _activeInstanceId = 0;
                        }
                        break;

                    case SDL.EventType.GamepadButtonDown:
                    case SDL.EventType.GamepadButtonUp:
                        bool isPressed = (e.GButton.Down);

                        var button = (SDL.GamepadButton) e.GButton.Button;

                        if(_buttonMap.TryGetValue(button, out var xboxButton)) _virtualXbox.SetButtonState(xboxButton, isPressed);

                        _virtualXbox.SubmitReport();
                        break;

                    case SDL.EventType.GamepadAxisMotion:
                        int axisValue = e.GAxis.Value;

                        var axisType =(SDL.GamepadAxis) e.GAxis.Axis;

                        switch (axisType)
                        {
                            case SDL.GamepadAxis.LeftTrigger: 
                                byte leftTriggerBytes = (byte)(axisValue / 128);
                                _virtualXbox.SetSliderValue(Xbox360Slider.LeftTrigger, leftTriggerBytes);
                                break;
                            case SDL.GamepadAxis.RightTrigger: 
                                byte rightTriggerBytes = (byte)(axisValue / 128);
                                _virtualXbox.SetSliderValue(Xbox360Slider.RightTrigger, rightTriggerBytes);
                                break;
                            case SDL.GamepadAxis.LeftX: _virtualXbox.SetAxisValue(Xbox360Axis.LeftThumbX, (short) axisValue); break;
                            case SDL.GamepadAxis.LeftY: _virtualXbox.SetAxisValue(Xbox360Axis.LeftThumbY, (short) (axisValue == -32768 ? 32767 : -axisValue)); break;
                            case SDL.GamepadAxis.RightX: _virtualXbox.SetAxisValue(Xbox360Axis.RightThumbX, (short) axisValue); break;
                            case SDL.GamepadAxis.RightY: _virtualXbox.SetAxisValue(Xbox360Axis.RightThumbY, (short) (axisValue == -32768 ? 32767 : -axisValue)); break;
                        }                      

                        _virtualXbox.SubmitReport();
                        break;
                    case SDL.EventType.GamepadSensorUpdate:
                        unsafe
                        {
                            var sensorData = e.GSensor.Data; 
                            if((SDL.SensorType) e.GSensor.Sensor == SDL.SensorType.Gyro)
                            {
                                _gyro[0] = sensorData[0];
                                _gyro[1] = sensorData[1];
                                _gyro[2] = sensorData[2];
                                _hasGyro = true;

                            }
                            else if((SDL.SensorType) e.GSensor.Sensor == SDL.SensorType.Accel)
                            {
                                _accel[0] = sensorData[0];
                                _accel[1] = sensorData[1];
                                _accel[2] = sensorData[2];
                                _hasAccel = true;

                            }
                            if (_hasGyro && _hasAccel)
                            {
                                MotionUpdated?.Invoke(new MotionData
                                {
                                    GyroX = _gyro[0], GyroY = _gyro[1], GyroZ = _gyro[2],
                                    AccelX = _accel[0], AccelY = _accel[1], AccelZ = _accel[2],
                                    Timestamp = e.GSensor.SensorTimestamp
                                });
                                _hasGyro = false;
                                _hasAccel = false;
                            }
                        }                      
                        break;
                    
                }
            }
        }

        SDL.CloseGamepad(_physicalGamepad);
        
        SDL.Quit();
    }

        private void OnVirtualXboxFeedback(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        byte vigemLargeMotor = e.LargeMotor;
        byte vigemSmallMotor = e.SmallMotor;

        ushort sdlLowFreq = (ushort)(vigemLargeMotor * 257);
        ushort sdlHighFreq = (ushort)(vigemSmallMotor * 257);

        uint durationMs = 0xFFFF;

        SDL.RumbleGamepad(_physicalGamepad, sdlLowFreq, sdlHighFreq, durationMs);
    }
}



