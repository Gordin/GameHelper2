// <copyright file="PlayerSettingsShell.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using ImGuiNET;

    /// <summary>Player-oriented navigation, independent of plugin lifetime and game memory.</summary>
    internal sealed class PlayerSettingsShell
    {
        internal sealed record PluginEntry(string Id, string Name, string Description, bool Enabled);
        internal string Page = "home";
        internal string Search = string.Empty;
        private string lastContent = string.Empty;
        private bool quitRequested;
        private readonly Func<string, string, string> translate;
        internal PlayerSettingsShell(Func<string, string, string> translate) => this.translate = translate;
        private string T(string key, string fallback) => this.translate("settings.shell." + key, fallback);
        internal static string GroupOf(string name) => name switch
        {
            "LootTracker" or "PortalAccess" => "mapping",
            "Atlas2" or "Radar" or "PreloadAlert" or "RitualWispAlert" => "exploration",
            "HealthBars" or "PlayerBuffBar" or "AutoHotKeyTrigger" => "combat",
            "PickupHelper" or "LootValue" => "items",
            _ => "other",
        };

        private string Title(string page) => page switch
        {
            "home" => this.T("home", "Overview"),
            "mapping" => this.T("mapping", "Maps & earnings"),
            "exploration" => this.T("exploration", "Maps & exploration"),
            "combat" => this.T("combat", "Character & combat"),
            "items" => this.T("items", "Loot & items"),
            "other" => this.T("other", "Other plugins"),
            "general" => this.T("general", "General & shortcuts"),
            "display" => this.T("display", "Display & fonts"),
            "plugins" => this.T("plugins", "Plugin manager"),
            "advanced" => this.T("advanced", "Advanced tools"),
            "about" => this.T("about", "About"),
            _ => page,
        };

        private string Description(string group) => group switch
        {
            "mapping" => this.T("mapping_hint", "Map timer, currency totals, session history and portal interaction."),
            "exploration" => this.T("exploration_hint", "Atlas, radar, encounters and ritual alerts."),
            "combat" => this.T("combat_hint", "Health bars, buffs and combat key rules."),
            "items" => this.T("items_hint", "Loot prices, item filters and pickup preferences."),
            _ => this.T("other_hint", "Settings for additional installed plugins."),
        };

        internal void Draw(IReadOnlyList<PluginEntry> plugins, IntPtr backdrop, string version, string status,
            string hotkey, Action<string> drawCore, Action<string> drawPlugin, Action<string, bool> setEnabled,
            Action hide, Action quit)
        {
            var header = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var draw = ImGui.GetWindowDrawList();
            draw.AddCircle(header + new Vector2(19, 22), 15, ImGui.GetColorU32(ImGuiTheme.Accent), 32, 2);
            draw.AddLine(header + new Vector2(12, 27), header + new Vector2(25, 15), ImGui.GetColorU32(ImGuiTheme.Accent), 2);
            draw.AddCircleFilled(header + new Vector2(19, 22), 3, ImGui.GetColorU32(ImGuiTheme.Accent));
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.25f, header + new Vector2(46, 0), 0xFFF7F7F4, "GameHelper");
            draw.AddText(header + new Vector2(47, 29), ImGui.GetColorU32(ImGuiTheme.TextMuted), "PATH OF EXILE 2   /   " + version);
            // Explicit drag surface works even when ImGui only permits title-bar dragging.
            ImGui.InvisibleButton("##shell_drag", new Vector2(Math.Max(80, width - 150), 52));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
            ImGui.SetCursorScreenPos(header + new Vector2(width - 100, 5));
            if (ChromeButton("hide", false)) hide();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(this.T("hide", "Hide settings") + "  " + hotkey);
            ImGui.SameLine();
            if (ChromeButton("quit", true)) this.quitRequested = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(this.T("quit", "Quit GameHelper"));
            ImGui.SetCursorScreenPos(header + new Vector2(0, 66));

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.08f, .12f, .17f, .92f));
            if (ImGui.BeginChild("SettingsSidebarV2", new Vector2(212, 0), ImGuiChildFlags.Borders))
            {
                this.Nav("home", "01", plugins);
                ImGui.Spacing();
                ImGui.TextDisabled(this.T("play", "PLAY"));
                foreach (var group in new[] { "mapping", "exploration", "combat", "items" })
                    this.Nav(group, group switch { "mapping" => "02", "exploration" => "03", "combat" => "04", _ => "05" }, plugins);
                if (plugins.Any(p => GroupOf(p.Id) == "other")) this.Nav("other", "+", plugins);
                ImGui.Spacing();
                ImGui.TextDisabled(this.T("preferences", "PREFERENCES"));
                foreach (var page in new[] { "general", "display", "plugins", "advanced", "about" }) this.Nav(page, "", plugins);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextDisabled(hotkey + "  " + this.T("hotkey_hint", "Show / hide settings"));
                ImGui.TextColored(ImGuiTheme.TextMuted, status);
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.14f, .18f, .23f, .97f));
            if (ImGui.BeginChild("SettingsContentV2", Vector2.Zero, ImGuiChildFlags.Borders))
            {
                ImGui.SetNextItemWidth(Math.Min(340, ImGui.GetContentRegionAvail().X - 80));
                ImGui.InputTextWithHint("##settings_search", this.T("search", "Search plugins by name or description"), ref this.Search, 100);
                if (this.Search.Length > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(this.T("clear", "Clear") + "##search_clear")) this.Search = string.Empty;
                }
                ImGui.Spacing();
                if (ImGui.BeginChild("SettingsPageBody", Vector2.Zero, ImGuiChildFlags.None))
                {
                    var contentKey = this.Page + "|" + this.Search;
                    if (contentKey != this.lastContent) { ImGui.SetScrollY(0); this.lastContent = contentKey; }
                    if (!string.IsNullOrWhiteSpace(this.Search))
                    {
                        ImGuiTheme.SectionHeader(this.T("results", "Search results"));
                        var term = this.Search.Trim();
                        this.PluginCards(plugins.Where(p => (p.Id + " " + p.Name + " " + p.Description).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList());
                    }
                    else if (this.Page == "home") this.Home(plugins, backdrop, hotkey, hide);
                    else if (this.Page.StartsWith("plugin:", StringComparison.Ordinal))
                    {
                        var plugin = plugins.FirstOrDefault(p => "plugin:" + p.Id == this.Page);
                        if (plugin == null) this.Page = "plugins";
                        else
                        {
                            if (Pill(this.T("back", "Back") + " · " + this.Title(GroupOf(plugin.Id)) + "##back", false, new Vector2(0, 32)))
                                this.Page = GroupOf(plugin.Id);
                            ImGuiTheme.SectionHeader(plugin.Name, plugin.Description);
                            var enabled = plugin.Enabled;
                            if (ImGui.Checkbox(this.T("enabled", "Enable this plugin") + "##plugin_enabled", ref enabled))
                                setEnabled(plugin.Id, enabled);
                            ImGui.Spacing();
                            // Draw settings only on the following frame after OnEnable / OnDisable.
                            if (plugin.Enabled && enabled) drawPlugin(plugin.Id);
                            else ImGui.TextWrapped(this.T("disabled_hint", "Enable this plugin to configure it. Your saved settings are kept."));
                        }
                    }
                    else if (new[] { "mapping", "exploration", "combat", "items", "other" }.Contains(this.Page))
                    {
                        ImGuiTheme.SectionHeader(this.Title(this.Page), this.Description(this.Page));
                        this.PluginCards(plugins.Where(p => GroupOf(p.Id) == this.Page).ToList());
                    }
                    else drawCore(this.Page);
                }
                ImGui.EndChild();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            this.DrawQuitDialog(quit);
        }

        private void Nav(string page, string number, IReadOnlyList<PluginEntry> plugins)
        {
            var plugin = plugins.FirstOrDefault(p => "plugin:" + p.Id == this.Page);
            var selected = this.Page == page || (plugin != null && GroupOf(plugin.Id) == page);
            ImGui.PushStyleColor(ImGuiCol.Button, selected ? ImGuiTheme.Accent : Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? ImGuiTheme.Accent : new Vector4(.24f, .32f, .4f, 1));
            ImGui.PushStyleColor(ImGuiCol.Text, selected ? new Vector4(.12f, .16f, .22f, 1) : new Vector4(.88f, .91f, .95f, 1));
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(.08f, .5f));
            if (ImGui.Button((number.Length > 0 ? number + "   " : "       ") + this.Title(page) + "##nav_" + page, new Vector2(-1, 36)))
            { this.Page = page; this.Search = string.Empty; }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        private void Home(IReadOnlyList<PluginEntry> plugins, IntPtr backdrop, string hotkey, Action hide)
        {
            var origin = ImGui.GetCursorScreenPos();
            var size = new Vector2(ImGui.GetContentRegionAvail().X, 236);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(origin, origin + size, 0xFF59422B, 16);
            if (backdrop != IntPtr.Zero)
                dl.AddImageRounded(backdrop, origin, origin + size, new Vector2(0, .16f), new Vector2(1, .8f), 0xFFFFFFFF, 16);
            dl.AddRectFilled(origin, origin + size, 0x3A171A1F, 16);
            dl.AddText(origin + new Vector2(24, 23), 0xFFE7DCA7, this.T("hero_eyebrow", "YOUR ADVENTURE, AT A GLANCE"));
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.85f, origin + new Vector2(22, 60), 0xFFFFFFFF, this.T("hero_title", "Ready for the next map."));
            dl.AddText(origin + new Vector2(24, 111), 0xFFE6E8EA, this.T("hero_subtitle", "Explore, collect, and keep track of every run."));
            ImGui.SetCursorScreenPos(origin + new Vector2(24, 167));
            if (Pill(this.T("return_game", "Return to game") + "  " + hotkey + "##return", true, new Vector2(222, 44))) hide();
            ImGui.SetCursorScreenPos(origin + new Vector2(0, size.Y + 18));
            ImGui.Text(this.T("quick", "Set up your next run"));
            ImGui.SameLine();
            ImGui.TextDisabled($"  {plugins.Count(p => p.Enabled)} / {plugins.Count}  " + this.T("active", "plugins active"));
            ImGui.Spacing();
            var groups = new[] { "mapping", "exploration", "combat", "items" };
            var columns = ImGui.GetContentRegionAvail().X >= 620 ? 2 : 1;
            if (ImGui.BeginTable("##home_cards", columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var group in groups)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.21f, .27f, .33f, 1));
                    if (ImGui.BeginChild("home_" + group, new Vector2(0, 140), ImGuiChildFlags.Borders))
                    {
                        ImGui.Text(this.Title(group));
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                        ImGui.TextWrapped(this.Description(group));
                        ImGui.PopStyleColor();
                        ImGui.SetCursorPosY(86);
                        if (Pill(this.T("open", "Open settings") + "  >##" + group, false, new Vector2(148, 30))) this.Page = group;
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }
                ImGui.EndTable();
            }
            ImGui.Spacing();
            ImGui.TextDisabled(this.T("save_hint", "Settings are saved when you hide this window or exit."));
        }

        private void PluginCards(IReadOnlyList<PluginEntry> plugins)
        {
            if (plugins.Count == 0) { ImGui.TextWrapped(this.T("empty", "No matching plugins. Try another name or open the plugin manager.")); return; }
            foreach (var plugin in plugins)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.2f, .25f, .31f, 1));
                if (ImGui.BeginChild("card_" + plugin.Id, new Vector2(0, 158), ImGuiChildFlags.Borders))
                {
                    ImGui.Text(plugin.Name);
                    ImGui.SameLine();
                    ImGui.TextColored(plugin.Enabled ? ImGuiTheme.Success : ImGuiTheme.TextMuted,
                        "  · " + (plugin.Enabled ? this.T("on", "Enabled") : this.T("off", "Disabled")));
                    ImGui.TextDisabled(plugin.Id);
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                    ImGui.TextWrapped(plugin.Description);
                    ImGui.PopStyleColor();
                    if (Pill(this.T("configure", "Configure") + "  >##" + plugin.Id, false, new Vector2(136, 32)))
                    { this.Page = "plugin:" + plugin.Id; this.Search = string.Empty; }
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }

        internal void RequestQuit() => this.quitRequested = true;

        private void DrawQuitDialog(Action quit)
        {
            if (this.quitRequested) { ImGui.OpenPopup("GameHelperCloseConfirmation"); this.quitRequested = false; }
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize / 2, ImGuiCond.Always, new Vector2(.5f));
            ImGui.SetNextWindowSize(new Vector2(Math.Min(600, viewport.WorkSize.X - 32), 276));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28, 22));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(.21f, .22f, .27f, .99f));
            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(.05f, .09f, .14f, .68f));
            if (ImGui.BeginPopupModal("GameHelperCloseConfirmation", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                ImGui.Text(this.T("dialog_title", "System message"));
                ImGui.SameLine(ImGui.GetWindowWidth() - 80);
                if (ChromeButton("dialog_close", true)) ImGui.CloseCurrentPopup();
                ImGui.Separator();
                var text = this.T("dialog_message", "Exit GameHelper? Your settings will be saved.");
                ImGui.SetCursorPos(new Vector2(Math.Max(28, (ImGui.GetWindowWidth() - ImGui.CalcTextSize(text).X) / 2), 108));
                ImGui.TextWrapped(text);
                ImGui.SetCursorPos(new Vector2(28, 202));
                var w = (ImGui.GetContentRegionAvail().X - 20) / 2;
                if (Pill(this.T("cancel", "Cancel") + "##cancel_quit", false, new Vector2(w, 44))) ImGui.CloseCurrentPopup();
                ImGui.SameLine(0, 20);
                if (Pill(this.T("confirm", "Confirm") + "##confirm_quit", false, new Vector2(w, 44)))
                { quit(); ImGui.CloseCurrentPopup(); }
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar();
        }

        internal static bool Pill(string label, bool primary, Vector2 size)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 24);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(18, 6));
            ImGui.PushStyleColor(ImGuiCol.Button, primary ? ImGuiTheme.Accent : new Vector4(.94f, .96f, .98f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, primary ? new Vector4(1, .93f, .38f, 1) : Vector4.One);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, primary ? new Vector4(.89f, .76f, .08f, 1) : new Vector4(.75f, .83f, .9f, 1));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.16f, .2f, .27f, 1));
            var clicked = ImGui.Button(label, size);
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(2);
            return clicked;
        }

        private static bool ChromeButton(string id, bool close)
        {
            var p = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton("##chrome_" + id, new Vector2(40, 36));
            var dl = ImGui.GetWindowDrawList();
            if (ImGui.IsItemHovered()) dl.AddRectFilled(p, p + new Vector2(40, 36), 0x30FFFFFF, 12);
            if (close)
            {
                dl.AddLine(p + new Vector2(14, 12), p + new Vector2(26, 24), 0xFFF4F4F4, 2);
                dl.AddLine(p + new Vector2(26, 12), p + new Vector2(14, 24), 0xFFF4F4F4, 2);
            }
            else dl.AddLine(p + new Vector2(12, 18), p + new Vector2(28, 18), 0xFFF4F4F4, 2);
            return clicked;
        }
    }
}
