using System;
using System.ComponentModel;
using System.Drawing;
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace OpenTK
{
    public class FrameEventArgs
    {
        public double Time { get; set; }
    }

    public enum WindowState
    {
        Normal,
        Minimized,
        Maximized,
        Fullscreen
    }

    public enum WindowBorder
    {
        Resizable,
        Fixed,
        Hidden
    }

    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        public static readonly int SizeInBytes = 8;
    }

    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly int SizeInBytes = 12;
    }

    public struct Vector4
    {
        public float X, Y, Z, W;
        public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly int SizeInBytes = 16;
    }

    public struct Matrix4
    {
        public float M11, M12, M13, M14;
        public float M21, M22, M23, M24;
        public float M31, M32, M33, M34;
        public float M41, M42, M43, M44;

        public Matrix4(
            float m00, float m01, float m02, float m03,
            float m10, float m11, float m12, float m13,
            float m20, float m21, float m22, float m23,
            float m30, float m31, float m32, float m33)
        {
            M11 = m00; M12 = m01; M13 = m02; M14 = m03;
            M21 = m10; M22 = m11; M23 = m12; M24 = m13;
            M31 = m20; M32 = m21; M33 = m22; M34 = m23;
            M41 = m30; M42 = m31; M43 = m32; M44 = m33;
        }

        /// <summary>Multiplies two Matrix4 values (row-major, post-multiply convention).
        /// Note: this struct uses M[r][c] naming where M11 = row 1 col 1, etc.
        /// Added for the RealSky compute pass which needs inverse(view * projection).
        /// </summary>
        public static Matrix4 operator *(Matrix4 left, Matrix4 right)
        {
            return new Matrix4(
                left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31 + left.M14 * right.M41,
                left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32 + left.M14 * right.M42,
                left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33 + left.M14 * right.M43,
                left.M11 * right.M14 + left.M12 * right.M24 + left.M13 * right.M34 + left.M14 * right.M44,

                left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31 + left.M24 * right.M41,
                left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32 + left.M24 * right.M42,
                left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33 + left.M24 * right.M43,
                left.M21 * right.M14 + left.M22 * right.M24 + left.M23 * right.M34 + left.M24 * right.M44,

                left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31 + left.M34 * right.M41,
                left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32 + left.M34 * right.M42,
                left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33 + left.M34 * right.M43,
                left.M31 * right.M14 + left.M32 * right.M24 + left.M33 * right.M34 + left.M34 * right.M44,

                left.M41 * right.M11 + left.M42 * right.M21 + left.M43 * right.M31 + left.M44 * right.M41,
                left.M41 * right.M12 + left.M42 * right.M22 + left.M43 * right.M32 + left.M44 * right.M42,
                left.M41 * right.M13 + left.M42 * right.M23 + left.M43 * right.M33 + left.M44 * right.M43,
                left.M41 * right.M14 + left.M42 * right.M24 + left.M43 * right.M34 + left.M44 * right.M44
            );
        }

        /// <summary>
        /// Computes the inverse of a 4x4 matrix using cofactor expansion.
        /// Returns identity if the matrix is singular.
        /// Added for the RealSky compute pass which needs inverse(view * projection).
        /// </summary>
        public static void Invert(ref Matrix4 m, out Matrix4 result)
        {
            // 4x4 matrix inverse via cofactors / adjugate.
            // Uses the existing M[r][c] field names (M11 = row 1 col 1).
            // Source: standard 4x4 inverse derivation, matches OpenTK 3.x semantics.
            // First, remap the row-major M11..M44 fields to canonical m00..m33 names
            // so the cofactor algebra below reads cleanly.
            float m00 = m.M11, m01 = m.M12, m02 = m.M13, m03 = m.M14;
            float m10 = m.M21, m11 = m.M22, m12 = m.M23, m13 = m.M24;
            float m20 = m.M31, m21 = m.M32, m22 = m.M33, m23 = m.M34;
            float m30 = m.M41, m31 = m.M42, m32 = m.M43, m33 = m.M44;

            float b00 = m00 * m11 - m01 * m10;
            float b01 = m00 * m12 - m02 * m10;
            float b02 = m00 * m13 - m03 * m10;
            float b03 = m01 * m12 - m02 * m11;
            float b04 = m01 * m13 - m03 * m11;
            float b05 = m02 * m13 - m03 * m12;
            float b06 = m20 * m31 - m21 * m30;
            float b07 = m20 * m32 - m22 * m30;
            float b08 = m20 * m33 - m23 * m30;
            float b09 = m21 * m32 - m22 * m31;
            float b10 = m21 * m33 - m23 * m31;
            float b11 = m22 * m33 - m23 * m32;

            float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;

            if (System.Math.Abs(det) < 1e-12f)
            {
                result = new Matrix4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                return;
            }

            float invDet = 1.0f / det;

            result = new Matrix4(
                ( m11 * b11 - m12 * b10 + m13 * b09) * invDet,
                (-m01 * b11 + m02 * b10 - m03 * b09) * invDet,
                ( m31 * b05 - m32 * b04 + m33 * b03) * invDet,
                (-m21 * b05 + m22 * b04 - m23 * b03) * invDet,
                (-m10 * b11 + m12 * b08 - m13 * b07) * invDet,
                ( m00 * b11 - m02 * b08 + m03 * b07) * invDet,
                (-m30 * b05 + m32 * b02 - m33 * b01) * invDet,
                ( m20 * b05 - m22 * b02 + m23 * b01) * invDet,
                ( m10 * b10 - m11 * b08 + m13 * b06) * invDet,
                (-m00 * b10 + m01 * b08 - m03 * b06) * invDet,
                ( m30 * b04 - m31 * b02 + m33 * b00) * invDet,
                (-m20 * b04 + m21 * b02 - m23 * b00) * invDet,
                (-m10 * b09 + m11 * b07 - m12 * b06) * invDet,
                ( m00 * b09 - m01 * b07 + m02 * b06) * invDet,
                (-m30 * b03 + m31 * b01 - m32 * b00) * invDet,
                ( m20 * b03 - m21 * b01 + m22 * b00) * invDet
            );
        }
    }

    public class DisplayResolution
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public DisplayResolution(int w, int h) { Width = w; Height = h; }
    }

    public class DisplayDevice
    {
        public static DisplayDevice Default = new DisplayDevice();
        public int Width => 1920;
        public int Height => 1080;
        public OpenBveApi.Math.Vector2 ScaleFactor => OpenBveApi.Math.Vector2.One;
        public System.Collections.Generic.IList<DisplayResolution> AvailableResolutions { get; } = new System.Collections.Generic.List<DisplayResolution>
        {
            new DisplayResolution(800, 600),
            new DisplayResolution(1024, 768),
            new DisplayResolution(1280, 720),
            new DisplayResolution(1280, 800),
            new DisplayResolution(1280, 1024),
            new DisplayResolution(1366, 768),
            new DisplayResolution(1440, 900),
            new DisplayResolution(1600, 900),
            new DisplayResolution(1680, 1050),
            new DisplayResolution(1920, 1080),
            new DisplayResolution(2560, 1440),
            new DisplayResolution(3840, 2160)
        };
        public void RestoreResolution() { }
        public void ChangeResolution(DisplayResolution res) { }
    }

    public enum GameWindowFlags
    {
        Default
    }

    public static class Toolkit
    {
        public static void Init(ToolkitOptions options) { }
    }

    public class ToolkitOptions
    {
        public PlatformBackend Backend { get; set; }
        public bool EnableHighResolution { get; set; }
    }

    public enum PlatformBackend
    {
        PreferX11
    }

    public class MouseCursor
    {
        public static MouseCursor Default = new MouseCursor();
        public MouseCursor() { }
        public MouseCursor(int x, int y, int w, int h, IntPtr data) { }
    }

    // The GameWindow wrapper around Silk.NET IWindow
    public class GameWindow : IDisposable
    {
        protected IWindow window;
        private IInputContext inputContext;

        public class GameWindowContext
        {
            public bool IsCurrent => true;
        }
        public GameWindowContext Context { get; } = new GameWindowContext();

        public int Width { get => window.Size.X; set => window.Size = new Silk.NET.Maths.Vector2D<int>(value, window.Size.Y); }
        public int Height { get => window.Size.Y; set => window.Size = new Silk.NET.Maths.Vector2D<int>(window.Size.X, value); }
        public int X { get => window.Position.X; set => window.Position = new Silk.NET.Maths.Vector2D<int>(value, window.Position.Y); }
        public int Y { get => window.Position.Y; set => window.Position = new Silk.NET.Maths.Vector2D<int>(window.Position.X, value); }
        public MouseCursor Cursor { get; set; }
        public Icon Icon { get; set; }
        public string Title { get => window.Title; set => window.Title = value; }
        public bool Visible { get => window.IsVisible; set => window.IsVisible = value; }
        public double TargetUpdateFrequency { get => window.UpdatesPerSecond; set => window.UpdatesPerSecond = value; }
        public double TargetRenderFrequency { get => window.FramesPerSecond; set => window.FramesPerSecond = value; }
        public bool CursorVisible { get; set; } = true;
        public bool IsExiting => false;
        public void ProcessEvents() { }

        public WindowState WindowState
        {
            get => (WindowState)window.WindowState;
            set => window.WindowState = (Silk.NET.Windowing.WindowState)value;
        }

        public WindowBorder WindowBorder
        {
            get => (WindowBorder)window.WindowBorder;
            set => window.WindowBorder = (Silk.NET.Windowing.WindowBorder)value;
        }

        public Rectangle Bounds
        {
            get => new Rectangle(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y);
            set
            {
                window.Position = new Silk.NET.Maths.Vector2D<int>(value.X, value.Y);
                window.Size = new Silk.NET.Maths.Vector2D<int>(value.Width, value.Height);
            }
        }

        public double RenderFrequency => 60.0; // fallback

        // Input Events
        public event EventHandler<CancelEventArgs> Closing;
        public event EventHandler<OpenTK.Input.KeyboardKeyEventArgs> KeyDown;
        public event EventHandler<OpenTK.Input.KeyboardKeyEventArgs> KeyUp;
        public event EventHandler<OpenTK.Input.MouseButtonEventArgs> MouseDown;
        public event EventHandler<OpenTK.Input.MouseButtonEventArgs> MouseUp;
        public event EventHandler<OpenTK.Input.MouseMoveEventArgs> MouseMove;
        public event EventHandler<OpenTK.Input.MouseWheelEventArgs> MouseWheel;
        public event EventHandler<OpenTK.Input.FileDropEventArgs> FileDrop;

        public GameWindow(int width, int height, OpenTK.Graphics.GraphicsMode mode, string title, GameWindowFlags flags)
        {
            var options = WindowOptions.Default;
            options.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
            options.Title = title;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Compatability, ContextFlags.Default, new APIVersion(3, 3));
            window = Window.Create(options);
            SetupWindowEvents();
        }

        public GameWindow(int width, int height, OpenTK.Graphics.GraphicsMode mode, string title, GameWindowFlags flags, DisplayDevice device, int major, int minor, OpenTK.Graphics.GraphicsContextFlags contextFlags)
        {
            var options = WindowOptions.Default;
            options.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
            options.Title = title;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Compatability, ContextFlags.Default, new APIVersion(major, minor));
            window = Window.Create(options);
            SetupWindowEvents();
        }

        private void SetupWindowEvents()
        {
            window.Load += () =>
            {
                LibRender2.OpenGLBind.GL = global::Silk.NET.OpenGL.Legacy.GL.GetApi(window);
                inputContext = window.CreateInput();
                SetupInput();
                OnLoad(EventArgs.Empty);
            };

            window.Update += (dt) =>
            {
                OnUpdateFrame(new FrameEventArgs { Time = dt });
            };

            window.Render += (dt) =>
            {
                OnRenderFrame(new FrameEventArgs { Time = dt });
            };

            window.Resize += (size) =>
            {
                OnResize(EventArgs.Empty);
            };

            window.Closing += () =>
            {
                var cancelArgs = new CancelEventArgs();
                Closing?.Invoke(this, cancelArgs);
                OnClosing(cancelArgs);
                if (!cancelArgs.Cancel)
                {
                    OnUnload(EventArgs.Empty);
                }
            };
        }

        private void SetupInput()
        {
            foreach (var keyboard in inputContext.Keyboards)
            {
                keyboard.KeyDown += (kb, key, code) =>
                {
                    var tkKey = MapKey(key);
                    bool shift = kb.IsKeyPressed(Silk.NET.Input.Key.LShift) || kb.IsKeyPressed(Silk.NET.Input.Key.RShift);
                    bool control = kb.IsKeyPressed(Silk.NET.Input.Key.LControl) || kb.IsKeyPressed(Silk.NET.Input.Key.RControl);
                    bool alt = kb.IsKeyPressed(Silk.NET.Input.Key.LAlt) || kb.IsKeyPressed(Silk.NET.Input.Key.RAlt);
                    KeyDown?.Invoke(this, new OpenTK.Input.KeyboardKeyEventArgs { Key = tkKey, Shift = shift, Control = control, Alt = alt });
                };
                keyboard.KeyUp += (kb, key, code) =>
                {
                    var tkKey = MapKey(key);
                    bool shift = kb.IsKeyPressed(Silk.NET.Input.Key.LShift) || kb.IsKeyPressed(Silk.NET.Input.Key.RShift);
                    bool control = kb.IsKeyPressed(Silk.NET.Input.Key.LControl) || kb.IsKeyPressed(Silk.NET.Input.Key.RControl);
                    bool alt = kb.IsKeyPressed(Silk.NET.Input.Key.LAlt) || kb.IsKeyPressed(Silk.NET.Input.Key.RAlt);
                    KeyUp?.Invoke(this, new OpenTK.Input.KeyboardKeyEventArgs { Key = tkKey, Shift = shift, Control = control, Alt = alt });
                };
            }

            foreach (var mouse in inputContext.Mice)
            {
                mouse.MouseDown += (m, button) =>
                {
                    var tkButton = MapMouseButton(button);
                    var state = new OpenTK.Input.MouseState();
                    state.LeftButton = button == MouseButton.Left ? OpenTK.Input.ButtonState.Pressed : OpenTK.Input.ButtonState.Released;
                    state.RightButton = button == MouseButton.Right ? OpenTK.Input.ButtonState.Pressed : OpenTK.Input.ButtonState.Released;
                    MouseDown?.Invoke(this, new OpenTK.Input.MouseButtonEventArgs
                    {
                        Button = tkButton,
                        IsPressed = true,
                        X = (int)m.Position.X,
                        Y = (int)m.Position.Y,
                        Mouse = state
                    });
                };
                mouse.MouseUp += (m, button) =>
                {
                    var tkButton = MapMouseButton(button);
                    var state = new OpenTK.Input.MouseState();
                    state.LeftButton = button == MouseButton.Left ? OpenTK.Input.ButtonState.Released : OpenTK.Input.ButtonState.Pressed;
                    state.RightButton = button == MouseButton.Right ? OpenTK.Input.ButtonState.Released : OpenTK.Input.ButtonState.Pressed;
                    MouseUp?.Invoke(this, new OpenTK.Input.MouseButtonEventArgs
                    {
                        Button = tkButton,
                        IsPressed = false,
                        X = (int)m.Position.X,
                        Y = (int)m.Position.Y,
                        Mouse = state
                    });
                };
                mouse.MouseMove += (m, pos) =>
                {
                    MouseMove?.Invoke(this, new OpenTK.Input.MouseMoveEventArgs
                    {
                        X = (int)pos.X,
                        Y = (int)pos.Y
                    });
                };
                mouse.Scroll += (m, wheel) =>
                {
                    MouseWheel?.Invoke(this, new OpenTK.Input.MouseWheelEventArgs
                    {
                        Delta = (int)wheel.Y
                    });
                };
            }
        }

        private OpenTK.Input.Key MapKey(Key key)
        {
            return (OpenTK.Input.Key)key;
        }

        private OpenTK.Input.MouseButton MapMouseButton(MouseButton button)
        {
            return (OpenTK.Input.MouseButton)button;
        }

        public void Run() => window.Run();
        public void SwapBuffers() => window.GLContext?.SwapBuffers();
        public void Close() => window.Close();
        public void Exit() => window.Close();

        protected virtual void OnLoad(EventArgs e) { }
        protected virtual void OnRenderFrame(FrameEventArgs e) { }
        protected virtual void OnUpdateFrame(FrameEventArgs e) { }
        protected virtual void OnResize(EventArgs e) { }
        protected virtual void OnClosing(CancelEventArgs e) { }
        protected virtual void OnUnload(EventArgs e) { }
        protected virtual void OnMouseMove(OpenTK.Input.MouseMoveEventArgs e) { }

        public void Dispose()
        {
            inputContext?.Dispose();
            window?.Dispose();
        }
    }
}

