﻿using System;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace MathSupport
{
  class Ellipse
  {
    float a, b, c;
    Vector3 center;

    // Sphere constructor
    public Ellipse ( float r, Vector3 center ) : this( r, r, r, center )
    {
    }

    // Ellipse constructor
    public Ellipse ( float a, float b, float c, Vector3 center )
    {
      this.a = a;
      this.b = b;
      this.c = c;
      this.center = center;
    }

    // "polar coordinates" method
    public Vector3 IntersectionI ( float x, float y )
    {
      Vector3d o = new Vector3d( 0, 0, -c );
      Vector3d m = new Vector3d( x - center.X, y - center.Y, c );
      Vector3d v = o - m;
      v.Normalize();
      double A = v.X * v.X * b * b * c * c + v.Y * v.Y * a * a * c * c + v.Z * v.Z * a * a * b * b;
      double B = 2 * (v.X * b * b * c * c + v.Y * a * a * c * c + v.Z * a * a * b * b);
      double C = v.X * v.X * b * b * c * c + v.Y * v.Y * a * a * c * c + v.Z * a * a * b * b - a * a * b * b * c * c;
      double D = Math.Sqrt( B * B - 4 * A * C );
      double t = (-B - D) / (2 * A);
      double X = m.X + t * v.X;
      double Y = m.Y + t * v.Y;
      double Z = m.Z + t * v.Z;
      return new Vector3( (float)X, -(float)Y, (float)Z );
    }

    // "parallel rays" method
    public Vector3? Intersection ( float x, float y, bool restricted )
    {
      x -= center.X;
      y -= center.Y;

      if ( (x < -a) || (x > a) || (y < -b) || (y > b) )
      {
        float x1 = (float)Math.Sqrt( (a * a * b * b * y * y) / (b * b * y * y + x * x) );
        float x2 = -x1;
        float y1 = (y * x1) / -x;
        float y2 = (y * x2) / -x;
        if ( Math.Abs( x - x1 ) < Math.Abs( x - x2 ) )
          return new Vector3( x1, y1, 0 );
        else
          return new Vector3( x2, y2, 0 );
      }

      float z = (1 - (x * x) / (a * a) - (y * y) / (b * b)) * c * c;
      if ( z < 0 )
        return null;
      z = (float)Math.Sqrt( z );
      return new Vector3( x, -y, z );
    }
  }

  /// <summary>
  /// Trackball interactive 3D scene navigation
  /// Original code: Matyas Brenner
  /// </summary>
  public class Trackball
  {
    /// <summary>
    /// Center of the rotation (world coords).
    /// </summary>
    public Vector3 Center
    {
      get;
      set;
    }

    /// <summary>
    /// Scene diameter (for default zoom factor only).
    /// </summary>
    private float diameter = 5.0f;

    /// <summary>
    /// Inital camera position (world coords).
    /// </summary>
    private Vector3 eye0 = new Vector3( 0.0f, 0.0f, 10.0f );

    /// <summary>
    /// Current camera position (world coords).
    /// </summary>
    public Vector3 Eye
    {
      get
      {
        return Vector3.TransformVector( eye0, ModelViewInv );
      }
    }

    /// <summary>
    /// Vertical field-of-view angle in radians.
    /// </summary>
    private float fov = 1.0f;

    /// <summary>
    /// Zoom factor (multiplication).
    /// </summary>
    float zoom = 1.0f;

    public Trackball ( Vector3 cent, float diam =5.0f )
    {
      Center   = cent;
      diameter = diam;
      eye0     = cent + new Vector3( 0.0f, 0.0f, 2.0f * diam );
    }

    Matrix4 prevRotation = Matrix4.Identity;
    Matrix4 rotation     = Matrix4.Identity;

    Ellipse ellipse;
    Vector3? a, b;

    Matrix4 perspectiveProjection;
    Matrix4 ortographicProjection;

    /// <summary>
    /// Perspective / orthographic projection?
    /// </summary>
    bool perspective = true;

    public Matrix4 PerspectiveProjection
    {
      get
      {
        return perspectiveProjection;
      }
    }

    public Matrix4 OrthographicProjection
    {
      get
      {
        return ortographicProjection;
      }
    }

    public Matrix4 Projection
    {
      get
      {
        return perspective ? perspectiveProjection : ortographicProjection;
      }
    }

    float minZoom =  0.2f;
    float maxZoom = 20.0f;

    /// <summary>
    /// Sets up a projective viewport
    /// </summary>
    public void GLsetupViewport ( int width, int height )
    {
      // 1. set ViewPort transform:
      GL.Viewport( 0, 0, width, height );

      // 2. set projection matrix
      perspectiveProjection = Matrix4.CreatePerspectiveFieldOfView( fov, width / (float)height, 0.01f, 1000.0f );
      float minSize = 2.0f * Math.Min( width, height );
      ortographicProjection = Matrix4.CreateOrthographic( diameter * width / minSize,
                                                          diameter * height / minSize,
                                                          0.01f, 1000.0f );
      GLsetProjection();
      setEllipse( width, height );
    }

    /// <summary>
    /// Setup of a camera called for every frame prior to any rendering.
    /// </summary>
    public void GLsetCamera ()
    {
      // not needed if shaders are active .. but doesn't make any harm..
      Matrix4 modelview = ModelView;
      GL.MatrixMode( MatrixMode.Modelview );
      GL.LoadMatrix( ref modelview );
    }

    public Matrix4 ModelView
    {
      get
      {
        return Matrix4.CreateTranslation( -Center ) *
               Matrix4.CreateScale( zoom / diameter ) *
               prevRotation *
               rotation *
               Matrix4.CreateTranslation( 0.0f, 0.0f, -1.5f );
      }
    }

    public Matrix4 ModelViewInv
    {
      get
      {
        Matrix4 rot = prevRotation * rotation;
        rot.Transpose();

        return Matrix4.CreateTranslation( 0.0f, 0.0f, 1.5f ) *
               rot *
               Matrix4.CreateScale( diameter / zoom ) *
               Matrix4.CreateTranslation( Center );
      }
    }

    public void Reset ()
    {
      zoom         = 1.0f;
      rotation     = Matrix4.Identity;
      prevRotation = Matrix4.Identity;
    }

    private void setEllipse ( int width, int height )
    {
      width  /= 2;
      height /= 2;

      ellipse = new Ellipse( Math.Min( width, height ), new Vector3( width, height, 0 ) );
    }

    private Matrix4 calculateRotation ( Vector3? a, Vector3? b, bool sensitive )
    {
      if ( !a.HasValue || !b.HasValue )
        return rotation;

      Vector3 axis = Vector3.Cross( a.Value, b.Value );
      float angle = Vector3.CalculateAngle( a.Value, b.Value );
      if ( sensitive )
        angle *= 0.4f;
      return Matrix4.CreateFromAxisAngle( axis, angle );
    }

    public void GLtogglePerspective ()
    {
      perspective = !perspective;
      GLsetProjection();
    }

    public void GLsetProjection ()
    {
      // not needed if shaders are active .. but doesn't make any harm..
      GL.MatrixMode( MatrixMode.Projection );
      if ( perspective )
        GL.LoadMatrix( ref perspectiveProjection );
      else
        GL.LoadMatrix( ref ortographicProjection );
    }

    //--- GUI interaction ---

    public void MouseDown ( MouseEventArgs e )
    {
      a = ellipse.IntersectionI( e.X, e.Y );
    }

    public void MouseUp ( MouseEventArgs e )
    {
      prevRotation *= rotation;
      rotation = Matrix4.Identity;
      a = null;
      b = null;
    }

    public void MouseMove ( MouseEventArgs e )
    {
      if ( e.Button != MouseButtons.Left )
        return;

       
      b = ellipse.IntersectionI( e.X, e.Y );
      rotation = calculateRotation( a, b, (Control.ModifierKeys & Keys.Shift) != Keys.None );
    }

    public void MouseWheel ( MouseEventArgs e )
    {
      float dZoom = -e.Delta / 120.0f;
      zoom *= (float)Math.Pow( 1.04, dZoom );

      // zoom bounds:
      zoom = Arith.Clamp( zoom, minZoom, maxZoom );
    }

    public void KeyDown ( KeyEventArgs e )
    {
      // nothing yet
    }

    public void KeyUp ( KeyEventArgs e )
    {
      if ( e.KeyCode == Keys.O )
      {
        e.Handled = true;
        GLtogglePerspective();
      }
    }
  }
}
