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

namespace CsvRwRouteParser.New
{
    /// <summary>Identifies which section (namespace) a command belongs to.</summary>
    internal enum SectionKind
    {
        None, Options, Route, Track, Structure, Texture, Signal, Train, Cycle
    }

    /// <summary>
    /// A single parsed route command produced by the preprocessing (lexical) stage.
    /// Replaces the old per-line string scanning during dispatch and lets handlers
    /// switch on a resolved enum value instead of re-parsing the command text.
    /// </summary>
    internal readonly struct RouteToken
    {
        internal readonly SectionKind Section;
        internal readonly int CommandValue;       // resolved enum value, or -1 if unresolved
        internal readonly double TrackPosition;    // valid only if IsTrackPosition
        internal readonly bool IsTrackPosition;
        internal readonly bool IsSectionHeader;    // a [Section] line; no command
        internal readonly string[] Arguments;      // split, post-formation arguments (may be null)
        internal readonly string CommandName;      // resolved bare command name, or null
        internal readonly int[] CommandIndices;    // parenthesis indices (FindIndices)
        internal readonly string File;
        internal readonly int Line;
        internal readonly int Column;

        internal RouteToken(SectionKind section, int commandValue, double trackPosition, bool isTrackPosition,
            bool isSectionHeader, string[] arguments, string commandName, int[] commandIndices,
            string file, int line, int column)
        {
            Section = section;
            CommandValue = commandValue;
            TrackPosition = trackPosition;
            IsTrackPosition = isTrackPosition;
            IsSectionHeader = isSectionHeader;
            Arguments = arguments;
            CommandName = commandName;
            CommandIndices = commandIndices;
            File = file;
            Line = line;
            Column = column;
        }
    }
}
