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
	internal enum TrainCommand
	{
		/// <summary>Sets the interval between preceding AI trains</summary>
		Interval,
		/// <summary>Sets the max speed of an AI train</summary>
		Velocity,
		/// <summary>Sets the folder used for the player train</summary>
		Folder,
		/// <summary>Sets the folder used for the player train</summary>
		File,
		/// <summary>Sets the run sound played for Rail with the structure index N</summary>
		Run,
		/// <summary>Sets the run sound played for Rail with the structure index N</summary>
		Rail,
		/// <summary>Sets the flange sound played for Rail with the structure index N</summary>
		Flange,
		/// <summary>Sets the daytime timetable image</summary>
		TimetableDay,
		/// <summary>Sets the nighttime timetable image</summary>
		TimetableNight,
		/// <summary>Sets the initial destination value</summary>
		Destination,
		/*
		 * RW commands, currently unsupported
		 */
		/// <summary>Sets the stopping frequency of the train in front</summary>
		/// <remarks>Unsure as to effects at present</remarks>
		Station,
		/// <summary>Sets the acceleration of the train in front</summary>
		/// <remarks>Appears to be a constant acceleration value at all times.
		/// As we simulate the train fully, it's not really useful.</remarks>
		Acceleration,
		/// <summary>Contains a URL where the train may be downloaded if not currently possessed by the player</summary>
		DownloadLocation
	}
}
