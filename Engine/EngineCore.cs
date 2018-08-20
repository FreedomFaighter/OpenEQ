﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Jitter;
using Jitter.Collision;
using Jitter.Collision.Shapes;
using Jitter.Dynamics;
using Jitter.LinearMath;
using NsimGui;
using NsimGui.Widgets;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;
using OpenEQ.Common;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using static OpenEQ.Engine.Globals;

namespace OpenEQ.Engine {
	public partial class EngineCore : GameWindow {
		bool DeferredEnabled;
		
		public readonly Gui Gui;
		
		readonly List<Model> Models = new List<Model>();
		readonly List<AniModelInstance> AniModels = new List<AniModelInstance>();
		readonly List<double> FrameTimes = new List<double>();
		readonly List<PointLight> Lights = new List<PointLight>();

		public double FPS => FrameTimes.Count == 0 ? 0 : 1 / (FrameTimes.Sum() / FrameTimes.Count);

		Matrix4x4 ProjectionView;

		World World;
		
		public EngineCore() : base(
			1280, 720, new GraphicsMode(new ColorFormat(8, 8, 8, 8), 16, 0), "OpenEQ", 
			GameWindowFlags.Default, DisplayDevice.Default, 4, 1, GraphicsContextFlags.ForwardCompatible
		) {
			VSync = VSyncMode.Off;
			Stopwatch.Start();
			Gui = new Gui(new GuiRenderer());
			
			MouseMove += (_, e) => Gui.MousePosition = (e.X, e.Y);
			MouseDown += (_, e) => UpdateMouseButton(e.Button, true);
			MouseUp += (_, e) => UpdateMouseButton(e.Button, false);
			MouseWheel += (_, e) => Gui.WheelDelta += e.Delta;

			Resize += (_, e) => {
				Gui.Dimensions = new Vector2(Width, Height);
				Gui.Scale = new Vector2(1.5f);
				ProjectionMat = Matrix4x4.CreatePerspectiveFieldOfView(45 * (MathF.PI / 180), (float) Width / Height, 1, 5000);
			};
		}

		public void AddLight(Vector3 pos, float radius, float attenuation, Vector3 color) =>
			Lights.Add(new PointLight(pos, radius, attenuation, color));

		public void Add(Model model) => Models.Add(model);
		public void Add(AniModelInstance modelInstance) => AniModels.Add(modelInstance);

		public void Start() {
			World = new World(new CollisionSystemSAP()) { Gravity = new JVector(0, 0, 1) };

			var ov = new List<JVector>();
			var oi = new List<TriangleVertexIndices>();
			Console.WriteLine("Building mesh for physics");
			foreach(var model in Models) {
				if(!model.IsFixed) continue;
				foreach(var mesh in model.Meshes) {
					if(!mesh.IsCollidable) continue;
					var (pv, pi) = mesh.PhysicsMesh;
					foreach(var mat in mesh.ModelMatrices) {
						var tv = mat == Matrix4x4.Identity
							? (IEnumerable<Vector3>) pv
							: pv.AsParallel().AsOrdered().Select(x => Vector3.Transform(x, mat));
						var offset = ov.Count;
						ov.AddRange(tv.Select(x => new JVector(x.X, x.Y, x.Z)));
						oi.AddRange(pi.Select(x => new TriangleVertexIndices(x.I0 + offset, x.I1 + offset, x.I2 + offset)));
					}
				}
			}
			
			Console.WriteLine($"Building octree for {ov.Count} vertices across {oi.Count} triangles");
			var octree = new Octree(ov, oi);
			Console.WriteLine("Built octree");
			
			//World.AddBody(new RigidBody(new TriangleMeshShape(octree)) { IsStatic = true });
			
			Run();
		}

		void UpdateMouseButton(MouseButton button, bool state) {
			switch(button) {
				case MouseButton.Left:
					Gui.MouseLeft = state;
					break;
				case MouseButton.Right:
					Gui.MouseRight = state;
					break;
			}
		}
		
		readonly Dictionary<Key, bool> KeyState = new Dictionary<Key, bool>();

		protected override void OnKeyDown(KeyboardKeyEventArgs e) {
			switch(e.Key) {
				case Key.L:
					DeferredEnabled = !DeferredEnabled;
					break;
				default:
					KeyState[e.Key] = true;
					break;
			}
		}

		protected override void OnKeyUp(KeyboardKeyEventArgs e) => KeyState.Remove(e.Key);

		protected override void OnUpdateFrame(FrameEventArgs e) {
			World.Step((float) e.Time, true);
			
			var movement = vec3();
			var movescale = KeyState.Keys.Contains(Key.ShiftLeft) ? 250 : 30;
			var pitchscale = .5f;
			var yawscale = 1.25f;
			var updatedCamera = false;
			foreach(var key in KeyState.Keys)
				switch(key) {
					case Key.W:
						movement += vec3(0, (float) e.Time * movescale, 0);
						break;
					case Key.S:
						movement += vec3(0, (float) -e.Time * movescale, 0);
						break;
					case Key.A:
						movement += vec3((float) -e.Time * movescale, 0, 0);
						break;
					case Key.D:
						movement += vec3((float) e.Time * movescale, 0, 0);
						break;
					case Key.E:
						movement += vec3(0, 0, (float) e.Time * movescale);
						break;
					case Key.Q:
						movement += vec3(0, 0, (float) -e.Time * movescale);
						break;
					case Key.Up:
						Camera.Look((float) e.Time * pitchscale, 0);
						updatedCamera = true;
						break;
					case Key.Down:
						Camera.Look((float) -e.Time * pitchscale, 0);
						updatedCamera = true;
						break;
					case Key.Left:
						Camera.Look(0, (float) e.Time * yawscale);
						updatedCamera = true;
						break;
					case Key.Right:
						Camera.Look(0, (float) -e.Time * yawscale);
						updatedCamera = true;
						break;
					case Key.Escape:
					case Key.Tilde:
						Exit();
						break;
				}
			if(movement.Length() > 0) {
				Camera.Move(movement);
				updatedCamera = true;
			}

			if(updatedCamera)
				Camera.Update();

			base.OnUpdateFrame(e);
		}

		protected override void OnRenderFrame(FrameEventArgs e) {
			FrameTime = Time;
			if(FrameTimes.Count == 200)
				FrameTimes.RemoveAt(0);
			FrameTimes.Add(e.Time);

			ProjectionView = FpsCamera.Matrix * ProjectionMat;

			if(DeferredEnabled) {
				SetupDeferredPathway();
				Profile("Deferred render", RenderDeferredPathway);
			}

			Profile("Forward render", () => {
				if(!DeferredEnabled) {
					GL.Viewport(0, 0, Width, Height);
					GL.ClearColor(0, 0, 0, 1);
					GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		
					GL.Enable(EnableCap.CullFace);
					GL.Disable(EnableCap.Blend);
					GL.Enable(EnableCap.DepthTest);
					Models.ForEach(model => model.Draw(ProjectionView, forward: false));
					AniModels.ForEach(model => model.Draw(ProjectionView, forward: false));
				}
				GL.Enable(EnableCap.Blend);
				GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
				GL.Enable(EnableCap.DepthTest);
				GL.ActiveTexture(TextureUnit.Texture0);
				GL.DepthMask(false);
				Models.ForEach(model => model.Draw(ProjectionView, forward: true));
				AniModels.ForEach(model => model.Draw(ProjectionView, forward: true));
				GL.DepthMask(true);
				GL.Finish();
			});

			Gui.Render((float) e.Time);

			SwapBuffers();
		}
	}
}