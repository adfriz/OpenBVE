//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2025, The OpenBVE Project
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

namespace OpenBveApi.Graphics
{
        /// <summary>The spatial upscaler applied to the 3D world rendering</summary>
        public enum UpscalerMode
        {
                /// <summary>No upscaling - the world renders at native resolution</summary>
                Off = 0,
                /// <summary>AMD FidelityFX Super Resolution 1.0 - fused EASU + RCAS single pass</summary>
                Fsr1 = 1,
                /// <summary>Qualcomm Snapdragon Game Super Resolution v1 - single pass edge-direction aware Lanczos upscaling</summary>
                Sgsr1 = 2
        }

        /// <summary>
        /// The internal render resolution (upscaler quality preset) as a percentage of
        /// the window size. The upscaler then produces the native resolution output.
        /// The percentages follow the vendor-recommended FSR 1.0 presets.
        /// </summary>
        public enum UpscalerQuality
        {
                /// <summary>50% internal resolution (2.0x upscale)</summary>
                Performance = 0,
                /// <summary>59% internal resolution (1.7x upscale)</summary>
                Balanced = 1,
                /// <summary>67% internal resolution (1.5x upscale)</summary>
                Quality = 2,
                /// <summary>100% internal resolution (1.0x, sharpening only) - "Native AA"</summary>
                NativeAA = 3,
                /// <summary>77% internal resolution (1.3x upscale)</summary>
                UltraQuality = 4,
                /// <summary>33% internal resolution (3.0x upscale)</summary>
                UltraPerformance = 6
        }

        /// <summary>
        /// Vendor-recommended quality preset tables for each upscaler mode.
        ///
        /// FSR 1.0 presets: Native AA 100%, Ultra Quality 77% (1.3x), Quality 67%
        /// (1.5x), Balanced 59% (1.7x), Performance 50% (2.0x), Ultra Performance
        /// 33% (3.0x). SGSR v1 supports arbitrary ratios, so it uses the same list.
        /// </summary>
        public static class UpscalerPresets
        {
                /// <summary>Preset list ordered from highest quality to highest performance</summary>
                private static readonly UpscalerQuality[] Presets =
                {
                        UpscalerQuality.NativeAA,
                        UpscalerQuality.UltraQuality,
                        UpscalerQuality.Quality,
                        UpscalerQuality.Balanced,
                        UpscalerQuality.Performance,
                        UpscalerQuality.UltraPerformance
                };

                /// <summary>Returns the shared quality preset list (both upscalers accept the same ratios)</summary>
                public static UpscalerQuality[] GetQualities(UpscalerMode mode)
                {
                        return Presets;
                }

                /// <summary>Returns the preset labels (including percentages) for the given upscaler mode</summary>
                public static string[] GetLabels(UpscalerMode mode)
                {
                        UpscalerQuality[] list = GetQualities(mode);
                        string[] labels = new string[list.Length];
                        for (int i = 0; i < list.Length; i++)
                        {
                                labels[i] = GetDefaultLabel(list[i]);
                        }
                        return labels;
                }

                /// <summary>The internal render resolution fraction for a preset (1.0 = native)</summary>
                public static double GetRenderScale(UpscalerQuality quality)
                {
                        switch (quality)
                        {
                                case UpscalerQuality.UltraQuality: return 0.77;
                                case UpscalerQuality.Quality: return 0.67;
                                case UpscalerQuality.Balanced: return 0.59;
                                case UpscalerQuality.Performance: return 0.50;
                                case UpscalerQuality.UltraPerformance: return 0.33;
                                case UpscalerQuality.NativeAA: return 1.00;
                                default: return 0.67;
                        }
                }

                /// <summary>The untranslated English label of a preset, including the official percentage</summary>
                public static string GetDefaultLabel(UpscalerQuality quality)
                {
                        switch (quality)
                        {
                                case UpscalerQuality.NativeAA: return "Native AA (100%)";
                                case UpscalerQuality.UltraQuality: return "Ultra Quality (77%)";
                                case UpscalerQuality.Quality: return "Quality (67%)";
                                case UpscalerQuality.Balanced: return "Balanced (59%)";
                                case UpscalerQuality.Performance: return "Performance (50%)";
                                case UpscalerQuality.UltraPerformance: return "Ultra Performance (33%)";
                                default: return "Quality (67%)";
                        }
                }

                /// <summary>The untranslated English name of a preset, without the percentage</summary>
                public static string GetDefaultName(UpscalerQuality quality)
                {
                        switch (quality)
                        {
                                case UpscalerQuality.NativeAA: return "Native AA";
                                case UpscalerQuality.UltraQuality: return "Ultra Quality";
                                case UpscalerQuality.Quality: return "Quality";
                                case UpscalerQuality.Balanced: return "Balanced";
                                case UpscalerQuality.Performance: return "Performance";
                                case UpscalerQuality.UltraPerformance: return "Ultra Performance";
                                default: return "Quality";
                        }
                }

                /// <summary>Snaps a quality preset to the nearest supported preset</summary>
                public static UpscalerQuality SnapToMode(UpscalerMode mode, UpscalerQuality quality)
                {
                        UpscalerQuality[] allowed = GetQualities(mode);
                        if (allowed.Length == 0)
                        {
                                return UpscalerQuality.Quality;
                        }
                        double scale = GetRenderScale(quality);
                        UpscalerQuality best = allowed[0];
                        double bestDistance = double.MaxValue;
                        foreach (UpscalerQuality candidate in allowed)
                        {
                                double distance = System.Math.Abs(GetRenderScale(candidate) - scale);
                                if (distance < bestDistance)
                                {
                                        bestDistance = distance;
                                        best = candidate;
                                }
                        }
                        return best;
                }
        }

        /// <summary>
        /// Maps the UI order to the saved mode values. SGSR is the first enabled
        /// choice, so stepping past Off selects it rather than FSR 1.0.
        /// </summary>
        public static class UpscalerUi
        {
                private static readonly UpscalerMode[] Modes = { UpscalerMode.Off, UpscalerMode.Sgsr1, UpscalerMode.Fsr1 };

                public static UpscalerMode ModeAt(int index)
                {
                        return index >= 0 && index < Modes.Length ? Modes[index] : UpscalerMode.Off;
                }

                public static int IndexOf(UpscalerMode mode)
                {
                        for (int i = 0; i < Modes.Length; i++)
                        {
                                if (Modes[i] == mode)
                                {
                                        return i;
                                }
                        }
                        return 0;
                }
        }
}