namespace OpenTK.Graphics
{
    public class GraphicsMode
    {
        public GraphicsMode(ColorFormat format, int depth, int stencil, int samples) { }
    }

    public class ColorFormat
    {
        public ColorFormat(int r, int g, int b, int a) { }
    }

    public enum GraphicsContextFlags
    {
        Default
    }
}

namespace OpenTK.Input
{
    public enum ButtonState
    {
        Released,
        Pressed
    }

    public struct MouseState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public ButtonState LeftButton { get; set; }
        public ButtonState RightButton { get; set; }
        public static bool operator ==(MouseState left, MouseState right) => left.X == right.X && left.Y == right.Y && left.LeftButton == right.LeftButton && left.RightButton == right.RightButton;
        public static bool operator !=(MouseState left, MouseState right) => !(left == right);
        public override bool Equals(object obj) => obj is MouseState other && this == other;
        public override int GetHashCode() => HashCode.Combine(X, Y, LeftButton, RightButton);
    }

    public static class Mouse
    {
        public static MouseState GetState() => new MouseState();
    }

    public enum MouseButton
    {
        Left,
        Right,
        Middle,
        Button1,
        Button2,
        Button3
    }

    public class MouseButtonEventArgs : EventArgs
    {
        public MouseButton Button { get; set; }
        public bool IsPressed { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public MouseState Mouse { get; set; }
    }

    public class MouseMoveEventArgs : EventArgs
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class MouseWheelEventArgs : EventArgs
    {
        public int Delta { get; set; }
    }

    public class KeyboardKeyEventArgs : EventArgs
    {
        public Key Key { get; set; }
        public bool Shift { get; set; }
        public bool Control { get; set; }
        public bool Alt { get; set; }
    }

    public class FileDropEventArgs : EventArgs
    {
        public string FileName { get; set; }
    }

    // Direct mapping of key values matching Silk.NET/OpenTK layout
    public enum Key
    {
        Unknown = 0,
        LShift = 43,
        RShift = 47,
        LControl = 53,
        RControl = 57,
        LAlt = 61,
        RAlt = 65,
        F1 = 79,
        F2 = 80,
        F3 = 81,
        F4 = 82,
        F5 = 83,
        F6 = 84,
        F7 = 85,
        F8 = 86,
        F9 = 87,
        F10 = 88,
        F11 = 89,
        F12 = 90,
        Up = 150,
        Down = 151,
        Left = 152,
        Right = 153,
        Enter = 159,
        Escape = 161,
        Space = 163,
        BackSpace = 168,
        Delete = 173,
        PageUp = 175,
        PageDown = 176,
        Keypad0 = 233,
        Keypad1 = 234,
        Keypad2 = 235,
        Keypad3 = 236,
        Keypad4 = 237,
        Keypad5 = 238,
        Keypad6 = 239,
        Keypad7 = 240,
        Keypad8 = 241,
        Keypad9 = 242,
        KeypadDivide = 253,
        KeypadMultiply = 254,
        KeypadSubtract = 257,
        KeypadAdd = 261,
        KeypadDecimal = 265,
        KeypadEnter = 269,
        A = 273,
        B = 274,
        C = 275,
        D = 276,
        E = 277,
        F = 278,
        G = 279,
        H = 280,
        I = 281,
        J = 282,
        K = 283,
        L = 284,
        M = 285,
        N = 286,
        O = 287,
        P = 288,
        Q = 289,
        R = 290,
        S = 291,
        T = 292,
        U = 293,
        V = 294,
        W = 295,
        X = 296,
        Y = 297,
        Z = 298,
        ShiftLeft = LShift,
        ShiftRight = RShift,
        ControlLeft = LControl,
        ControlRight = RControl,
        KeypadPeriod = KeypadDecimal,
        Plus = 356,
        Minus = 353,
        KeypadPlus = KeypadAdd,
        KeypadMinus = KeypadSubtract,
        Number0 = 327,
        Number1 = 328,
        Number2 = 329,
        Number3 = 330,
        Number4 = 331,
        Number5 = 332,
        Number6 = 333,
        Number7 = 334,
        Number8 = 335,
        Number9 = 336,
        Period = 371
    }
}

