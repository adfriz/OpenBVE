//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#version 410 core
precision highp int;
precision highp float;

struct Light
{
	vec3 position;
	vec3 ambient;
	vec3 diffuse;
	vec3 specular;
	vec4 lightModel;
};

struct MaterialColor
{
	vec4 ambient;
	vec4 diffuse;
	vec4 specular;
	vec3 emission;
	float shininess;
};

layout(location = 0) in vec3 iPosition;
layout(location = 1) in vec3 iNormal;
layout(location = 2) in vec2 iUv;
layout(location = 3) in vec4 iColor;
layout(location = 4) in ivec3 iMatrixChain;

uniform mat4 uCurrentProjectionMatrix;
uniform mat4 uCurrentModelViewMatrix;
uniform mat3 uNormalMatrix;
uniform mat4 uCurrentTextureMatrix;

uniform bool uIsLight;
uniform Light uLight;
uniform MaterialColor uMaterial;
uniform int uMaterialFlags;

layout (std140) uniform uAnimationMatricies {
    mat4 modelMatricies[128];
};

layout (std140) uniform uInstanceMatricies {
    mat4 instanceMatricies[128];
    mat4 instanceNormalMatricies[128];
};

out vec4 oViewPos;
out vec2 oUv;
out vec4 oColor;
out vec4 oLightResult;

vec4 getLightResult(mat3 normalMatrix)
{
	vec3 normal = normalize(normalMatrix * vec3(iNormal.x, iNormal.y, -iNormal.z));
	float nDotVP = max(0.0, dot(normal, normalize(vec3(uLight.position))));
	float nDotHV = max(0.0, dot(normal, normalize(vec3(oViewPos.xyz + uLight.position))));
	float pf = nDotVP == 0.0 ? 0.0 : pow(nDotHV, uMaterial.shininess);

	vec3 lightColor = uLight.ambient * uMaterial.ambient.rgb + uLight.diffuse * uMaterial.diffuse.rgb * nDotVP + uLight.specular * uMaterial.specular.rgb * pf;
	vec4 sceneColor = (uMaterialFlags & 1) != 0 ? vec4(uMaterial.emission, 1.0) + uMaterial.ambient * uLight.lightModel : uLight.lightModel;
	return clamp(sceneColor + vec4(lightColor, uMaterial.diffuse.a), 0.0, 1.0);
}

vec3 transformVector(vec3 initialVector, int matrixIndex)
{
	float X = (initialVector.x * modelMatricies[matrixIndex][0].x) + (initialVector.y * modelMatricies[matrixIndex][1].x) + (initialVector.z * modelMatricies[matrixIndex][2].x);
	float Y = (initialVector.x * modelMatricies[matrixIndex][0].y) + (initialVector.y * modelMatricies[matrixIndex][1].y) + (initialVector.z * modelMatricies[matrixIndex][2].y);
	float Z = (initialVector.x * modelMatricies[matrixIndex][0].z) + (initialVector.y * modelMatricies[matrixIndex][1].z) + (initialVector.z * modelMatricies[matrixIndex][2].z);
	// ignoreW per DirectX

	X += 1 * modelMatricies[matrixIndex][3].x;
	Y += 1 * modelMatricies[matrixIndex][3].y;
	Z += 1 * modelMatricies[matrixIndex][3].z;
	return vec3(X, Y, Z);
}

void main()
{
	vec3 pos = vec3(iPosition);
	oColor = iColor;

	if(iMatrixChain.x != 0)
	{	
		// unpack packed matrix indicies
		int m0 = (iMatrixChain.x & (0xff << 24)) >> 24;
		int m1 = (iMatrixChain.x >> 16) & 0xff;
		int m2 = (iMatrixChain.x & 0xff00) >> 8;
		int m3 = (iMatrixChain.x & 0xff);
        
		if(m0 >=0 && m0 < 255)
		{	
			pos = transformVector(pos, m0);
		}
		
		if(m1 >=0 && m1 < 255)
		{
			pos = transformVector(pos, m1);
		}
		
		if(m2 >=0 && m2 < 255)
		{
			pos = transformVector(pos, m2);
		}

		if(m3 >=0 && m3 < 255)
		{
			pos = transformVector(pos, m3);
		}		
	}
	
	if(iMatrixChain.y != 0)
	{	
		// unpack packed matrix indicies
		int m0 = (iMatrixChain.y & (0xff << 24)) >> 24;
		int m1 = (iMatrixChain.y >> 16) & 0xff;
		int m2 = (iMatrixChain.y & 0xff00) >> 8;
		int m3 = (iMatrixChain.y & 0xff);
        
		if(m0 >=0 && m0 < 255)
		{	
			pos = transformVector(pos, m0);
		}
		
		if(m1 >=0 && m1 < 255)
		{
			pos = transformVector(pos, m1);
		}
		
		if(m2 >=0 && m2 < 255)
		{
			pos = transformVector(pos, m2);
		}

		if(m3 >=0 && m3 < 255)
		{
			pos = transformVector(pos, m3);
		}		
	}

	if(iMatrixChain.z != 0)
	{	
		// unpack packed matrix indicies
		int m0 = (iMatrixChain.z & (0xff << 24)) >> 24;
		int m1 = (iMatrixChain.z >> 16) & 0xff;
		int m2 = (iMatrixChain.z & 0xff00) >> 8;
		int m3 = (iMatrixChain.z & 0xff);
        
		if(m0 >=0 && m0 < 255)
		{	
			pos = transformVector(pos, m0);
		}
		
		if(m1 >=0 && m1 < 255)
		{
			pos = transformVector(pos, m1);
		}
		
		if(m2 >=0 && m2 < 255)
		{
			pos = transformVector(pos, m2);
		}

		if(m3 >=0 && m3 < 255)
		{
			pos = transformVector(pos, m3);
		}		
	}
	
	pos.z = -pos.z;
	vec4 transformedPosition = vec4(pos, 1.0);

	// Hardware Instancing Support
	// If flag 256 is set, use gl_InstanceID to pick the instance matrix
	if((uMaterialFlags & 256) != 0)
	{
		mat4 instMatrix = instanceMatricies[gl_InstanceID];
		oViewPos = uCurrentModelViewMatrix * (instMatrix * transformedPosition);
	}
	else
	{
		oViewPos = uCurrentModelViewMatrix * transformedPosition;
	}
	gl_Position = uCurrentProjectionMatrix * oViewPos;

	oUv = (uCurrentTextureMatrix * vec4(iUv, 1.0, 1.0)).xy;
	
	mat3 effectiveNormalMatrix = uNormalMatrix;
	if((uMaterialFlags & 256) != 0)
	{
		effectiveNormalMatrix = mat3(instanceNormalMatricies[gl_InstanceID]);
	}

	oLightResult = uIsLight && (uMaterialFlags & 4) == 0 ? getLightResult(effectiveNormalMatrix) : uMaterial.ambient;
}
