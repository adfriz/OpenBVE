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
	internal enum StructureCommand
	{
		/// <summary>The object used for RailType N</summary>
		Rail,
		/// <summary>The object used for BeaconType N</summary>
		Beacon,
		/// <summary>The object used for PoleType N</summary>
		Pole,
		/// <summary>The object used for Ground N</summary>
		Ground,
		/// <summary>The left Wall object for type N</summary>
		WallL,
		/// <summary>The right Wall object for type N</summary>
		WallR,
		/// <summary>The left Dike object for type N</summary>
		DikeL,
		/// <summary>The right Dike object for type N</summary>
		DikeR,
		/// <summary>The left Form object for type N</summary>
		FormL,
		/// <summary>The right Form object for type N</summary>
		FormR,
		/// <summary>The left FormCenter object for type N</summary>
		FormCL,
		/// <summary>The right FormCenter object for type N</summary>
		FormCR,
		/// <summary>The left FormRoof object for type N</summary>
		RoofL,
		/// <summary>The right FormRoof object for type N</summary>
		RoofR,
		/// <summary>The left FormRoofCenter object for type N</summary>
		RoofCL,
		/// <summary>The right FormRoofCenter object for type N</summary>
		RoofCR,
		/// <summary>The left Crack object for type N</summary>
		CrackL,
		/// <summary>The right Crack object for type N</summary>
		CrackR,
		/// <summary>The object used for FreeObject N</summary>
		FreeObj,
		/// <summary>The image / object used for Background N</summary>
		Background,
		/// <summary>The image / object used for Background N</summary>
		Back,
		/// <summary>The image / object used for Background N</summary>
		BackgroundX,
		/// <summary>The image / object used for Background N</summary>
		BackX,
		/// <summary>If Background N is an image, sets the aspect ratio used in wrapping</summary>
		BackgroundAspect,
		/// <summary>If Background N is an image, sets the aspect ratio used in wrapping</summary>
		BackAspect,
		/// <summary>The object used for RainType N</summary>
		Weather,
		/// <summary>Loads a dynamic lighting set</summary>
		DynamicLight,

		/*
		 * HMMSIM
		 */
		/// <summary>The object used for Object N</summary>
		/// <remarks>Equivalent to .FreeObj</remarks>
		Object

	}
}