namespace OpenTK.Graphics.OpenGL
{
    public enum MatrixMode : int
    {
        Modelview = 0x1700,
        Projection = 0x1701
    }

    public enum BlendingFactor : int
    {
        SrcAlpha = 0x0302,
        OneMinusSrcAlpha = 0x0303,
        One = 1,
        Zero = 0,
        OneMinusSrcColor = 0x0301
    }

    public enum AlphaFunction : int
    {
        Never = 0x0200,
        Less = 0x0201,
        Equal = 0x0202,
        Lequal = 0x0203,
        Greater = 0x0204,
        Notequal = 0x0205,
        Gequal = 0x0206,
        Always = 0x0207
    }

    public enum ShaderType : int
    {
        FragmentShader = 0x8B30,
        VertexShader = 0x8B31,
        ComputeShader = 0x91B9
    }

    public enum ErrorCode : int
    {
        NoError = 0,
        InvalidEnum = 0x0500,
        InvalidValue = 0x0501,
        InvalidOperation = 0x0502,
        StackOverflow = 0x0503,
        StackUnderflow = 0x0504,
        OutOfMemory = 0x0505,
        TableTooLargeExt = 0x8031
    }

    public enum PixelInternalFormat : int
    {
        Rgba = 0x8058,
        DepthComponent16 = 0x81A5,
        R8 = 0x8229,
        Luminance = 0x1909,
        Rgb8 = 0x8051,
        Rgba8 = 0x8058,
        LuminanceAlpha = 0x190A,
        DepthComponent24 = 0x81A6,
        R32ui = 0x8236,
        Rgb = 0x1907,
        Rgba16f = 0x881A
    }

