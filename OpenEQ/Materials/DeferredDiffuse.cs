﻿using System.Linq;
using System.Numerics;
using ImageLib;
using OpenEQ.Engine;
using OpenTK.Graphics.OpenGL4;

namespace OpenEQ.Materials {
	public class DeferredDiffuseMaterial : Material {
		public override bool Deferred => true;
		static Program Program;
		
		readonly Texture[] Textures;
		readonly float AnimationSpeed;
		
		public DeferredDiffuseMaterial(Image[] images, float animationSpeed = 0) {
			Textures = images.Select(image => new Texture(image, false)).ToArray();
			AnimationSpeed = animationSpeed;

			if(Program == null) {
				Program = new Program(@"
#version 410
precision highp float;
layout (location = 0) in vec4 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in mat4 aModelMat;
uniform mat4 uProjectionViewMat;
out vec2 vTexCoord;
out vec3 vNormal;
void main() {
	gl_Position = uProjectionViewMat * aModelMat * aPosition;
	vTexCoord = aTexCoord;
	mat3 nmat = transpose(inverse(mat3(aModelMat)));
	vNormal = normalize(nmat * aNormal);
}
					", @"
#version 410
precision highp float;
in vec2 vTexCoord;
in vec3 vNormal;
layout (location = 0) out vec4 color;
layout (location = 1) out vec3 normal;
uniform sampler2D uTex;
void main() {
	color = texture(uTex, vTexCoord);
	color.a = 0;
	normal = vNormal;
}
				");
				Program.SetUniform("uTex", 0);
			}
		}

		public override void Use(Matrix4x4 projView) {
			Program.Use();
			Program.SetUniform("uProjectionViewMat", projView);
			GL.ActiveTexture(TextureUnit.Texture0);
			if(AnimationSpeed == 0)
				Textures[0].Use();
			else
				Textures[(int) (Globals.Time / AnimationSpeed) % Textures.Length].Use();
		}
	}
}