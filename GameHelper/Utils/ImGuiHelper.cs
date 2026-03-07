// <copyright file="ImGuiHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using System.Linq;
    using GameHelper.RemoteEnums;
    using GameOffsets.Natives;
    using ImGuiNET;

    /// <summary>
    ///     Has helper functions to DRY out the Ui creation.
    /// </summary>
    public static class ImGuiHelper
    {
        /// <summary>
        ///     Converts the float data to imgui text widget.
        /// </summary>
        /// <param name="text">text to display along with the float data</param>
        /// <param name="data">float data to display</param>
        public static void DisplayFloatWithInfinitySupport(string text, float data)
        {
            ImGui.Text(text);
            ImGui.SameLine();
            if (float.IsInfinity(data))
            {
                ImGui.Text("Inf");
            }
            else
            {
                ImGui.Text($"{data}");
            }
        }

        /// <summary>
        ///     Flags associated with transparent ImGui window.
        /// </summary>
        public const ImGuiWindowFlags TransparentWindowFlags = ImGuiWindowFlags.NoInputs |
                                                               ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse |
                                                               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                                               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
                                                               ImGuiWindowFlags.NoTitleBar;

        /// <summary>
        ///     Converts rgba color information to uint32 color format.
        /// </summary>
        /// <param name="r">red color number between 0 - 255.</param>
        /// <param name="g">green color number between 0 - 255.</param>
        /// <param name="b">blue color number between 0 - 255.</param>
        /// <param name="a">alpha number between 0 - 255.</param>
        /// <returns>color in uint32 format.</returns>
        public static uint Color(uint r, uint g, uint b, uint a)
        {
            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        /// <summary>
        ///     Converts rgba color information to uint32 color format.
        /// </summary>
        /// <param name="color">x,y,z,w = alpha number between 0 - 255.</param>
        /// <returns>color in uint32 format.</returns>
        public static uint Color(Vector4 color)
        {
            color *= 255f;
            return ((uint)color.W << 24) | ((uint)color.Z << 16) | ((uint)color.Y << 8) | (uint)color.X;
        }

        /// <summary>
        ///     Converts rgba color information to Vector4 color format.
        /// </summary>
        /// <param name="color">rgba color packed into uint32 format</param>
        /// <returns>rgba color in Vector4 format</returns>
        public static Vector4 Color(uint color)
        {
            var ret = Vector4.Zero;
            ret.Z = (color & 0xFF) / 255f;
            color >>= 8;
            ret.Y = (color & 0xFF) / 255f;
            color >>= 8;
            ret.X = (color & 0xFF) / 255f;
            color >>= 8;
            ret.W = (color & 0xFF) / 255f;
            return ret;
        }

        /// <summary>
        ///     Draws the Rectangle on the screen.
        /// </summary>
        /// <param name="pos">Position of the rectange.</param>
        /// <param name="size">Size of the rectange.</param>
        /// <param name="r">color selector red 0 - 255.</param>
        /// <param name="g">color selector green 0 - 255.</param>
        /// <param name="b">color selector blue 0 - 255.</param>
        public static void DrawRect(Vector2 pos, Vector2 size, byte r, byte g, byte b)
        {
            ImGui.GetForegroundDrawList().AddRect(pos, pos + size, Color(r, g, b, 255), 0f, ImDrawFlags.RoundCornersNone, 4f);
        }

        /// <summary>
        ///     Draws the text on the screen.
        /// </summary>
        /// <param name="pos">world location to draw the text.</param>
        /// <param name="text">text to draw.</param>
        public static void DrawText(StdTuple3D<float> pos, string text)
        {
            var colBg = Color(0, 0, 0, 255);
            var colFg = Color(255, 255, 255, 255);
            var textSizeHalf = ImGui.CalcTextSize(text) / 2;
            var location = Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(pos);
            var max = location + textSizeHalf;
            location -= textSizeHalf;
            ImGui.GetBackgroundDrawList().AddRectFilled(location, max, colBg);
            ImGui.GetForegroundDrawList().AddText(location, colFg, text);
        }

        /// <summary>
        ///     Draws the disabled button on the ImGui.
        /// </summary>
        /// <param name="buttonLabel">text to write on the button.</param>
        public static void DrawDisabledButton(string buttonLabel)
        {
            var col = Color(204, 204, 204, 128);
            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, col);
            ImGui.Button(buttonLabel);
            ImGui.PopStyleColor(3);
        }

        /// <summary>
        ///     Helps convert address to ImGui Widget.
        /// </summary>
        /// <param name="name">name of the object whos address it is.</param>
        /// <param name="address">address of the object in the game.</param>
        public static void IntPtrToImGui(string name, IntPtr address)
        {
            var addr = address.ToInt64().ToString("X");
            ImGui.Text(name);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Color(0, 0, 0, 0));
            if (ImGui.SmallButton(addr))
            {
                ImGui.SetClipboardText(addr);
            }

            ImGui.PopStyleColor();
        }

        /// <summary>
        ///     Helps convert the text into ImGui widget that display the text
        ///     and copy it if user click on it.
        /// </summary>
        /// <param name="displayText">text to display on the ImGui.</param>
        /// <param name="copyText">text to copy when user click.</param>
        public static void DisplayTextAndCopyOnClick(string displayText, string copyText)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Color(0, 0, 0, 0));
            if (ImGui.SmallButton(displayText))
            {
                ImGui.SetClipboardText(copyText);
            }

            ImGui.PopStyleColor();
        }

        /// <summary>
        ///     Creates a ImGui ComboBox for C# Enums.
        /// </summary>
        /// <typeparam name="T">Enum type to display in the ComboBox.</typeparam>
        /// <param name="displayText">Text to display along the ComboBox.</param>
        /// <param name="current">Selected enum value in the ComboBox.</param>
        /// <returns>true in case user select an item otherwise false.</returns>
        public static bool EnumComboBox<T>(string displayText, ref T current)
            where T : struct, Enum
        {
            return IEnumerableComboBox(displayText, Enum.GetValues<T>(), ref current);
        }

        /// <summary>
        ///     Creates a ImGui ComboBox for C# Enums whos values are not continous.
        /// </summary>
        /// <typeparam name="T">Enum type to display in the ComboBox.</typeparam>
        /// <param name="displayText">Text to display along the ComboBox.</param>
        /// <param name="current">Selected enum value in the ComboBox.</param>
        /// <returns>true in case user select an item otherwise false.</returns>
        public static bool NonContinuousEnumComboBox<T>(string displayText, ref T current)
            where T : struct, Enum
        {
            var ret = false;
            var enumValues = Enum.GetValues<T>();
            if (ImGui.BeginCombo(displayText, $"{current}"))
            {
                foreach (var item in enumValues)
                {
                    var selected = item.Equals(current);
                    if (ImGui.IsWindowAppearing() && selected)
                    {
                        ImGui.SetScrollHereY();
                    }

                    if (ImGui.Selectable($"{Convert.ToInt32(item)}:{item}", selected))
                    {
                        current = item;
                        ret = true;
                    }
                }

                ImGui.EndCombo();
            }

            return ret;
        }

        /// <summary>
        ///     Creates a ImGui ComboBox for C# IEnumerable.
        /// </summary>
        /// <typeparam name="T">The type of objects in the IEnumerable.</typeparam>
        /// <param name="displayText">Text to display along the ComboBox.</param>
        /// <param name="items">IEnumerable data to choose from in the ComboBox.</param>
        /// <param name="current">Currently selected object of the IEnumerable data.</param>
        /// <returns>Returns a value indicating whether user has selected an item or not.</returns>
        public static bool IEnumerableComboBox<T>(string displayText, IEnumerable<T> items, ref T current)
        {
            var ret = false;
            if (ImGui.BeginCombo(displayText, $"{current}"))
            {
                var counter = 0;
                foreach (var item in items)
                {
                    var selected = item.Equals(current);
                    if (ImGui.IsWindowAppearing() && selected)
                    {
                        ImGui.SetScrollHereY();
                    }

                    if (ImGui.Selectable($"{counter}:{item}", selected))
                    {
                        current = item;
                        ret = true;
                    }

                    counter++;
                }

                ImGui.EndCombo();
            }

            return ret;
        }

        /// <summary>
        ///     Displays the text in ImGui tooltip.
        /// </summary>
        /// <param name="text">text to display in the ImGui tooltip.</param>
        /// <param name="maxwidth">max number of characters to display in a single line before wrapping it into a new line</param>
        public static void ToolTip(string text, float maxwidth = 35.0f)
        {
            if (ImGui.IsItemHovered())
            {
                if (ImGui.BeginTooltip())
                {
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * maxwidth);
                    ImGui.TextUnformatted(text);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }
        }

        /// <summary>
        ///     Creates a widget to update Vector2 data with integer increments.
        /// </summary>
        /// <param name="text">Text to display on the widget right hand side.</param>
        /// <param name="itemWidth">width available to draw the widget</param>
        /// <param name="data">data to change by the widget</param>
        /// <param name="min0">minimum possible integer for Vector2 X data</param>
        /// <param name="max0">maximum possible integer for Vector2 X data</param>
        /// <param name="min1">minimum possible integer for Vector2 Y data</param>
        /// <param name="max1">maximum possible integer for Vector2 Y data</param>
        /// <param name="flags">flags for this widget</param>
        /// <returns></returns>
        public static bool Vector2SliderInt(string text, float itemWidth, ref Vector2 data,
            int min0, int max0, int min1, int max1, ImGuiSliderFlags flags)
        {
            var dataChanged = false;
            var dataX = (int)data.X;
            var dataY = (int)data.Y;
            ImGui.PushItemWidth(itemWidth / 3.1f);
            if (ImGui.SliderInt($"##{text}111", ref dataX, min0, max0, "%d", flags))
            {
                dataChanged = true;
                data.X = dataX;
            }

            ImGui.SameLine(0f, 5f);
            if (ImGui.SliderInt($"{text}##{text}222", ref dataY, min1, max1, "%d", flags))
            {
                dataChanged = true;
                data.Y = dataY;
            }

            ImGui.PopItemWidth();
            return dataChanged;
        }

        /// <summary>
        ///     Function to center elements in column.
        /// </summary>
        /// <param name="frameHeight">Pass the ImGui.GetFrameHeight</param>
        public static void CenterElementInColumn(float frameHeight)
        {
            var padding = (ImGui.GetColumnWidth() - frameHeight) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
        }

        /// <summary>
        ///     Function to display stats in humanreadable format on the imgui.
        /// </summary>
        /// <param name="stats">stats dictionary reference</param>
        /// <param name="displayname">stats dictionary (human readable) name to display</param>
        public static void StatsWidget(Dictionary<GameStats, int> stats, string displayname)
        {
            if (ImGui.TreeNode(displayname))
            {
                KeyValuePair<GameStats, int>[] snapshot;
                lock (stats)
                {
                    snapshot = stats.ToArray();
                }

                foreach (var stat in snapshot)
                {
                    ImGuiHelper.DisplayTextAndCopyOnClick($"{stat.Key}: {stat.Value}", $"{stat.Key}");
                }

                ImGui.TreePop();
            }
        }
    }
}