    public enum PixelFormat : int
    {
        Rgba = 0x1908,
        DepthComponent = 0x1902,
        Red = 0x1903,
        Luminance = 0x1909,
        Rgb = 0x1907,
        LuminanceAlpha = 0x190A,
        RedInteger = 0x8D94
    }

    public enum PixelType : int
    {
        UnsignedByte = 0x1401,
        Float = 0x1406,
        UnsignedInt = 0x1405
    }

    public enum RenderbufferStorage : int
    {
        DepthComponent24 = 0x81A6
    }

    public enum DrawBuffersEnum : int
    {
        ColorAttachment0 = 0x8CE0
    }

    public enum PrimitiveType : int
    {
        Points = 0x0000,
        Lines = 0x0001,
        LineLoop = 0x0002,
        LineStrip = 0x0003,
        Triangles = 0x0004,
        TriangleStrip = 0x0005,
        TriangleFan = 0x0006,
        Quads = 0x0007,
        QuadStrip = 0x0008,
        Polygon = 0x0009
    }

    public enum EnableCap : int
    {
        DepthClamp = 0x864F,
        DepthTest = 0x0B71,
        Blend = 0x0BE2,
        CullFace = 0x0B44,
        Dither = 0x0BD0,
        Fog = 0x0B60,
        Lighting = 0x0B50,
        ScissorTest = 0x0C11,
        StencilTest = 0x0B90,
        Texture2D = 0x0DE1
    }

