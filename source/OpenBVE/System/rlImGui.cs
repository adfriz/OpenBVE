/**********************************************************************************************
*
*   rlImGui _ raylib-cs + imgui-cs
*
*   A bridge between raylib-cs and imgui-cs for professional UI tools.
*
**********************************************************************************************/

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;

namespace OpenBve
{
    public static class rlImGui
    {
        private static IntPtr ImGuiContext = IntPtr.Zero;
        private static Texture2D FontTexture;

        public static void Setup(bool useDarkStyle = true)
        {
            ImGuiContext = ImGui.CreateContext();
            if (useDarkStyle)
                ImGui.StyleColorsDark();
            else
                ImGui.StyleColorsLight();

            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

            // Load Font Texture
            unsafe
            {
                byte* pixels;
                int width, height;
                io.Fonts.GetTexDataAsRGBA32(out pixels, out width, out height);

                Image image = new Image
                {
                    Data = (void*)pixels,
                    Width = width,
                    Height = height,
                    Mipmaps = 1,
                    Format = PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8A8
                };

                FontTexture = Raylib.LoadTextureFromImage(image);
                io.Fonts.SetTexID(new IntPtr(FontTexture.Id));
            }
        }

        public static void Begin()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            io.DeltaTime = Raylib.GetFrameTime();

            ImGui.NewFrame();
        }

        public static void End()
        {
            ImGui.Render();
            DrawData(ImGui.GetDrawData());
        }

        public static void Shutdown()
        {
            Raylib.UnloadTexture(FontTexture);
            ImGui.DestroyContext(ImGuiContext);
        }

        private static void DrawData(ImGuiDrawDataPtr drawData)
        {
            rlgl.rlDrawRenderBatchActive();
            rlgl.rlDisableBackfaceCulling();

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImGuiDrawListPtr cmdList = drawData.CmdListsRange[n];
                
                for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
                {
                    ImGuiCmdPtr pcmd = cmdList.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        // Handle callback
                    }
                    else
                    {
                        Raylib.BeginScissorMode((int)pcmd.ClipRect.X, (int)pcmd.ClipRect.Y, (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X), (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y));
                        
                        // Map ImGui vertices to rlgl
                        rlgl.rlSetTexture(pcmd.TextureId.ToInt32());
                        rlgl.rlBegin(rlgl.RL_TRIANGLES);
                        
                        unsafe
                        {
                            ushort* indices = (ushort*)cmdList.IdxBuffer.Data;
                            ImDrawVert* vertices = (ImDrawVert*)cmdList.VtxBuffer.Data;

                            for (int i = 0; i < pcmd.ElemCount; i++)
                            {
                                ushort idx = indices[pcmd.IdxOffset + i];
                                ImDrawVert v = vertices[idx];

                                rlgl.rlColor4ub(v.col & 0xFF, (v.col >> 8) & 0xFF, (v.col >> 16) & 0xFF, (v.col >> 24) & 0xFF);
                                rlgl.rlTexCoord2f(v.uv.X, v.uv.Y);
                                rlgl.rlVertex2f(v.pos.X, v.pos.Y);
                            }
                        }
                        rlgl.rlEnd();
                        rlgl.rlSetTexture(0);
                    }
                }
            }
            Raylib.EndScissorMode();
            rlgl.rlEnableBackfaceCulling();
        }
    }
}
