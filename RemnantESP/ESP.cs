using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Toolkit;
using SharpDX.Toolkit.Graphics;

namespace RemnantESP
{
    class ESP : Game
    {
        SpriteBatch SpriteBatch;
        SpriteFont SpriteFont;
        public ESP()
        {
            new GraphicsDeviceManager(this);
        }
        protected override void Initialize()
        {
            base.Initialize();
            SpriteBatch = new SpriteBatch(GraphicsDevice);
            SpriteFont = ToDisposeContent(SpriteFont.New(GraphicsDevice, SpriteFontData.Load(Properties.Resources.Arial8)));
            Window.IsMouseVisible = true;
            Window.Title = Window.Name = "ShalzuthPerception";
            ((Form)Window.NativeWindow).FormBorderStyle = FormBorderStyle.None;
            Window.ClientSizeChanged += (s, ea) => { };
            ((Form)Window.NativeWindow).TransparencyKey = System.Drawing.Color.AntiqueWhite;
            Process process = default;
            while (process == default(Process))
            {
                process = Process.GetProcesses().FirstOrDefault(p => p.ProcessName.Contains(Engine.Memory.Process.ProcessName) && p.MainWindowHandle != IntPtr.Zero);
                Thread.Sleep(1000);
            }
            SetWindowLong(((Form)Window.NativeWindow).Handle, -20, GetWindowLong(((Form)Window.NativeWindow).Handle, -20) | 0x80000 | 0x20);
            SetParent(((Form)Window.NativeWindow).Handle, (IntPtr)process.MainWindowHandle);
            SetWindowPos(((Form)Window.NativeWindow).Handle, 0, 0, 0, 1920, 1080, 0);
        }
        static byte[] BitmapToByteArray(System.Drawing.Bitmap bitmap)
        {
            var bmpdata = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
            int numbytes = bmpdata.Stride * bitmap.Height;
            byte[] bytedata = new byte[numbytes];
            IntPtr ptr = bmpdata.Scan0;
            Marshal.Copy(ptr, bytedata, 0, numbytes);
            bitmap.UnlockBits(bmpdata);
            return bytedata;
        }
        Texture2D box;
        Texture2D line;
        protected override void LoadContent()
        {
            using (var bmp = new System.Drawing.Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.White))
                {
                    g.DrawRectangle(pen, 0, 0, 128, 128);
                }
                var data = BitmapToByteArray(bmp);
                box = Texture2D.New(GraphicsDevice, bmp.Width, bmp.Height, SharpDX.DXGI.Format.R8G8B8A8_UNorm);
                box.SetData(data);
            }
            using (var bmp = new System.Drawing.Bitmap(8, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                {
                    g.FillRectangle(brush, 0, 0, 8, 128);
                }
                var data = BitmapToByteArray(bmp);
                line = Texture2D.New(GraphicsDevice, bmp.Width, bmp.Height, SharpDX.DXGI.Format.R8G8B8A8_UNorm);
                line.SetData(data);
            }
            base.LoadContent();
        }
        Vector3 LastRotation = Vector3.Zero;
        Vector3 vAxisX = Vector3.Zero;
        Vector3 vAxisY = Vector3.Zero;
        Vector3 vAxisZ = Vector3.Zero;
        Vector2 WorldToScreen(Vector3 worldLocation, Vector3 cameraLocation, Vector3 cameraRotation, Single fieldOfView)
        {
            if (LastRotation != cameraRotation)
            {
                cameraRotation.GetAxes(out vAxisX, out vAxisY, out vAxisZ);
                LastRotation = cameraRotation;
            }
            var vDelta = worldLocation - cameraLocation;
            var vTransformed = new Vector3(vDelta.Mult(vAxisY), vDelta.Mult(vAxisZ), vDelta.Mult(vAxisX));
            if (vTransformed.Z < 1f)
                vTransformed.Z = 1f;
            float ScreenCenterX = Window.ClientBounds.Width / 2;
            float ScreenCenterY = Window.ClientBounds.Height / 2;
            var fullScreen = new Vector2(ScreenCenterX + vTransformed.X * (ScreenCenterX / (float)Math.Tan(fieldOfView * (float)Math.PI / 360)) / vTransformed.Z,
                ScreenCenterY - vTransformed.Y * (ScreenCenterX / (float)Math.Tan(fieldOfView * (float)Math.PI / 360)) / vTransformed.Z);
            return new Vector2(fullScreen.X, fullScreen.Y);
        }
        Single LastYRotation = 0;
        Single CameraSinTheta = 0;
        Single CameraCosTheta = 0;
        Vector2 WorldToWindow(Vector3 targetLocation, Vector3 playerLocation, Vector3 cameraRotation, Single maxRange, Single radarSize)
        {
            if (LastYRotation != cameraRotation.Y)
            {
                var CameraRadians = (Single)Math.PI * (-cameraRotation.Y - 90.0f) / 180.0f;
                CameraSinTheta = (Single)Math.Sin(CameraRadians);
                CameraCosTheta = (Single)Math.Cos(CameraRadians);
                LastYRotation = cameraRotation.Y;
            }
            radarSize /= 2;
            var diff = targetLocation - playerLocation;
            var radarLoc = new Vector2(radarSize * diff.X / maxRange, radarSize * diff.Y / maxRange);
            radarLoc = new Vector2(CameraCosTheta * radarLoc.X - CameraSinTheta * radarLoc.Y, CameraSinTheta * radarLoc.X + CameraCosTheta * radarLoc.Y);
            radarLoc += new Vector2(radarSize, radarSize);
            return radarLoc;
        }
        void DrawLines(Color color, Vector2[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
                DrawLine(color, points[i], points[i + 1]);
        }
        void DrawLine(Color color, Vector2 start, Vector2 end)
        {
            var dist = Vector2.Distance(start, end);
            var angle = -Math.PI / 2 - Math.Atan2(-(end.Y - start.Y), end.X - start.X);
            SpriteBatch.Draw(line, new Rectangle((int)(start.X), (int)(start.Y), 2, (int)dist), null, color, (Single)angle, Vector2.Zero, SpriteEffects.None, 0.0f);
        }
        void DrawBox(Vector3 targetPosition, Vector3 targetRotation, Vector3 cameraLocation, Vector3 cameraRotation, Single fieldOfView)
        {
            var targetTest = WorldToScreen(targetPosition, cameraLocation, cameraRotation, fieldOfView);
            if (targetTest.X < 0 || targetTest.Y < 0 || targetTest.X > Width || targetTest.Y > Height)
                return;

            Single l = 60f, w = 60f, h = 160f, o = 50f;

            var zOffset = -80f;
            var xOffset = -20f;
            var yOffset = -20f;

            var p02 = new Vector3(o - l, w / 2, 0f);
            var p03 = new Vector3(o - l, -w / 2, 0f);
            var p00 = new Vector3(o, -w / 2, 0f);
            var p01 = new Vector3(o, w / 2, 0f);

            var theta1 = 2.0f * (targetRotation.FromRotator().Y);

            var cos = (float)Math.Cos(theta1);
            var sin = (float)Math.Sin(theta1);

            float[] rotMVals =
                {cos, sin, 0, 0,
                -sin, cos, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1 };
            var rotM = new Matrix(rotMVals);

            var curPos = new Vector3(targetPosition.X + xOffset, targetPosition.Y + yOffset, targetPosition.Z + zOffset);
            p01 = Vector3.TransformCoordinate(p01, rotM) + curPos;
            p03 = Vector3.TransformCoordinate(p03, rotM) + curPos;
            p00 = Vector3.TransformCoordinate(p00, rotM) + curPos;
            p02 = Vector3.TransformCoordinate(p02, rotM) + curPos;

            var s03 = WorldToScreen(p03, cameraLocation, cameraRotation, fieldOfView);
            var s00 = WorldToScreen(p00, cameraLocation, cameraRotation, fieldOfView);
            var s02 = WorldToScreen(p02, cameraLocation, cameraRotation, fieldOfView);
            var s01 = WorldToScreen(p01, cameraLocation, cameraRotation, fieldOfView);
            var boxColor = Color.Red;

            p03.Z += h; var s032 = WorldToScreen(p03, cameraLocation, cameraRotation, fieldOfView);
            p00.Z += h; var s002 = WorldToScreen(p00, cameraLocation, cameraRotation, fieldOfView);
            p02.Z += h; var s022 = WorldToScreen(p02, cameraLocation, cameraRotation, fieldOfView);
            p01.Z += h; var s012 = WorldToScreen(p01, cameraLocation, cameraRotation, fieldOfView);

            DrawLines(boxColor, new Vector2[] { s00, s01, s02, s03, s00 });
            DrawLines(boxColor, new Vector2[] { s002, s012, s022, s032, s002 });
            DrawLine(boxColor, s03, s032);
            DrawLine(boxColor, s00, s002);
            DrawLine(boxColor, s02, s022);
            DrawLine(boxColor, s01, s012);
        }
        void DrawArrow(Vector3 targetPosition, Vector3 targetRotation, Vector3 playerLocation, Vector3 cameraRotation)
        {
            if (targetPosition == playerLocation)
            {
                SpriteBatch.Draw(line, new Rectangle(100, 100, 2, 2), null, Color.Green, 0, Vector2.Zero, SpriteEffects.None, 0.0f);
                return;
            }
            var radarLoc = WorldToWindow(targetPosition, playerLocation, cameraRotation, 3000, 200);
            if (radarLoc.X > 0 && radarLoc.X < 200 && radarLoc.Y > 0 && radarLoc.Y < 200)
            {
                /*targetRotation.Normalize();
                var endLoc = targetPosition + 500 * targetRotation;
                var endRadarLoc = WorldToWindow(endLoc, playerLocation, cameraRotation, 3000, 200);
                DrawLine(Color.Yellow, radarLoc, endRadarLoc);*/
                SpriteBatch.Draw(line, new Rectangle((int)radarLoc.X, (int)radarLoc.Y, 2, 2), null, Color.Red, 0, Vector2.Zero, SpriteEffects.None, 0.0f);
            }

        }
        void DrawMenu()
        {
            if (GetKeyState((int)Keys.Insert) == 0)
            {
                var i = 0;
                SpriteBatch.DrawString(SpriteFont, "ShaIzuth's ESP", new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "FPS : " + measuredFPS.ToString("0.00"), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Enemies(F1) : " + (GetKeyState((int)Keys.F1) == 0), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Items(F2) : " + (GetKeyState((int)Keys.F2) == 0), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Trash(F3) : " + (GetKeyState((int)Keys.F3) == 1), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Debug(F4) : " + (GetKeyState((int)Keys.F4) == 1), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Ammo+Stamina(F5) : " + (GetKeyState((int)Keys.F5) == 1), new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Hide Menu (Ins)", new Vector2(20, 20 + i++ * 15), Color.White);
                SpriteBatch.DrawString(SpriteFont, "Exit (End) ", new Vector2(20, 20 + i++ * 15), Color.White);
                //SpriteBatch.DrawString(SpriteFont, "Show trash", new Vector2(20, 20 + i++ * 15), Color.White);
            }
        }
        void DrawEsp()
        {
            var World = new Engine.UEObject(Engine.GWorld);
            //var Level = World["PersistentLevel"];
            var Levels = World["Levels"];
            var OwningGameInstance = World["OwningGameInstance"];
            var LocalPlayers = OwningGameInstance["LocalPlayers"];
            var PlayerController = LocalPlayers[0]["PlayerController"];
            //var Player = PlayerController["Player"];

            var AcknowledgedPawn = PlayerController["AcknowledgedPawn"];
            if (AcknowledgedPawn == null || !AcknowledgedPawn.IsA("Class Engine.Character")) return;
            if (GetKeyState((int)Keys.F5) == 1)
            {
                Engine.Memory.WriteProcessMemory(AcknowledgedPawn["Stamina"]["Value"].Address, BitConverter.GetBytes(50.0f));
                Engine.Memory.WriteProcessMemory(AcknowledgedPawn["LongGunAmmo"]["Value"].Address, BitConverter.GetBytes(100.0f));
                Engine.Memory.WriteProcessMemory(AcknowledgedPawn["HandGunAmmo"]["Value"].Address, BitConverter.GetBytes(100.0f));
                var CachedInventory = AcknowledgedPawn["CachedInventory"];
                var Items = CachedInventory["Items"];
                // item is struct, bigger than normal... need to update class to do Items[itemIndex] correctly
                var ItemsAddr = Engine.Memory.ReadProcessMemory<UInt64>(Items.Address);
                for (var itemIndex = 0u; itemIndex < Items.Num; itemIndex++)
                {
                    var ItemInstanceData = new Engine.UEObject(Engine.Memory.ReadProcessMemory<UInt64>(ItemsAddr + 3 * 8 + itemIndex * 5 * 8));
                    if (ItemInstanceData.IsA("Class GunfireRuntime.RangedWeaponInstanceData"))
                        Engine.Memory.WriteProcessMemory(ItemInstanceData["AmmoInClip"].Address, BitConverter.GetBytes(50));
                }
            }
            var PlayerCameraManager = PlayerController["PlayerCameraManager"];
            var CameraCache = PlayerCameraManager["CameraCachePrivate"];
            var CameraPOV = CameraCache["POV"];
            var CameraLocation = Engine.Memory.ReadProcessMemory<Vector3>(CameraPOV["Location"].Address);
            var CameraRotation = Engine.Memory.ReadProcessMemory<Vector3>(CameraPOV["Rotation"].Address);
            var CameraFOV = Engine.Memory.ReadProcessMemory<Single>(CameraPOV["FOV"].Address);

            var PlayerRoot = AcknowledgedPawn["RootComponent"];
            var PlayerRelativeLocation = PlayerRoot["RelativeLocation"];
            var PlayerLocation = Engine.Memory.ReadProcessMemory<Vector3>(PlayerRelativeLocation.Address);
            //var PlayerRotation = Engine.Memory.ReadProcessMemory<Vector3>(PlayerRelativeLocation.Address + 24);
            DrawArrow(PlayerLocation, CameraRotation, PlayerLocation, CameraRotation);
            for (var levelIndex = 1u; levelIndex < Levels.Num; levelIndex++)
            {
                var Level = Levels[levelIndex];
                // https://github.com/EpicGames/UnrealEngine/blob/4.22.3-release/Engine/Source/Runtime/Engine/Classes/Engine/Level.h#L376 doesn't exist by string
                var Actors = new Engine.UEObject(Level.Address + 0xA8);
                for (var i = 0u; i < Actors.Num; i++)
                {
                    var Actor = Actors[i];
                    if (Actor.Address == 0) continue;
                    if (Actor.Value == 0) continue;
                    if (Actor.IsA("Class GunfireRuntime.Item") || Actor.IsA("Class Remnant.LootContainer"))
                    {
                        var RootComponent = Actor["RootComponent"];
                        if (RootComponent == null || RootComponent.Address == 0 || !RootComponent.ClassName.Contains("Component")) continue;
                        var RelativeLocation = RootComponent["RelativeLocation"];
                        var Location = Engine.Memory.ReadProcessMemory<Vector3>(RelativeLocation.Address);
                        if (Actor["bActorIsBeingDestroyed"].Value == 1) continue;
                        var loc = WorldToScreen(Location, CameraLocation, CameraRotation, CameraFOV);
                        if (loc.X > 0 && loc.Y > 0 && loc.X < Width && loc.Y < Height)
                        {
                            var dist = ((CameraLocation - Location).Length() / 100.0f).ToString("0.0");

                            if (GetKeyState((int)Keys.F2) == 0)
                            {
                                if (Actor.ClassName.Contains("TraitBook")) SpriteBatch.DrawString(SpriteFont, "Trait(" + dist + ")", loc, Color.CornflowerBlue);
                                else if (Actor.ClassName.Contains("Traits"))
                                {
                                    var InteractLabel = Engine.Memory.ReadProcessMemory<UInt64>(Actor["InteractLabel"].Address);
                                    var ItemNameAddr = Engine.Memory.ReadProcessMemory<UInt64>(InteractLabel + 0x28); ;
                                    var ItemName = Engine.Memory.ReadProcessMemory<String>(ItemNameAddr);
                                    SpriteBatch.DrawString(SpriteFont, ItemName + "(" + dist + ")", loc, Color.CornflowerBlue);
                                }
                                else if (Actor.ClassName.Contains("LootContainer"))
                                {
                                    var tag = Engine.Memory.ReadProcessMemory<UInt16>(Actor.Address + 0x198);
                                    if (tag != 1) SpriteBatch.DrawString(SpriteFont, "Box(" + dist + ")", loc, Color.Cyan);
                                    //else SpriteBatch.DrawString(SpriteFont, "BoxOpened(" + dist + ")", loc, Color.Cyan);
                                }
                                else if (Actor.ClassName.Contains("Trinket"))
                                {
                                    var ItemName = Actor.ClassName.Substring(Actor.ClassName.IndexOf("Trinket") + 8);
                                    ItemName = ItemName.Substring(0, ItemName.IndexOf("Trinket") - 1);
                                    SpriteBatch.DrawString(SpriteFont, ItemName + "(" + dist + ")", loc, Color.Magenta);
                                }
                                else if (Actor.ClassName.Contains("GenericItem"))
                                {
                                    var InteractLabel = Engine.Memory.ReadProcessMemory<UInt64>(Actor["InteractLabel"].Address);
                                    var ItemNameAddr = Engine.Memory.ReadProcessMemory<UInt64>(InteractLabel + 0x28); ;
                                    var ItemName = Engine.Memory.ReadProcessMemory<String>(ItemNameAddr);
                                    SpriteBatch.DrawString(SpriteFont, ItemName + "(" + dist + ")", loc, Color.Magenta);
                                }
                            }
                            if (GetKeyState((int)Keys.F3) == 1)
                            {
                                if (Actor.ClassName.Contains("Scraps")) SpriteBatch.DrawString(SpriteFont, "Scrap(" + dist + ")", loc, Color.PaleGoldenrod);
                                else if (Actor.ClassName.Contains("Ammo_HandGun")) SpriteBatch.DrawString(SpriteFont, "SAmmo(" + dist + ")", loc, Color.White);
                                else if (Actor.ClassName.Contains("Ammo_LongGun")) SpriteBatch.DrawString(SpriteFont, "PAmmo(" + dist + ")", loc, Color.Red);
                                else if (Actor.ClassName.Contains("Consumable")) SpriteBatch.DrawString(SpriteFont, "Item(" + dist + ")", loc, Color.Green);
                                else if (Actor.ClassName.Contains("Iron")) SpriteBatch.DrawString(SpriteFont, "Iron(" + dist + ")", loc, Color.PaleGoldenrod);
                            }
                            
                            if (GetKeyState((int)Keys.F4) == 1)
                                SpriteBatch.DrawString(SpriteFont, "Debug(" + dist + ")" + Actor.ClassName, loc, Color.White);

                        }
                    }
                    if (GetKeyState((int)Keys.F1) == 0 && Actor.IsA("Class GunfireRuntime.AICharacter"))
                    {
                        var RootComponent = Actor["RootComponent"];
                        if (RootComponent == null || RootComponent.Address == 0 || !RootComponent.IsA("Class Engine.CapsuleComponent")) continue;
                        var RelativeLocation = RootComponent["RelativeLocation"];
                        var Location = Engine.Memory.ReadProcessMemory<Vector3>(RelativeLocation.Address);
                        //var RelativeRotation = RootComponent["RelativeRotation"];
                        var Rotation = Engine.Memory.ReadProcessMemory<Vector3>(RelativeLocation.Address + 24);
                        if (Actor["bActorIsBeingDestroyed"].Value == 1) continue;
                        var hp = Engine.Memory.ReadProcessMemory<Single>(Actor["HealthNormalized"].Address);
                        if (hp == 0) continue;
                        DrawBox(Location, Rotation, CameraLocation, CameraRotation, CameraFOV);
                        DrawArrow(Location, Rotation, PlayerLocation, CameraRotation);
                    }
                }
            }
        }
        Int32 Height = 0;
        Int32 Width = 0;
        Stopwatch clock = new Stopwatch();
        long frameCount;
        double measuredFPS;
        protected override void Draw(GameTime gameTime)
        {
            if (GetKeyState((int)Keys.End) == 1)
                Environment.Exit(0);
            if (!clock.IsRunning) clock.Start();
            frameCount++;
            if (clock.ElapsedMilliseconds >= 1000)
            {
                measuredFPS = (double)frameCount / (clock.ElapsedMilliseconds/ 1000.0f);
                frameCount = 0;
                clock.Restart();
            }
            Height = ((Form)Window.NativeWindow).ClientSize.Height;
            Width = ((Form)Window.NativeWindow).ClientSize.Width;
            GraphicsDevice.Clear(Color.AntiqueWhite);
            SpriteBatch.Begin(SpriteSortMode.Immediate, GraphicsDevice.BlendStates.Default);
            DrawMenu();
            DrawEsp();
            SpriteBatch.End();
            base.Draw(gameTime);
        }
        [DllImport("user32")] static extern Int32 GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32")] static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32")] static extern IntPtr SetActiveWindow(IntPtr handle);
        [DllImport("user32")] static extern short GetKeyState(int keyCode);
        protected void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021)
            {
                m.Result = (IntPtr)4; // prevent the form from being clicked and gaining focus
                return;
            }
            else if (m.Msg == 6)
            {
                if (((int)m.WParam & 0xFFFF) != 0)
                    if (m.LParam != IntPtr.Zero) SetActiveWindow(m.LParam);
                    else SetActiveWindow(IntPtr.Zero);
            }
        }
    }
}
