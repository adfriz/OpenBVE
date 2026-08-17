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
	internal enum RouteCommand
	{
		/// <summary>Used by BVE to allow for debugging, unused by OpenBVE</summary>
		DeveloperID,
		/// <summary>A textual description of the route to be displayed in the main menu</summary>
		Comment,
		/// <summary>An image of the route to be displayed in the main menu</summary>
		Image,
		/// <summary>The timetable image to be displayed in-cab</summary>
		TimeTable,
		/// <summary>The mode for thew train's safety system to start in</summary>
		Change,
		/// <summary>The rail gauge</summary>
		Gauge,
		/// <summary>Sets a speed limit for each signal aspect</summary>
		Signal,
		/// <summary>The acceleration due to gravity</summary>
		AccelerationDueToGravity,
		/// <summary>The game starting time</summary>
		StartTime,
		/// <summary>Sets the background to be displayed on loading screens</summary>
		LoadingScreen,
		/// <summary>Sets a custom unit of speed to be displayed in in-game messages</summary>
		DisplaySpeed,
		/// <summary>Sets briefing data</summary>
		Briefing,
		/// <summary>Sets the initial elevation above sea-level</summary>
		Elevation,
		/// <summary>Sets the initial air temperature</summary>
		Temperature,
		/// <summary>Sets the initial air pressure</summary>
		Pressure,
		/// <summary>Sets the ambient light color</summary>
		AmbientLight,
		/// <summary>Sets the directional light color</summary>
		DirectionalLight,
		/// <summary>Sets the position of the directional light</summary>
		LightDirection,
		/// <summary>Adds dynamic lighting</summary>
		DynamicLight,
		/// <summary>Sets the initial viewpoint for the camera</summary>
		InitialViewPoint,
		/// <summary>Adds AI trains</summary>
		TfoXML
	}
}
