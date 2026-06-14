// <copyright file="IconPicker.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System;
    using System.IO;
    using System.Numerics;
    using GameHelper;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    /// A class to store the currently selected icon.
    /// This class assumes that the icon sprite (png) file has:
    ///   (1) Arranged icons in the center of the Icon box.
    ///   (2) All icon boxes are of exact same size.
    ///   (3) There is no padding/margins/buffer-pixel between icon boxes
    ///       (i.e. where 1 box ends, another starts).
    /// </summary>
    public class IconPicker
    {
        private static readonly ImGuiWindowFlags PopUpFlags =
            ImGuiWindowFlags.AlwaysHorizontalScrollbar |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBackground;

        private float iconScale = 10;
        private Vector2 popUpPos = Vector2.Zero;
        private bool showPopUp = false;
        private Vector2 iconDimension = Vector2.One;

        /// <summary>
        /// Initializes a new instance of the <see cref="IconPicker"/> class.
        /// </summary>
        /// <param name="filepathname">file pathname to the icon sprite file.</param>
        /// <param name="clicked">Row and Column information of the icon user has clicked.</param>
        /// <param name="iconSize">Width and height of a single icon of the sprite in pixel.</param>
        /// <param name="iconScale">how big you want to display the icon.</param>
        [JsonConstructor]
        public IconPicker(
            string filepathname,
            Vector2 clicked,
            Vector2 iconSize,
            float iconScale)
        {
            this.FilePathName = filepathname;
            this.Clicked = clicked;
            this.iconScale = iconScale;
            this.IconSize= iconSize;
            this.Initialize();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IconPicker"/> class.
        /// </summary>
        /// <param name="filepathname">file pathname to the icon sprite file.</param>
        /// <param name="x">Default Icon Column number. Note: start from 0.</param>
        /// <param name="y">Default Icon Row number. Note: start from 0.</param>
        /// <param name="s">Default Icon size.</param>
        /// <param name="iconSize">Size of a single icon of the given sprite in pixel.</param>
        /// <param name="showPath">Whether to show a path to this entity type by default.</param>
        /// <param name="pathColor">Default color for the path line.</param>
        public IconPicker(
            string filepathname, int x, int y, int s, Vector2 iconSize,
            bool showPath = false,
            Vector4? pathColor = null)
        {
            this.FilePathName = filepathname;
            this.Clicked = new(x, y);
            this.IconSize = iconSize;
            this.iconScale = s;
            this.ShowPath = showPath;
            this.PathColor = pathColor ?? new Vector4(0f, 1f, 1f, 1f);
            this.Initialize();
        }

        /// <summary>
        /// Gets a value indicating which icon user has clicked.
        /// </summary>
        public Vector2 Clicked { get; private set; } = Vector2.Zero;

        /// <summary>
        /// Get a value indicating the single icon size in pixel.
        /// </summary>
        public Vector2 IconSize { get; private set; } = Vector2.One;

        /// <summary>
        /// Gets a value indicating how big you want to display the icon.
        /// </summary>
        public float IconScale => this.iconScale;

        /// <summary>
        /// Gets or sets a value indicating whether this icon should be drawn on the map.
        /// Toggled independently of <see cref="IconScale"/> so the size is preserved while hidden.
        /// </summary>
        public bool Show = true;

        /// <summary>
        /// Gets a value indicating whether this icon should actually be drawn:
        /// enabled via <see cref="Show"/> and has a positive scale.
        /// </summary>
        [JsonIgnore]
        public bool Draw => this.Show && this.iconScale > 0f;

        /// <summary>
        /// Gets or sets a value indicating whether to compute and draw a path to this entity type.
        /// </summary>
        public bool ShowPath = false;

        /// <summary>
        /// Gets or sets the color of the path line for this entity type.
        /// </summary>
        public Vector4 PathColor = new(0f, 1f, 1f, 1f);

        /// <summary>
        /// Gets the icon sprite file pathname.
        /// </summary>
        public string FilePathName { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the texture pointer.
        /// </summary>
        [JsonIgnore]
        public IntPtr TexturePtr { get; private set; } = IntPtr.Zero;

        /// <summary>
        /// Gets the vector pointing to start (top left) of the Box.
        /// </summary>
        [JsonIgnore]
        public Vector2 UV0 { get; private set; } = Vector2.Zero;

        /// <summary>
        /// Gets the vector pointing to end ( bottom right ) of the Box.
        /// </summary>
        [JsonIgnore]
        public Vector2 UV1 { get; private set; } = Vector2.Zero;

        /// <summary>
        /// Show the Setting Widget for the selection of icon. This function assumes
        /// that the ImGui window is already created.
        /// </summary>
        public void ShowSettingWidget()
        {
            ImGui.PushID(this.GetHashCode());
            ImGui.PushItemWidth(200);
            ImGui.InputFloat($"##iconscale", ref this.iconScale, 1f, 1f);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.Checkbox("Path", ref this.ShowPath);
            if (this.ShowPath)
            {
                ImGui.SameLine();
                ImGui.PushItemWidth(150);
                ImGui.ColorEdit4("##pathcolor", ref this.PathColor,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreview);
                ImGui.PopItemWidth();
            }

            ImGui.SameLine();

            var buttonSize = new Vector2(ImGui.GetFontSize());
            Core.Overlay.AddOrGetImagePointer(this.FilePathName, false, out var p, out var w, out var h);

            if (ImGui.ImageButton("icon_picker", this.TexturePtr, buttonSize, this.UV0, this.UV1))
            {
                this.popUpPos = ImGui.GetWindowPos();
                this.popUpPos.X += ImGui.GetWindowSize().X;
                this.showPopUp = true;
            }

            if (this.showPopUp)
            {
                ImGui.SetNextWindowPos(this.popUpPos, ImGuiCond.Appearing);
                ImGui.SetNextWindowSize(new Vector2(400), ImGuiCond.Appearing);
                var title = $"Icon Picker (Double click to select an item)";
                if (ImGui.Begin(title, ref this.showPopUp, PopUpFlags))
                {
                    if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseDoubleClicked[0])
                    {
                        var clicked = ImGui.GetIO().MouseClickedPos[0] - ImGui.GetCursorScreenPos();
                        var x = (int)(clicked.X / (w * this.iconDimension.X));
                        var y = (int)(clicked.Y / (h * this.iconDimension.Y));
                        this.Clicked = new Vector2(x, y);
                        Console.WriteLine($"IconPicker: selected icon at column={x}, row={y}");
                        this.UpdateUV0UV1();
                        this.showPopUp = false;
                    }
                    else if (!ImGui.IsWindowFocused())
                    {
                        this.showPopUp = false;
                    }

                    ImGui.Image(p, new Vector2(w, h));
                }

                ImGui.End();
            }

            ImGui.PopID();
        }

        /// <summary>
        /// Uploads the sprite icon file as texture and updates the class data.
        /// </summary>
        private void Initialize()
        {
            if (File.Exists(this.FilePathName))
            {
                this.UploadIconSpriteFile();
                this.UpdateUV0UV1();
            }
            else
            {
                throw new FileNotFoundException($"Missing Icons (sprite) file with name: {this.FilePathName}");
            }
        }

        private void UploadIconSpriteFile()
        {
            Core.Overlay.AddOrGetImagePointer(this.FilePathName, false, out var p, out var width, out var height);
            this.iconDimension = new(this.IconSize.X / width, this.IconSize.Y / height);
            this.TexturePtr = p;
        }

        private void UpdateUV0UV1()
        {
            var selected = this.Clicked;
            var size = this.iconDimension;
            this.UV0 = new Vector2(selected.X++ * size.X, selected.Y++ * size.Y);
            this.UV1 = new Vector2(selected.X * size.X, selected.Y * size.Y);
        }
    }
}