    public enum TextureMinFilter : int
    {
        Nearest = 0x2600,
        Linear = 0x2601,
        NearestMipmapNearest = 0x2700,
        LinearMipmapNearest = 0x2701,
        NearestMipmapLinear = 0x2702,
        LinearMipmapLinear = 0x2703
    }

    public enum TextureMagFilter : int
    {
        Nearest = 0x2600,
        Linear = 0x2601
    }

    public enum TextureWrapMode : int
    {
        Clamp = 0x2900,
        Repeat = 0x2901,
        ClampToEdge = 0x812F,
        ClampToBorder = 0x812D
    }

    public enum ExtTextureFilterAnisotropic : int
    {
        TextureMaxAnisotropyExt = 0x84FE,
        MaxTextureMaxAnisotropyExt = 0x84FF
    }

    public enum ClearBufferMask : int
    {
        DepthBufferBit = 0x00000100,
        ColorBufferBit = 0x00004000
    }

    public enum BufferUsageHint : int
    {
        StreamDraw = 0x88E0,
        StreamRead = 0x88E1,
        StreamCopy = 0x88E2,
        StaticDraw = 0x88E4,
        StaticRead = 0x88E5,
        StaticCopy = 0x88E6,
        DynamicDraw = 0x88E8,
        DynamicRead = 0x88E9,
        DynamicCopy = 0x88EA
    }

