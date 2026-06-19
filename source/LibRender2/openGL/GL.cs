using System;
using Silk.NET.OpenGL.Legacy;

namespace OpenTK.Graphics.OpenGL
{
    using Silk = global::Silk;

    public static class GL
    {
        private static global::Silk.NET.OpenGL.Legacy.GL gl => LibRender2.OpenGLBind.GL;

        public static void ActiveTexture(TextureUnit texture) => gl.ActiveTexture((Silk.NET.OpenGL.Legacy.TextureUnit)texture);
        public static void AttachShader(int program, int shader) => gl.AttachShader((uint)program, (uint)shader);
        
        // Legacy/Immediate mode
        public static void Begin(PrimitiveType mode) => gl.Begin((Silk.NET.OpenGL.Legacy.PrimitiveType)mode);
        public static void End() => gl.End();
        public static void Color3(double r, double g, double b) => gl.Color3(r, g, b);
        public static void Color4(double r, double g, double b, double a) => gl.Color4(r, g, b, a);
        public static void Color4(float r, float g, float b, float a) => gl.Color4(r, g, b, a);
        public static void Color4(byte r, byte g, byte b, byte a) => gl.Color4(r, g, b, a);
        public static void Vertex2(double x, double y) => gl.Vertex2(x, y);
        public static void Vertex3(double x, double y, double z) => gl.Vertex3(x, y, z);
        public static void TexCoord2(double u, double v) => gl.TexCoord2(u, v);
        public static void MatrixMode(MatrixMode mode) => gl.MatrixMode((Silk.NET.OpenGL.Legacy.MatrixMode)mode);
        public static void LoadIdentity() => gl.LoadIdentity();
        public static unsafe void LoadMatrix(float[] m) { fixed (float* ptr = m) gl.LoadMatrix(ptr); }
        public static unsafe void LoadMatrix(double* m) => gl.LoadMatrix(m);
        public static unsafe void MultMatrix(float[] m) { fixed (float* ptr = m) gl.MultMatrix(ptr); }
        public static unsafe void MultMatrix(double* m) => gl.MultMatrix(m);
        public static void PushMatrix() => gl.PushMatrix();
        public static void PopMatrix() => gl.PopMatrix();
        public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) => gl.Ortho(left, right, bottom, top, zNear, zFar);
        
