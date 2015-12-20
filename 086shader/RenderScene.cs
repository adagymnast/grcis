﻿#define USE_INVALIDATE

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using OpenglSupport;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace _086shader
{
  public partial class Form1
  {
    /// <summary>
    /// Are we allowed to use VBO?
    /// </summary>
    bool useVBO = true;

    /// <summary>
    /// Can we use shaders?
    /// </summary>
    bool canShaders = false;

    /// <summary>
    /// Are we currently using shaders?
    /// </summary>
    bool useShaders = false;

    uint[] VBOid = null;         // vertex array (colors, normals, coords), index array
    int stride = 0;              // stride for vertex array

    /// <summary>
    /// Global GLSL program repository.
    /// </summary>
    Dictionary<string, GlProgramInfo> programs = new Dictionary<string, GlProgramInfo>();

    /// <summary>
    /// Current (active) GLSL program.
    /// </summary>
    GlProgram activeProgram = null;

    long lastFpsTime = 0L;
    int frameCounter = 0;
    long triangleCounter = 0L;
    double lastFps = 0.0;
    double lastTps = 0.0;

    /// <summary>
    /// Function called whenever the main application is idle..
    /// </summary>
    void Application_Idle ( object sender, EventArgs e )
    {
      while ( glControl1.IsIdle )
      {
#if USE_INVALIDATE
        glControl1.Invalidate();
#else
        glControl1.MakeCurrent();
        Render();
#endif

        long now = DateTime.Now.Ticks;
        if ( now - lastFpsTime > 5000000 )      // more than 0.5 sec
        {
          lastFps = 0.5 * lastFps + 0.5 * (frameCounter * 1.0e7 / (now - lastFpsTime));
          lastTps = 0.5 * lastTps + 0.5 * (triangleCounter * 1.0e7 / (now - lastFpsTime));
          lastFpsTime = now;
          frameCounter = 0;
          triangleCounter = 0L;

          if ( lastTps < 5.0e5 )
            labelFps.Text = String.Format( CultureInfo.InvariantCulture, "Fps: {0:f1}, Tps: {1:f0}k",
                                           lastFps, (lastTps * 1.0e-3) );
          else
            labelFps.Text = String.Format( CultureInfo.InvariantCulture, "Fps: {0:f1}, Tps: {1:f1}m",
                                           lastFps, (lastTps * 1.0e-6) );
        }
      }
    }

    /// <summary>
    /// OpenGL init code.
    /// </summary>
    void InitOpenGL ()
    {
      // log OpenGL info just for curiosity:
      GlInfo.LogGLProperties();

      // general OpenGL:
      glControl1.VSync = true;
      GL.ClearColor( Color.DarkBlue );
      GL.Enable( EnableCap.DepthTest );
      GL.ShadeModel( ShadingModel.Flat );

      // VBO init:
      VBOid = new uint[ 2 ];
      GL.GenBuffers( 2, VBOid );
      useVBO = (GL.GetError() == ErrorCode.NoError);

      // shaders:
      if ( useVBO )
        canShaders = SetupShaders();

      // texture:
      GenerateTexture();
    }

    bool SetupShaders ()
    {
      activeProgram = null;

      foreach ( var programInfo in programs.Values )
        if ( programInfo.Setup() )
          activeProgram = programInfo.program;

      if ( activeProgram == null )
        return false;

      GlProgramInfo defInfo;
      if ( programs.TryGetValue( "default", out defInfo ) &&
           defInfo.program != null )
        activeProgram = defInfo.program;

      return true;
    }

    // generated texture:
    const int TEX_SIZE = 128;
    const int TEX_CHECKER_SIZE = 8;
    static Vector3 colWhite = new Vector3( 0.85f, 0.75f, 0.30f );
    static Vector3 colBlack = new Vector3( 0.15f, 0.15f, 0.50f );
    static Vector3 colShade = new Vector3( 0.15f, 0.15f, 0.15f );

    /// <summary>
    /// Texture handle
    /// </summary>
    int texName = 0;

    /// <summary>
    /// Generate the texture.
    /// </summary>
    void GenerateTexture ()
    {
      GL.PixelStore( PixelStoreParameter.UnpackAlignment, 1 );
      texName = GL.GenTexture();
      GL.BindTexture( TextureTarget.Texture2D, texName );

      Vector3[,] data = new Vector3[ TEX_SIZE, TEX_SIZE ];
      for ( int y = 0; y < TEX_SIZE; y++ )
        for ( int x = 0; x < TEX_SIZE; x++ )
        {
          bool odd = ((x / TEX_CHECKER_SIZE + y / TEX_CHECKER_SIZE) & 1) > 0;
          data[ y, x ] = odd ? colBlack : colWhite;
          // add some fancy shading on the edges:
          if ( (x % TEX_CHECKER_SIZE) == 0 || (y % TEX_CHECKER_SIZE) == 0 )
            data[ y, x ] += colShade;
          if ( ((x+1) % TEX_CHECKER_SIZE) == 0 || ((y+1) % TEX_CHECKER_SIZE) == 0 )
            data[ y, x ] -= colShade;
        }

      GL.TexImage2D( TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, TEX_SIZE, TEX_SIZE, 0, PixelFormat.Rgb, PixelType.Float, data );

      GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat );
      GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat );
      GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear );
      GL.TexParameter( TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Linear );

      GlInfo.LogError( "create-texture" );
    }

    /// <summary>
    /// Prepare VBO content and upload it to the GPU.
    /// </summary>
    void PrepareDataBuffers ()
    {
      if ( useVBO &&
           scene != null &&
           scene.Triangles > 0 )
      {
        // Vertex array: color [normal] coord
        GL.BindBuffer( BufferTarget.ArrayBuffer, VBOid[ 0 ] );
        int vertexBufferSize = scene.VertexBufferSize( true, true, true, true );
        GL.BufferData( BufferTarget.ArrayBuffer, (IntPtr)vertexBufferSize, IntPtr.Zero, BufferUsageHint.StaticDraw );
        IntPtr videoMemoryPtr = GL.MapBuffer( BufferTarget.ArrayBuffer, BufferAccess.WriteOnly );
        unsafe
        {
          stride = scene.FillVertexBuffer( (float*)videoMemoryPtr.ToPointer(), true, true, true, true );
        }
        GL.UnmapBuffer( BufferTarget.ArrayBuffer );
        GL.BindBuffer( BufferTarget.ArrayBuffer, 0 );
        GlInfo.LogError( "fill vertex-buffer" );

        // Index buffer
        GL.BindBuffer( BufferTarget.ElementArrayBuffer, VBOid[ 1 ] );
        GL.BufferData( BufferTarget.ElementArrayBuffer, (IntPtr)(scene.Triangles * 3 * sizeof( uint )), IntPtr.Zero, BufferUsageHint.StaticDraw );
        videoMemoryPtr = GL.MapBuffer( BufferTarget.ElementArrayBuffer, BufferAccess.WriteOnly );
        unsafe
        {
          scene.FillIndexBuffer( (uint*)videoMemoryPtr.ToPointer() );
        }
        GL.UnmapBuffer( BufferTarget.ElementArrayBuffer );
        GL.BindBuffer( BufferTarget.ElementArrayBuffer, 0 );
        GlInfo.LogError( "fill index-buffer" );
      }
      else
      {
        if ( useVBO )
        {
          GL.BindBuffer( BufferTarget.ArrayBuffer, VBOid[ 0 ] );
          GL.BufferData( BufferTarget.ArrayBuffer, (IntPtr)0, IntPtr.Zero, BufferUsageHint.StaticDraw );
          GL.BindBuffer( BufferTarget.ArrayBuffer, 0 );
          GL.BindBuffer( BufferTarget.ElementArrayBuffer, VBOid[ 1 ] );
          GL.BufferData( BufferTarget.ElementArrayBuffer, (IntPtr)0, IntPtr.Zero, BufferUsageHint.StaticDraw );
          GL.BindBuffer( BufferTarget.ElementArrayBuffer, 0 );
        }
      }
    }

    // appearance:
    Vector3 globalAmbient = new Vector3(  0.2f,  0.2f,  0.2f );
    Vector3 matAmbient    = new Vector3(  0.8f,  0.6f,  0.2f );
    Vector3 matDiffuse    = new Vector3(  0.8f,  0.6f,  0.2f );
    Vector3 matSpecular   = new Vector3(  0.8f,  0.8f,  0.8f );
    float   matShininess  = 100.0f;
    Vector3 whiteLight    = new Vector3(  1.0f,  1.0f,  1.0f );
    Vector3 lightPosition = new Vector3(-20.0f, 10.0f, 10.0f );
    Vector3 eyePosition   = new Vector3(  0.0f,  0.0f, 10.0f );

    void SetLightEye ( float size )
    {
      size += size;
      lightPosition = new Vector3( -2.0f * size, size, size );
      eyePosition   = new Vector3(         0.0f, 0.0f, size );
    }

    // attribute/vertex arrays:
    bool vertexAttribOn = false;
    bool vertexPointerOn = false;

    private void SetVertexAttrib ( bool on )
    {
      if ( vertexAttribOn == on )
        return;

      if ( activeProgram != null )
        if ( on )
          activeProgram.EnableVertexAttribArrays();
        else
          activeProgram.DisableVertexAttribArrays();

      vertexAttribOn = on;
    }

    private void SetVertexPointer ( bool on )
    {
      if ( vertexPointerOn == on )
        return;

      if ( on )
      {
        GL.EnableClientState( ArrayCap.VertexArray );
        if ( scene.TxtCoords > 0 )
          GL.EnableClientState( ArrayCap.TextureCoordArray );
        if ( scene.Normals > 0 )
          GL.EnableClientState( ArrayCap.NormalArray );
        if ( scene.Colors > 0 )
          GL.EnableClientState( ArrayCap.ColorArray );
      }
      else
      {
        GL.DisableClientState( ArrayCap.VertexArray );
        GL.DisableClientState( ArrayCap.TextureCoordArray );
        GL.DisableClientState( ArrayCap.NormalArray );
        GL.DisableClientState( ArrayCap.ColorArray );
      }

      vertexPointerOn = on;
    }

    void InitShaderRepository ()
    {
      programs.Clear();
      GlProgramInfo pi;

      // default program:
      pi = new GlProgramInfo( "default", new GlShaderInfo[] {
        new GlShaderInfo( ShaderType.VertexShader, "vertex.glsl", "086shader" ),
        new GlShaderInfo( ShaderType.FragmentShader, "fragment.glsl", "086shader" ) } );
      programs[ pi.name ] = pi;

      // put more programs here:
      // pi = new GlProgramInfo( ..
      //   ..
      // programs[ pi.name ] = pi;
    }

    /// <summary>
    /// Rendering one frame.
    /// </summary>
    private void Render ()
    {
      if ( !loaded )
        return;

      frameCounter++;
      useShaders = (scene != null) &&
                   scene.Triangles > 0 &&
                   useVBO &&
                   canShaders &&
                   activeProgram != null &&
                   checkShaders.Checked;

      GL.Clear( ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit );
      GL.ShadeModel( checkSmooth.Checked ? ShadingModel.Smooth : ShadingModel.Flat );
      GL.PolygonMode( checkTwosided.Checked ? MaterialFace.FrontAndBack : MaterialFace.Front,
                      checkWireframe.Checked ? PolygonMode.Line : PolygonMode.Fill );
      if ( checkTwosided.Checked )
        GL.Disable( EnableCap.CullFace );
      else
        GL.Enable( EnableCap.CullFace );

      SetCamera();
      RenderScene();

      glControl1.SwapBuffers();
    }

    /// <summary>
    /// Rendering code itself (separated for clarity).
    /// </summary>
    void RenderScene ()
    {
      // Scene rendering:
      if ( scene != null &&
           scene.Triangles > 0 &&        // scene is nonempty => render it
           useVBO )
      {
        // [txt] [colors] [normals] vertices
        GL.BindBuffer( BufferTarget.ArrayBuffer, VBOid[ 0 ] );
        IntPtr p = IntPtr.Zero;

        if ( useShaders )
        {
          SetVertexPointer( false );
          SetVertexAttrib( true );

          // using GLSL shaders:
          GL.UseProgram( activeProgram.Id );

          // uniforms:
          Matrix4 modelView    = GetModelView();
          Matrix4 modelViewInv = GetModelViewInv();
          Vector3 relEye       = Vector3.TransformVector( eyePosition, modelViewInv );
          GL.UniformMatrix4( activeProgram.GetUniform( "matrixModelView" ), false, ref modelView );
          if ( perspective )
            GL.UniformMatrix4( activeProgram.GetUniform( "matrixProjection" ), false, ref perspectiveProjection );
          else
            GL.UniformMatrix4( activeProgram.GetUniform( "matrixProjection" ), false, ref ortographicProjection );

          GL.Uniform3( activeProgram.GetUniform( "globalAmbient" ), ref globalAmbient );
          GL.Uniform3( activeProgram.GetUniform( "lightColor" ),    ref whiteLight );
          GL.Uniform3( activeProgram.GetUniform( "lightPosition" ), ref lightPosition );
          GL.Uniform3( activeProgram.GetUniform( "eyePosition" ),   ref relEye );
          GL.Uniform3( activeProgram.GetUniform( "Ka" ),            ref matAmbient );
          GL.Uniform3( activeProgram.GetUniform( "Kd" ),            ref matDiffuse );
          GL.Uniform3( activeProgram.GetUniform( "Ks" ),            ref matSpecular );
          GL.Uniform1( activeProgram.GetUniform( "shininess" ),     matShininess );

          // color handling:
          bool useGlobalColor = checkGlobalColor.Checked;
          if ( !scene.HasColors() )
            useGlobalColor = true;
          GL.Uniform1( activeProgram.GetUniform( "globalColor" ), useGlobalColor ? 1 : 0 );
          GlInfo.LogError( "set-uniforms" );

          // texture handling:
          bool useTexture = checkTexture.Checked;
          if ( !scene.HasTxtCoords() )
            useTexture = false;
          GL.Uniform1( activeProgram.GetUniform( "useTexture" ), useTexture ? 1 : 0 );
          GL.Uniform1( activeProgram.GetUniform( "texSurface" ), 0 );
          if ( useTexture )
          {
            GL.ActiveTexture( TextureUnit.Texture0 );
            GL.BindTexture( TextureTarget.Texture2D, texName );
          }
          GlInfo.LogError( "set-texture" );

          if ( activeProgram.HasAttribute( "texCoords" ) )
          {
            GL.VertexAttribPointer( activeProgram.GetAttribute( "texCoords" ), 2, VertexAttribPointerType.Float, false, stride, p );
            if ( scene.HasTxtCoords() )
              p += Vector2.SizeInBytes;
          }

          if ( activeProgram.HasAttribute( "color" ) )
          {
            GL.VertexAttribPointer( activeProgram.GetAttribute( "color" ), 3, VertexAttribPointerType.Float, false, stride, p );
            if ( scene.HasColors() )
              p += Vector3.SizeInBytes;
          }

          if ( activeProgram.HasAttribute( "normal" ) )
          {
            GL.VertexAttribPointer( activeProgram.GetAttribute( "normal" ), 3, VertexAttribPointerType.Float, false, stride, p );
            if ( scene.HasNormals() )
              p += Vector3.SizeInBytes;
          }

          GL.VertexAttribPointer( activeProgram.GetAttribute( "position" ), 3, VertexAttribPointerType.Float, false, stride, p );
          GlInfo.LogError( "set-attrib-pointers" );

          // index buffer
          GL.BindBuffer( BufferTarget.ElementArrayBuffer, VBOid[ 1 ] );

          // engage!
          GL.DrawElements( PrimitiveType.Triangles, scene.Triangles * 3, DrawElementsType.UnsignedInt, IntPtr.Zero );
          GlInfo.LogError( "draw-elements-shader" );
          GL.UseProgram( 0 );
        }
        else
        {
          SetVertexAttrib( false );
          SetVertexPointer( true );

          // using FFP:
          if ( scene.HasTxtCoords() )
          {
            GL.TexCoordPointer( 2, TexCoordPointerType.Float, stride, p );
            p += Vector2.SizeInBytes;
          }

          if ( scene.HasColors() )
          {
            GL.ColorPointer( 3, ColorPointerType.Float, stride, p );
            p += Vector3.SizeInBytes;
          }

          if ( scene.HasNormals() )
          {
            GL.NormalPointer( NormalPointerType.Float, stride, p );
            p += Vector3.SizeInBytes;
          }

          GL.VertexPointer( 3, VertexPointerType.Float, stride, p );

          // index buffer
          GL.BindBuffer( BufferTarget.ElementArrayBuffer, VBOid[ 1 ] );

          // engage!
          GL.DrawElements( PrimitiveType.Triangles, scene.Triangles * 3, DrawElementsType.UnsignedInt, IntPtr.Zero );
          GlInfo.LogError( "draw-elements-ffp" );
        }

        triangleCounter += scene.Triangles;
      }
      else                              // color cube
      {
        SetVertexPointer( false );
        SetVertexAttrib( false );

        GL.Begin( PrimitiveType.Quads );

        GL.Color3( 0.0f, 1.0f, 0.0f );          // Set The Color To Green
        GL.Vertex3( 1.0f, 1.0f, -1.0f );        // Top Right Of The Quad (Top)
        GL.Vertex3( -1.0f, 1.0f, -1.0f );       // Top Left Of The Quad (Top)
        GL.Vertex3( -1.0f, 1.0f, 1.0f );        // Bottom Left Of The Quad (Top)
        GL.Vertex3( 1.0f, 1.0f, 1.0f );         // Bottom Right Of The Quad (Top)

        GL.Color3( 1.0f, 0.5f, 0.0f );          // Set The Color To Orange
        GL.Vertex3( 1.0f, -1.0f, 1.0f );        // Top Right Of The Quad (Bottom)
        GL.Vertex3( -1.0f, -1.0f, 1.0f );       // Top Left Of The Quad (Bottom)
        GL.Vertex3( -1.0f, -1.0f, -1.0f );      // Bottom Left Of The Quad (Bottom)
        GL.Vertex3( 1.0f, -1.0f, -1.0f );       // Bottom Right Of The Quad (Bottom)

        GL.Color3( 1.0f, 0.0f, 0.0f );          // Set The Color To Red
        GL.Vertex3( 1.0f, 1.0f, 1.0f );         // Top Right Of The Quad (Front)
        GL.Vertex3( -1.0f, 1.0f, 1.0f );        // Top Left Of The Quad (Front)
        GL.Vertex3( -1.0f, -1.0f, 1.0f );       // Bottom Left Of The Quad (Front)
        GL.Vertex3( 1.0f, -1.0f, 1.0f );        // Bottom Right Of The Quad (Front)

        GL.Color3( 1.0f, 1.0f, 0.0f );          // Set The Color To Yellow
        GL.Vertex3( 1.0f, -1.0f, -1.0f );       // Bottom Left Of The Quad (Back)
        GL.Vertex3( -1.0f, -1.0f, -1.0f );      // Bottom Right Of The Quad (Back)
        GL.Vertex3( -1.0f, 1.0f, -1.0f );       // Top Right Of The Quad (Back)
        GL.Vertex3( 1.0f, 1.0f, -1.0f );        // Top Left Of The Quad (Back)

        GL.Color3( 0.0f, 0.0f, 1.0f );          // Set The Color To Blue
        GL.Vertex3( -1.0f, 1.0f, 1.0f );        // Top Right Of The Quad (Left)
        GL.Vertex3( -1.0f, 1.0f, -1.0f );       // Top Left Of The Quad (Left)
        GL.Vertex3( -1.0f, -1.0f, -1.0f );      // Bottom Left Of The Quad (Left)
        GL.Vertex3( -1.0f, -1.0f, 1.0f );       // Bottom Right Of The Quad (Left)

        GL.Color3( 1.0f, 0.0f, 1.0f );          // Set The Color To Violet
        GL.Vertex3( 1.0f, 1.0f, -1.0f );        // Top Right Of The Quad (Right)
        GL.Vertex3( 1.0f, 1.0f, 1.0f );         // Top Left Of The Quad (Right)
        GL.Vertex3( 1.0f, -1.0f, 1.0f );        // Bottom Left Of The Quad (Right)
        GL.Vertex3( 1.0f, -1.0f, -1.0f );       // Bottom Right Of The Quad (Right)

        GL.End();

        triangleCounter += 12;
      }
    }
  }
}