    public enum TextureUnit : int
    {
        Texture0 = 0x84C0,
        Texture1 = 0x84C1,
        Texture2 = 0x84C2,
        Texture3 = 0x84C3,
        Texture4 = 0x84C4,
        Texture5 = 0x84C5,
        Texture6 = 0x84C6,
        Texture7 = 0x84C7,
        Texture8 = 0x84C8,
        Texture9 = 0x84C9,
        Texture10 = 0x84CA,
        Texture11 = 0x84CB,
        Texture12 = 0x84CC,
        Texture13 = 0x84CD,
        Texture14 = 0x84CE,
        Texture15 = 0x84CF,
        Texture16 = 0x84D0,
        Texture17 = 0x84D1,
        Texture18 = 0x84D2,
        Texture19 = 0x84D3,
        Texture20 = 0x84D4,
        Texture21 = 0x84D5,
        Texture22 = 0x84D6,
        Texture23 = 0x84D7,
        Texture24 = 0x84D8,
        Texture25 = 0x84D9,
        Texture26 = 0x84DA,
        Texture27 = 0x84DB,
        Texture28 = 0x84DC,
        Texture29 = 0x84DD,
        Texture30 = 0x84DE,
        Texture31 = 0x84DF
    }

    public enum BufferTarget : int
    {
        ArrayBuffer = 0x8892,
        ElementArrayBuffer = 0x8893,
        ShaderStorageBuffer = 0x90D2,
        UniformBuffer = 0x8A11
    }

