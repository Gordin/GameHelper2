// <copyright file="GameUiExplorer.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Cache;
    using ImGuiNET;
    using RemoteEnums;
    using RemoteObjects.UiElement;
    using Utils;

    /// <summary>
    ///     Explore (visualize) the Game Ui Elements.
    /// </summary>
    public static class GameUiExplorer
    {
        private static readonly Vector4 VisibleUiElementColor = new(0, 255, 0, 255);
        private static readonly List<UiElement> Elements = new();
        private static readonly UiElementParents Parents = new(null, GameStateTypes.InGameState, GameStateTypes.EscapeState, "GameExplorer");

        /// <summary>
        ///     Initializes the co-routines.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(GameUiExplorerRenderCoRoutine());
            CoroutineHandler.Start(OnGameStateChange());
        }

        /// <summary>
        ///     Adds the UiElementBase to GameUiExplorer.
        /// </summary>
        /// <param name="element">UiElementBase to investigate.</param>
        internal static void AddUiElement(UiElementBase element)
        {
            Elements.Add(CreateUiElement(new UiElementBase(element.Address, Parents)));
            Core.GHSettings.ShowGameUiExplorer = true;
        }

        private static UiElement CreateUiElement(UiElementBase element)
        {
            UiElement eleStruct = new()
            {
                CurrentChildIndex = -1,
                CurrentChildPreview = string.Empty,
                Element = element,
                Children = new List<UiElementBase>()
            };

            for (var i = 0; i < element.TotalChildrens; i++)
            {
                eleStruct.Children.Add(element[i]!);
            }

            return eleStruct;
        }

        private static void RemoveUiElement(int i)
        {
            Elements[i].Children.Clear();
            Elements.RemoveAt(i);

            if (Elements.Count == 0 )
            {
                Parents.Clear();
            }
        }

        private static void RemoveAllUiElements()
        {
            for (var i = Elements.Count - 1; i >= 0; i--)
            {
                RemoveUiElement(i);
            }

            Parents.Clear();
        }

        /// <summary>
        ///     Draws the window to display the Game UiExplorer.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> GameUiExplorerRenderCoRoutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (!Core.GHSettings.ShowGameUiExplorer)
                {
                    continue;
                }

                Parents.UpdateAllParentsParallel();
                if (ImGui.Begin("Game UiExplorer", ref Core.GHSettings.ShowGameUiExplorer))
                {
                    if (ImGui.TreeNode("NOTES"))
                    {
                        ImGui.BulletText("Closing the game will remove all objects.");
                        ImGui.BulletText("To add element in this window go to any UiElement " +
                                         "in Data Visualization window and click Explore button.");
                        ImGui.BulletText("To check currently loaded element bounds, " +
                                         "hover over the element header in blue.");
                        ImGui.BulletText("To check bounds of all the children hover " +
                                         "over the Children box.");
                        ImGui.BulletText("Feel free to add same element more than once.");
                        ImGui.BulletText("When children combo box is opened feel free " +
                                         "to use the up/down arrow key.");
                        ImGui.BulletText("Children bounds are drawn with RED color.");
                        ImGui.BulletText("Current element bounds are drawn with Yellow Color.");
                        ImGui.BulletText("Green color child means it's visible, white means it isn't.");
                        ImGui.TreePop();
                    }

                    Parents.ToImGui();
                    if (ImGui.Button("Clear all Ui Elements (Mischief managed)") ||
                        Core.Process.Address == IntPtr.Zero)
                    {
                        RemoveAllUiElements();
                    }

                    ImGui.Separator();
                    for (var i = 0; i < Elements.Count; i++)
                    {
                        var current = Elements[i];
                        var isRequired = true;
                        var isCurrentModified = false;
                        var isEnterPressed = false;
                        if (ImGui.CollapsingHeader($"{i}", ref isRequired, ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            if (ImGui.IsItemHovered())
                            {
                                ImGuiHelper.DrawRect(
                                    current.Element.Position,
                                    current.Element.Size,
                                    255,
                                    255,
                                    0);
                            }

                            ImGuiHelper.IntPtrToImGui($"Address:", current.Element.Address);
                            current.Element.Address = current.Element.Address;
                            if (ImGui.BeginCombo($"Children##{i}", current.CurrentChildPreview))
                            {
                                if (current.CurrentChildIndex > -1)
                                {
                                    if (ImGui.IsItemHovered())
                                    {
                                        var cChild = current.Children[current.CurrentChildIndex];
                                        ImGuiHelper.DrawRect(
                                            cChild.Position,
                                            cChild.Size,
                                            255,
                                            64,
                                            64);
                                    }
                                }

                                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                                {
                                    if (current.CurrentChildIndex > 0)
                                    {
                                        current.CurrentChildIndex--;
                                    }
                                    else
                                    {
                                        current.CurrentChildIndex = current.Children.Count - 1;
                                    }

                                    isCurrentModified = true;
                                    Elements[i] = current;
                                }
                                else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                                {
                                    if (current.CurrentChildIndex < current.Children.Count - 1)
                                    {
                                        current.CurrentChildIndex++;
                                    }
                                    else
                                    {
                                        current.CurrentChildIndex = 0;
                                    }

                                    isCurrentModified = true;
                                    Elements[i] = current;
                                }
                                else if (ImGui.IsKeyPressed(ImGuiKey.Enter))
                                {
                                    isEnterPressed = true;
                                }

                                for (var j = 0; j < current.Children.Count; j++)
                                {
                                    var selected = j == current.CurrentChildIndex;
                                    var child = current.Children[j];
                                    child.Address = child.Address;

                                    if (child.IsVisible)
                                    {
                                        ImGui.PushStyleColor(ImGuiCol.Text, VisibleUiElementColor);
                                    }

                                    if (ImGui.Selectable($"{j}-{child.Address.ToInt64():X}", selected))
                                    {
                                        current.CurrentChildIndex = j;
                                        current.CurrentChildPreview = $"{child.Address.ToInt64():X}";
                                        Elements[i] = current;
                                    }

                                    if (child.IsVisible)
                                    {
                                        ImGui.PopStyleColor();
                                    }

                                    if (isEnterPressed && selected)
                                    {
                                        current.CurrentChildIndex = j;
                                        current.CurrentChildPreview = $"{child.Address.ToInt64():X}";
                                        Elements[i] = current;
                                        ImGui.CloseCurrentPopup();
                                        isEnterPressed = false;
                                    }

                                    if (ImGui.IsWindowAppearing() || isCurrentModified)
                                    {
                                        if (selected)
                                        {
                                            isCurrentModified = false;
                                            ImGui.SetScrollHereY();
                                        }
                                    }

                                    if (ImGui.IsItemHovered())
                                    {
                                        ImGuiHelper.DrawRect(
                                            child.Position,
                                            child.Size,
                                            255,
                                            255,
                                            0);
                                    }
                                }

                                ImGui.EndCombo();
                            }

                            if (ImGui.IsItemHovered())
                            {
                                for (var j = 0; j < current.Children.Count; j++)
                                {
                                    var child = current.Children[j];
                                    child.Address = child.Address;
                                    ImGuiHelper.DrawRect(child.Position, child.Size, 255, 255, 0);
                                    ImGui.GetForegroundDrawList().AddText(child.Position, 0xFF0000FF, $"{j}");
                                }
                            }

                            if (current.Children.Count > 0 && current.CurrentChildIndex > -1)
                            {
                                if (ImGui.Button($"Go to child##{i}"))
                                {
                                    Elements[i] = CreateUiElement(current.Children[current.CurrentChildIndex]);
                                }
                            }
                            else
                            {
                                ImGuiHelper.DrawDisabledButton($"Go to child##{i}");
                            }

                            ImGui.SameLine();
                            if (current.Element.TryGetParent(out var parent))
                            {
                                if (ImGui.Button($"Go to parent##{i}"))
                                {
                                    Elements[i] = CreateUiElement(parent);
                                }
                            }
                            else
                            {
                                ImGuiHelper.DrawDisabledButton($"Go to parent##{i}");
                            }
                        }

                        if (!isRequired)
                        {
                            RemoveUiElement(i);
                        }
                    }
                }

                ImGui.End();
            }
        }

        private static IEnumerator<Wait> OnGameStateChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.StateChanged);
                if (Core.States.GameCurrentState != GameStateTypes.InGameState
                    && Core.States.GameCurrentState != GameStateTypes.EscapeState
                    && Core.States.GameCurrentState != GameStateTypes.AreaLoadingState)
                {
                    RemoveAllUiElements();
                }
            }
        }

        private struct UiElement
        {
            public int CurrentChildIndex;
            public string CurrentChildPreview;
            public UiElementBase Element;
            public List<UiElementBase> Children;
        }
    }
}