        public static void BindBuffer(BufferTarget target, int buffer) => gl.BindBuffer((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)buffer);
        public static void BindBufferBase(BufferRangeTarget target, uint index, int buffer) => gl.BindBufferBase((Silk.NET.OpenGL.Legacy.GLEnum)target, index, (uint)buffer);
        public static void BindBufferBase(BufferTarget target, uint index, int buffer) => gl.BindBufferBase((Silk.NET.OpenGL.Legacy.GLEnum)target, index, (uint)buffer);
        public static void BindBufferBase(BufferRangeTarget target, int index, int buffer) => gl.BindBufferBase((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)index, (uint)buffer);
        public static void BindBufferBase(BufferTarget target, int index, int buffer) => gl.BindBufferBase((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)index, (uint)buffer);
        public static void BindFragDataLocation(int program, uint color, string name) => gl.BindFragDataLocation((uint)program, color, name);
        public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => gl.BindFramebuffer((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)framebuffer);
        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) => gl.BindRenderbuffer((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)renderbuffer);
        public static void BindTexture(TextureTarget target, int texture) => gl.BindTexture((Silk.NET.OpenGL.Legacy.GLEnum)target, (uint)texture);
        public static void BindVertexArray(int array) => gl.BindVertexArray((uint)array);
        
        public static void BlendEquationSeparate(BlendEquationModeSeparate modeRGB, BlendEquationModeSeparate modeAlpha) => gl.BlendEquationSeparate((Silk.NET.OpenGL.Legacy.GLEnum)modeRGB, (Silk.NET.OpenGL.Legacy.GLEnum)modeAlpha);
        public static void BlendEquationSeparate(BlendEquationMode modeRGB, BlendEquationMode modeAlpha) => gl.BlendEquationSeparate((Silk.NET.OpenGL.Legacy.GLEnum)modeRGB, (Silk.NET.OpenGL.Legacy.GLEnum)modeAlpha);
        public static void BlendFunc(BlendingFactor sfactor, BlendingFactor dfactor) => gl.BlendFunc((Silk.NET.OpenGL.Legacy.BlendingFactor)sfactor, (Silk.NET.OpenGL.Legacy.BlendingFactor)dfactor);
        public static void BlendFuncSeparate(BlendingFactorSrc srcRGB, BlendingFactorDest dstRGB, BlendingFactorSrc srcAlpha, BlendingFactorDest dstAlpha) => gl.BlendFuncSeparate((Silk.NET.OpenGL.Legacy.BlendingFactor)srcRGB, (Silk.NET.OpenGL.Legacy.BlendingFactor)dstRGB, (Silk.NET.OpenGL.Legacy.BlendingFactor)srcAlpha, (Silk.NET.OpenGL.Legacy.BlendingFactor)dstAlpha);
        
        public static unsafe void BufferData<T>(BufferTarget target, IntPtr size, T[] data, BufferUsageHint usage) where T : unmanaged
        {
            fixed (void* ptr = data)
                gl.BufferData((Silk.NET.OpenGL.Legacy.GLEnum)target, (nuint)size, ptr, (Silk.NET.OpenGL.Legacy.GLEnum)usage);
        }
        public static unsafe void BufferData(BufferTarget target, IntPtr size, IntPtr data, BufferUsageHint usage) =>
            gl.BufferData((Silk.NET.OpenGL.Legacy.GLEnum)target, (nuint)size, data.ToPointer(), (Silk.NET.OpenGL.Legacy.GLEnum)usage);
        
        public static unsafe void BufferSubData<T>(BufferTarget target, IntPtr offset, IntPtr size, T[] data) where T : unmanaged
        {
            fixed (void* ptr = data)
                gl.BufferSubData((Silk.NET.OpenGL.Legacy.GLEnum)target, offset, (nuint)size, ptr);
        }
        
        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target) => (FramebufferErrorCode)gl.CheckFramebufferStatus((Silk.NET.OpenGL.Legacy.GLEnum)target);
        public static void Clear(ClearBufferMask mask) => gl.Clear((uint)mask);
        public static unsafe void ClearBufferSubData(BufferTarget target, InternalFormat internalformat, IntPtr offset, IntPtr size, PixelFormat format, PixelType type, IntPtr data)
        {
            gl.ClearBufferSubData((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)internalformat, offset, (nuint)size, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, data.ToPointer());
        }
        public static void ClearColor(float red, float green, float blue, float alpha) => gl.ClearColor(red, green, blue, alpha);
        public static void ColorMask(bool red, bool green, bool blue, bool alpha) => gl.ColorMask(red, green, blue, alpha);
        public static void CompileShader(int shader) => gl.CompileShader((uint)shader);
        
        public static void CopyTexImage2D(TextureTarget target, int level, InternalFormat internalformat, int x, int y, int width, int height, int border) =>
            gl.CopyTexImage2D((Silk.NET.OpenGL.Legacy.GLEnum)target, level, (Silk.NET.OpenGL.Legacy.GLEnum)internalformat, x, y, (uint)width, (uint)height, border);
            
        public static int CreateProgram() => (int)gl.CreateProgram();
        public static int CreateShader(ShaderType type) => (int)gl.CreateShader((Silk.NET.OpenGL.Legacy.ShaderType)type);
        public static void CullFace(CullFaceMode mode) => gl.CullFace((Silk.NET.OpenGL.Legacy.GLEnum)mode);
        
        public static void DeleteBuffer(int buffer) => gl.DeleteBuffer((uint)buffer);
        public static void DeleteFramebuffer(int framebuffer) => gl.DeleteFramebuffer((uint)framebuffer);
        public static void DeleteFramebuffers(int n, ref int framebuffers) 
        { 
            uint val = (uint)framebuffers;
            gl.DeleteFramebuffers((uint)n, ref val); 
            framebuffers = (int)val;
        }
        public static void DeleteProgram(int program) => gl.DeleteProgram((uint)program);
        public static void DeleteRenderbuffer(int renderbuffer) => gl.DeleteRenderbuffer((uint)renderbuffer);
        public static void DeleteShader(int shader) => gl.DeleteShader((uint)shader);
        public static void DeleteTexture(int texture) => gl.DeleteTexture((uint)texture);
        
        public static void DeleteTextures(int n, ref int textures)
        {
            uint val = (uint)textures;
            gl.DeleteTextures((uint)n, ref val);
            textures = (int)val;
        }
        public static void DeleteTextures(int n, int[] textures)
        {
            uint[] val = Array.ConvertAll(textures, x => (uint)x);
            gl.DeleteTextures((uint)n, val);
        }
        public static void DeleteVertexArray(int array) => gl.DeleteVertexArray((uint)array);
        
        public static void DepthFunc(DepthFunction func) => gl.DepthFunc((Silk.NET.OpenGL.Legacy.DepthFunction)func);
        public static void DepthMask(bool flag) => gl.DepthMask(flag);
        public static void Disable(EnableCap cap) => gl.Disable((Silk.NET.OpenGL.Legacy.EnableCap)cap);
        public static void DisableVertexAttribArray(int index) => gl.DisableVertexAttribArray((uint)index);
        public static void DispatchCompute(uint num_groups_x, uint num_groups_y, uint num_groups_z) => gl.DispatchCompute(num_groups_x, num_groups_y, num_groups_z);
        public static void DispatchCompute(int num_groups_x, int num_groups_y, int num_groups_z) => gl.DispatchCompute((uint)num_groups_x, (uint)num_groups_y, (uint)num_groups_z);
        
        public static void DrawArrays(PrimitiveType mode, int first, int count) => gl.DrawArrays((Silk.NET.OpenGL.Legacy.PrimitiveType)mode, first, (uint)count);
        public static void DrawBuffer(DrawBufferMode mode) => gl.DrawBuffer((Silk.NET.OpenGL.Legacy.DrawBufferMode)mode);
        public static void DrawBuffers(int n, DrawBuffersEnum[] bufs)
        {
            var arr = Array.ConvertAll(bufs, x => (Silk.NET.OpenGL.Legacy.DrawBufferMode)x);
            gl.DrawBuffers((uint)n, arr);
        }
        public static unsafe void DrawElements(PrimitiveType mode, int count, DrawElementsType type, IntPtr indices) => gl.DrawElements((Silk.NET.OpenGL.Legacy.PrimitiveType)mode, (uint)count, (Silk.NET.OpenGL.Legacy.DrawElementsType)type, indices.ToPointer());
        
        public static void Enable(EnableCap cap) => gl.Enable((Silk.NET.OpenGL.Legacy.EnableCap)cap);
        public static void EnableVertexAttribArray(int index) => gl.EnableVertexAttribArray((uint)index);
        
        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment, RenderbufferTarget renderbuffertarget, int renderbuffer) =>
            gl.FramebufferRenderbuffer((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)attachment, (Silk.NET.OpenGL.Legacy.GLEnum)renderbuffertarget, (uint)renderbuffer);
            
        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment, TextureTarget textarget, int texture, int level) =>
            gl.FramebufferTexture2D((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)attachment, (Silk.NET.OpenGL.Legacy.GLEnum)textarget, (uint)texture, level);
            
        public static int GenBuffer() { gl.GenBuffers(1, out uint val); return (int)val; }
        public static int GenTexture() { gl.GenTextures(1, out uint val); return (int)val; }
        public static int GenFramebuffer() { gl.GenFramebuffers(1, out uint val); return (int)val; }

        public static void GenBuffers(int n, out int buffers)
        {
            gl.GenBuffers((uint)n, out uint val);
            buffers = (int)val;
        }
        public static void GenerateMipmap(GenerateMipmapTarget target) => gl.GenerateMipmap((Silk.NET.OpenGL.Legacy.TextureTarget)target);
        
        public static void GenFramebuffers(int n, out int framebuffers)
        {
            gl.GenFramebuffers((uint)n, out uint val);
            framebuffers = (int)val;
        }
        
        public static void GenRenderbuffers(int n, out int renderbuffers)
        {
            gl.GenRenderbuffers((uint)n, out uint val);
            renderbuffers = (int)val;
        }
        
        public static void GenTextures(int n, out int textures)
        {
            gl.GenTextures((uint)n, out uint val);
            textures = (int)val;
        }
        public static void GenTextures(int n, int[] textures)
        {
            uint[] val = new uint[n];
            gl.GenTextures((uint)n, val);
            for (int i = 0; i < n; i++) textures[i] = (int)val[i];
        }
        
        public static void GenVertexArrays(int n, out int arrays)
        {
            gl.GenVertexArrays((uint)n, out uint val);
            arrays = (int)val;
        }
        
        public static int GetAttribLocation(int program, string name) => gl.GetAttribLocation((uint)program, name);
        public static unsafe void GetBoolean(GetPName pname, bool[] params_array)
        {
            fixed (bool* ptr = params_array) gl.GetBoolean((Silk.NET.OpenGL.Legacy.GetPName)pname, ptr);
        }
        public static unsafe void GetBoolean(GetPName pname, out bool params_val)
        {
            gl.GetBoolean((Silk.NET.OpenGL.Legacy.GetPName)pname, out params_val);
        }
        public static ErrorCode GetError() => (ErrorCode)gl.GetError();
        public static unsafe void GetFloat(GetPName pname, float[] params_array)
        {
            fixed (float* ptr = params_array) gl.GetFloat((Silk.NET.OpenGL.Legacy.GetPName)pname, ptr);
        }
        public static float GetFloat(GetPName pname)
        {
            gl.GetFloat((Silk.NET.OpenGL.Legacy.GetPName)pname, out float val);
            return val;
        }
        public static unsafe void GetInteger(GetPName pname, out int params_val)
        {
            gl.GetInteger((Silk.NET.OpenGL.Legacy.GetPName)pname, out int val);
            params_val = val;
        }
        public static unsafe void GetInteger(GetPName pname, int[] params_array)
        {
            fixed (int* ptr = params_array) gl.GetInteger((Silk.NET.OpenGL.Legacy.GetPName)pname, ptr);
        }
        public static int GetInteger(GetPName pname)
        {
            gl.GetInteger((Silk.NET.OpenGL.Legacy.GetPName)pname, out int val);
            return val;
        }
        
        public static void GetProgram(int program, GetProgramParameterName pname, out int params_val) => gl.GetProgram((uint)program, (Silk.NET.OpenGL.Legacy.GLEnum)pname, out params_val);
        public static string GetProgramInfoLog(int program) => gl.GetProgramInfoLog((uint)program);
        public static void GetShader(int shader, ShaderParameter pname, out int params_val) => gl.GetShader((uint)shader, (Silk.NET.OpenGL.Legacy.GLEnum)pname, out params_val);
        public static string GetShaderInfoLog(int shader) => gl.GetShaderInfoLog((uint)shader);
        
        public static unsafe string GetString(StringName name)
        {
            byte* ptr = gl.GetString((Silk.NET.OpenGL.Legacy.GLEnum)name);
            return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr);
        }
        
        public static int GetUniformBlockIndex(int program, string uniformBlockName) => (int)gl.GetUniformBlockIndex((uint)program, uniformBlockName);
        public static int GetUniformLocation(int program, string name) => gl.GetUniformLocation((uint)program, name);
        
        public static void Hint(HintTarget target, HintMode mode) => gl.Hint((Silk.NET.OpenGL.Legacy.HintTarget)target, (Silk.NET.OpenGL.Legacy.HintMode)mode);
        public static bool IsEnabled(EnableCap cap) => gl.IsEnabled((Silk.NET.OpenGL.Legacy.EnableCap)cap);
        public static void LineWidth(float width) => gl.LineWidth(width);
        public static void LinkProgram(int program) => gl.LinkProgram((uint)program);
        public static void MemoryBarrier(MemoryBarrierFlags barriers) => gl.MemoryBarrier((Silk.NET.OpenGL.Legacy.MemoryBarrierMask)barriers);
        
        public static void PixelStore(PixelStoreParameter pname, int param) => gl.PixelStore((Silk.NET.OpenGL.Legacy.PixelStoreParameter)pname, param);
        public static void PolygonMode(MaterialFace face, PolygonMode mode) => gl.PolygonMode((Silk.NET.OpenGL.Legacy.GLEnum)face, (Silk.NET.OpenGL.Legacy.GLEnum)mode);
        
        public static void ProgramUniform1(int program, int location, int v0) => gl.ProgramUniform1((uint)program, location, v0);
        public static void ProgramUniform2(int program, int location, int v0, int v1) => gl.ProgramUniform2((uint)program, location, v0, v1);
        public static void ProgramUniform3(int program, int location, int v0, int v1, int v2) => gl.ProgramUniform3((uint)program, location, v0, v1, v2);
        public static void ProgramUniform4(int program, int location, int v0, int v1, int v2, int v3) => gl.ProgramUniform4((uint)program, location, v0, v1, v2, v3);
        
        public static void ProgramUniform1(int program, int location, float v0) => gl.ProgramUniform1((uint)program, location, v0);
        public static void ProgramUniform2(int program, int location, float v0, float v1) => gl.ProgramUniform2((uint)program, location, v0, v1);
        public static void ProgramUniform3(int program, int location, float v0, float v1, float v2) => gl.ProgramUniform3((uint)program, location, v0, v1, v2);
        public static void ProgramUniform4(int program, int location, float v0, float v1, float v2, float v3) => gl.ProgramUniform4((uint)program, location, v0, v1, v2, v3);

        public static unsafe void ProgramUniformMatrix4(int program, int location, int count, bool transpose, float[] value)
        {
            fixed (float* ptr = value) gl.ProgramUniformMatrix4((uint)program, location, (uint)count, transpose, ptr);
        }
        public static unsafe void ProgramUniformMatrix4(int program, int location, bool transpose, float[] value)
        {
            fixed (float* ptr = value) gl.ProgramUniformMatrix4((uint)program, location, 1, transpose, ptr);
        }
        public static unsafe void ProgramUniformMatrix4(int program, int location, bool transpose, ref float value)
        {
            fixed (float* ptr = &value) gl.ProgramUniformMatrix4((uint)program, location, 1, transpose, ptr);
        }
        public static unsafe void ProgramUniformMatrix4(int program, int location, bool transpose, ref Matrix4 value)
        {
            fixed (void* ptr = &value) gl.ProgramUniformMatrix4((uint)program, location, 1, transpose, (float*)ptr);
        }
        public static unsafe void ProgramUniformMatrix4(int program, int location, int count, bool transpose, ref Matrix4 value)
        {
            fixed (void* ptr = &value) gl.ProgramUniformMatrix4((uint)program, location, (uint)count, transpose, (float*)ptr);
        }

        public static void ReadBuffer(ReadBufferMode mode) => gl.ReadBuffer((Silk.NET.OpenGL.Legacy.ReadBufferMode)mode);
        public static unsafe void ReadPixels(int x, int y, int width, int height, PixelFormat format, PixelType type, IntPtr pixels) =>
            gl.ReadPixels(x, y, (uint)width, (uint)height, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, pixels.ToPointer());
        public static void RenderbufferStorage(RenderbufferTarget target, InternalFormat internalformat, int width, int height) =>
            gl.RenderbufferStorage((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height);
        public static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage internalformat, int width, int height) =>
            gl.RenderbufferStorage((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height);
            
        public static void Scissor(int x, int y, int width, int height) => gl.Scissor(x, y, (uint)width, (uint)height);
        public static void ShaderSource(int shader, string @string) => gl.ShaderSource((uint)shader, @string);
        
        public static unsafe void TexImage2D(TextureTarget target, int level, InternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels) =>
            gl.TexImage2D((Silk.NET.OpenGL.Legacy.GLEnum)target, level, (int)(Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height, border, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, pixels.ToPointer());
            
        public static unsafe void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels) =>
            gl.TexImage2D((Silk.NET.OpenGL.Legacy.GLEnum)target, level, (int)(Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height, border, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, pixels.ToPointer());

        public static unsafe void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : unmanaged
        {
            fixed (void* ptr = pixels)
                gl.TexImage2D((Silk.NET.OpenGL.Legacy.GLEnum)target, level, (int)(Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height, border, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, ptr);
        }
        public static unsafe void TexImage2D<T>(TextureTarget target, int level, InternalFormat internalformat, int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : unmanaged
        {
            fixed (void* ptr = pixels)
                gl.TexImage2D((Silk.NET.OpenGL.Legacy.GLEnum)target, level, (int)(Silk.NET.OpenGL.Legacy.GLEnum)internalformat, (uint)width, (uint)height, border, (Silk.NET.OpenGL.Legacy.GLEnum)format, (Silk.NET.OpenGL.Legacy.GLEnum)type, ptr);
        }

        public static void TexParameter(TextureTarget target, TextureParameterName pname, int param) => gl.TexParameter((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)pname, param);
        public static void TexParameter(TextureTarget target, TextureParameterName pname, float param) => gl.TexParameter((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)pname, param);
        public static unsafe void TexParameter(TextureTarget target, TextureParameterName pname, int[] params_array) { fixed (int* ptr = params_array) gl.TexParameter((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)pname, ptr); }
        public static unsafe void TexParameter(TextureTarget target, TextureParameterName pname, float[] params_array) { fixed (float* ptr = params_array) gl.TexParameter((Silk.NET.OpenGL.Legacy.GLEnum)target, (Silk.NET.OpenGL.Legacy.GLEnum)pname, ptr); }

        public static void Uniform1(int location, int v0) => gl.Uniform1(location, v0);
        public static void Uniform1(int location, float v0) => gl.Uniform1(location, v0);
        public static void Uniform2(int location, float v0, float v1) => gl.Uniform2(location, v0, v1);
        public static void Uniform3(int location, float v0, float v1, float v2) => gl.Uniform3(location, v0, v1, v2);
        public static void Uniform4(int location, float v0, float v1, float v2, float v3) => gl.Uniform4(location, v0, v1, v2, v3);
        public static void UniformBlockBinding(int program, uint uniformBlockIndex, uint uniformBlockBinding) =>
            gl.UniformBlockBinding((uint)program, uniformBlockIndex, uniformBlockBinding);
        public static void UniformBlockBinding(int program, int uniformBlockIndex, int uniformBlockBinding) =>
            gl.UniformBlockBinding((uint)program, (uint)uniformBlockIndex, (uint)uniformBlockBinding);
            
        public static unsafe void UniformMatrix4(int location, int count, bool transpose, ref float value)
        {
            fixed (float* ptr = &value) gl.UniformMatrix4(location, (uint)count, transpose, ptr);
        }
        public static unsafe void UniformMatrix4(int location, bool transpose, ref float value)
        {
            fixed (float* ptr = &value) gl.UniformMatrix4(location, 1, transpose, ptr);
        }
        public static unsafe void UniformMatrix4(int location, bool transpose, ref Matrix4 value)
        {
            fixed (void* ptr = &value) gl.UniformMatrix4(location, 1, transpose, (float*)ptr);
        }
        public static unsafe void UniformMatrix4(int location, int count, bool transpose, ref Matrix4 value)
        {
            fixed (void* ptr = &value) gl.UniformMatrix4(location, (uint)count, transpose, (float*)ptr);
        }
        public static unsafe void UniformMatrix4(int location, int count, bool transpose, float[] value)
        {
            fixed (float* ptr = value) gl.UniformMatrix4(location, (uint)count, transpose, ptr);
        }
        
        // BindImageTexture — required by GL 4.3 compute shaders that write to image2D
        // (RealSky atmospheric compute pass).
        // Mirrors Silk.NET.OpenGL.Legacy.GL.BindImageTexture — arguments are passed through
        // with only the texture handle widened to uint and the access / format enums
        // mapped to Silk.NET.OpenGL.Legacy.GLEnum.
        public static unsafe void BindImageTexture(uint unit, int texture, int level, bool layered, int layer, LibRender2.TextureAccess access, LibRender2.SizedInternalFormat format)
        {
            gl.BindImageTexture(unit, (uint)texture, level, layered, layer, (Silk.NET.OpenGL.Legacy.GLEnum)access, (Silk.NET.OpenGL.Legacy.GLEnum)format);
        }

        public static void UseProgram(int program) => gl.UseProgram((uint)program);
        
        public static unsafe void VertexAttribIPointer(int index, int size, VertexAttribIntegerType type, int stride, IntPtr pointer) =>
            gl.VertexAttribIPointer((uint)index, size, (Silk.NET.OpenGL.Legacy.GLEnum)type, (uint)stride, pointer.ToPointer());
            
        public static unsafe void VertexAttribPointer(int index, int size, VertexAttribPointerType type, bool normalized, int stride, int offset) =>
            gl.VertexAttribPointer((uint)index, size, (Silk.NET.OpenGL.Legacy.GLEnum)type, normalized, (uint)stride, (void*)offset);
            
        public static void Viewport(int x, int y, int width, int height) => gl.Viewport(x, y, (uint)width, (uint)height);
    }
}