    public enum BufferRangeTarget : int
    {
        ShaderStorageBuffer = 0x90D2,
        UniformBuffer = 0x8A11
    }

    public enum BlendEquationModeSeparate : int
    {
        FuncAdd = 0x8006,
        FuncSubtract = 0x800A,
        FuncReverseSubtract = 0x800B,
        Min = 0x8007,
        Max = 0x8008
    }

    public enum BlendingFactorSrc : int
    {
        SrcAlpha = 0x0302,
        OneMinusSrcAlpha = 0x0303,
        One = 1,
        Zero = 0,
        DstAlpha = 0x0304,
        OneMinusDstAlpha = 0x0305,
        DstColor = 0x0306,
        OneMinusDstColor = 0x0307,
        SrcAlphaSaturate = 0x0308
    }

    public enum BlendingFactorDest : int
    {
        OneMinusSrcAlpha = 0x0303,
        SrcAlpha = 0x0302,
        One = 1,
        Zero = 0,
        DstAlpha = 0x0304,
        OneMinusDstAlpha = 0x0305,
        DstColor = 0x0306,
        OneMinusDstColor = 0x0307
    }

    public enum FramebufferErrorCode : int
    {
        FramebufferComplete = 0x8CD5
    }

    public enum CullFaceMode : int
    {
        Front = 0x0404,
        Back = 0x0405,
        FrontAndBack = 0x0408
    }

    public enum GenerateMipmapTarget : int
    {
        Texture2D = 0x0DE1
    }

    public enum GetProgramParameterName : int
    {
        LinkStatus = 0x8B82,
        ActiveUniforms = 0x8B86
    }

    public enum ShaderParameter : int
    {
        CompileStatus = 0x8B81
    }

    public enum MemoryBarrierFlags : int
    {
        ShaderStorageBarrierBit = 0x2000,
        ShaderImageAccessBarrierBit = 0x00000020,
        TextureFetchBarrierBit = 0x00000008,
        AllBarrierBits = unchecked((int)0xFFFFFFFF)
    }

    public enum MaterialFace : int
    {
        Front = 0x0404,
        Back = 0x0405,
        FrontAndBack = 0x0408
    }

    public enum VertexAttribIntegerType : int
    {
        Int = 0x1404,
        UnsignedInt = 0x1405
    }

    public enum TextureTarget : int
    {
        Texture2D = 0x0DE1,
        Texture2DMultisample = 0x9100,
        Texture2DArray = 0x8C1A
    }

    public enum FramebufferTarget : int
    {
        Framebuffer = 0x8D40,
        ReadFramebuffer = 0x8CA8,
        DrawFramebuffer = 0x8CA9
    }

    public enum RenderbufferTarget : int
    {
        Renderbuffer = 0x8D41
    }

    public enum FramebufferAttachment : int
    {
        ColorAttachment0 = 0x8CE0,
        DepthAttachment = 0x8D00,
        StencilAttachment = 0x8D20
    }

    public enum TextureParameterName : int
    {
        TextureMinFilter = 0x2801,
        TextureMagFilter = 0x2800,
        TextureWrapS = 0x2802,
        TextureWrapT = 0x2803,
        TextureMaxAnisotropyExt = 0x84FE,
        GenerateMipmap = 0x8191,
        TextureSwizzleRgba = 0x8E8C,
        TextureBorderColor = 0x1004,
        TextureCompareMode = 0x884C,
        TextureCompareFunc = 0x884D
    }

    public enum DrawBufferMode : int
    {
        None = 0,
        ColorAttachment0 = 0x8CE0
    }

    public enum StringName : int
    {
        Extensions = 0x1F03,
        Version = 0x1F02,
        Renderer = 0x1F01,
        Vendor = 0x1F00,
        ShadingLanguageVersion = 0x8B8C
    }

