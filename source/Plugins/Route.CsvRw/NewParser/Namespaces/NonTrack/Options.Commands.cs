//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2026, Christopher Lees, The OpenBVE Project
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
namespace CsvRwRouteParser.New {
	internal enum OptionsCommand
	{
		/// <summary>Sets the length of a block</summary>
		/// <remarks>Grounds, walls, dikes & poles repeat once per block</remarks>
		BlockLength,
		/// <summary>Controls the X Object parser in use</summary>
		XParser,
		/// <summary>Controls the Wavefront OBJ parser in use</summary>
		ObjParser,
		/// <summary>Sets the unit of length relative to 1m</summary>
		UnitOfLength,
		/// <summary>Sets the unit of speed relative to 1km/h</summary>
		UnitOfSpeed,
		/// <summary>Controls how objects are disposed after the camera passes their point of origin</summary>
		ObjectVisibility,
		/// <summary>Controls the behaviour of signalling sections</summary>
		SectionBehavior,
		/// <summary>Controls whether cant is expected to be signed</summary>
		CantBehavior,
		/// <summary>Controls the blending mode used for fog</summary>
		FogBehavior,
		/// <summary>Controls whether BVETS Hacks are forced on / off for this route</summary>
		EnableBveTsHacks,
		EnableHacks = EnableBveTsHacks,
		/// <summary>Controls whether the route is driven in the reverse direction to construction</summary>
		ReverseDirection,
		/// <summary>Controls whether fuzzy color matching with a reduced palette is used</summary>
		/// <remarks>This matches BVE2 / BVE4 behaviour, and should be disabled where possible</remarks>
		CompatibleTransparencyMode,
		/// <summary>Provides a 2D (X, Z) vector to set the starting direction of track element 0</summary>
		StartingDirection
	}
}