    public enum GetPName : int
    {
        MajorVersion = 0x821B,
        MinorVersion = 0x821C,
        MaxTextureMaxAnisotropyExt = 0x84FF,
        BlendEquationAlpha = 0x883D,
        ColorClearValue = 0x0C22,
        FramebufferBinding = 0x8CA6,
        VertexArrayBinding = 0x85B5,
        ArrayBufferBinding = 0x8894,
        ElementArrayBufferBinding = 0x8895,
        CurrentProgram = 0x8B8D,
        TextureBinding2D = 0x8069,
        ActiveTexture = 0x84E0,
        Viewport = 0x0BA2,
        ScissorBox = 0x0C10,
        ColorWritemask = 0x0C23,
        DepthWritemask = 0x0B72,
        BlendSrcRgb = 0x80C9,
        BlendDstRgb = 0x80C8,
        BlendSrcAlpha = 0x80CB,
        BlendDstAlpha = 0x80CA,
        BlendEquationRgb = 0x8009
    }

    public enum HintTarget : int
    {
        GenerateMipmapHint = 0x84E3,
        PerspectiveCorrectionHint = 0x0C50,
        PointSmoothHint = 0x0C51,
        LineSmoothHint = 0x0C52,
        PolygonSmoothHint = 0x0C53,
        FogHint = 0x0C54
    }

    public enum HintMode : int
    {
        Nicest = 0x1302,
        Fastest = 0x1301,
        DontCare = 0x1300
    }

    public enum PixelStoreParameter : int
    {
        UnpackAlignment = 0x0CF5
    }

    public enum DepthFunction : int
    {
        Lequal = 0x0203,
        Equal = 0x0202,
        Less = 0x0201,
        Gequal = 0x0206,
        Greater = 0x0204
    }

    public enum DrawElementsType : int
    {
        UnsignedInt = 0x1405,
        UnsignedShort = 0x1403,
        UnsignedByte = 0x1401
    }

    public enum VertexAttribPointerType : int
    {
        Float = 0x1406,
        Int = 0x1404
    }

    public enum InternalFormat : int
    {
        Rgba = 0x8058,
        Rgba8 = 0x8058,
        DepthComponent24 = 0x81A6,
        R32f = 0x822E,
        Rgba16f = 0x881A,
        Rgb = 0x1907,
        Rgb8 = 0x8051,
        R32ui = 0x8236
    }

    public enum BlendEquationMode : int
    {
        FuncAdd = 0x8006,
        FuncSubtract = 0x800A,
        FuncReverseSubtract = 0x800B,
        Min = 0x8007,
        Max = 0x8008
    }

    public enum PolygonMode : int
    {
        Point = 0x1B00,
        Line = 0x1B01,
        Fill = 0x1B02
    }

    public enum TextureCompareMode : int
    {
        CompareRefToTexture = 0x884E,
        None = 0
    }

    public enum All : int
    {
        Lequal = 0x0203,
        CompareRefToTexture = 0x884E
    }

    public enum ReadBufferMode : int
    {
        None = 0,
        Front = 0x0404,
        Back = 0x0405
    }
}

namespace LibRender2
{
    public static class OpenGLBind
    {
        public static global::Silk.NET.OpenGL.Legacy.GL GL;
    }

    // --- GL 4.3 image-load-store enums (added for RealSky compute pass) ---

    /// <summary>Access mode for image textures (GL_READ_ONLY / GL_WRITE_ONLY / GL_READ_WRITE).</summary>
    public enum TextureAccess : int
    {
        ReadOnly = 0x88B8,
        WriteOnly = 0x88B9,
        ReadWrite = 0x88BA,
    }

    /// <summary>
    /// Sized internal formats used by GL 4.3 image textures. Only the subset
    /// required by the RealSky compute pass is defined here — extend as needed.
    /// Values match OpenGL / Silk.NET.OpenGL.GLEnum.
    /// </summary>
    public enum SizedInternalFormat : int
    {
        Rgba32f = 0x8814,
        Rgba16f = 0x881A,
        Rg32f = 0x8230,
        Rg16f = 0x822F,
        R11fG11fB10f = 0x8C3A,
        R32f = 0x822E,
        R16f = 0x822D,
        Rgba8 = 0x8058,
        Rgba8Snorm = 0x8F97,
        R8 = 0x8229,
        R8Snorm = 0x8F94,
        Rgba32ui = 0x8D70,
        Rgba16ui = 0x8D76,
        Rgb10A2ui = 0x906F,
        Rgba8ui = 0x8D7C,
        R32ui = 0x8236,
        R16ui = 0x8234,
        R8ui = 0x8232,
        Rgba32i = 0x8D82,
        Rgba16i = 0x8D88,
        Rgba8i = 0x8D8E,
        R32i = 0x8235,
        R16i = 0x8233,
        R8i = 0x8231,
    }
}
