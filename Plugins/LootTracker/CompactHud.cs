namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using ImGuiNET;

    public sealed record HudRow(string Name, string Time, string Value);
    public sealed class HudModel
    {
        public int MapCount { get; init; }
        public string AverageTime { get; init; } = "00:00";
        public string AverageValue { get; init; } = "0 Ex";
        public string Total { get; init; } = "0.0";
        public string Hourly { get; init; } = "0.0 / h";
        public string SessionTime { get; init; } = "00:00";
        public string Unit { get; init; } = "Ex";
        public bool IncompletePrice { get; init; }
        public IReadOnlyList<HudRow> Rows { get; init; } = Array.Empty<HudRow>();
    }

    /// <summary>The real HUD renderer is also used by the offscreen preview harness.</summary>
    public static class CompactHud
    {
        public static void Draw(HudModel model, Vector2 position, float width, float scale, float opacity,
            bool locked, Func<string, string, string> text)
        {
            scale = Math.Clamp(scale, 0.65f, 1.8f);
            width = Math.Max(680 * scale, width);
            var height = 124 * scale;
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(Math.Clamp(opacity, 0.05f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5 * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.055f, 0.055f, 0.06f, 1));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.4f, 0.42f, 0.85f));
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;
            if (locked) flags |= ImGuiWindowFlags.NoInputs;
            if (ImGui.Begin("###LootTrackerCompact", flags))
            {
                var draw = ImGui.GetWindowDrawList();
                var origin = ImGui.GetWindowPos();
                var font = ImGui.GetFont();
                var fontSize = 23 * scale;
                var normal = ImGui.ColorConvertFloat4ToU32(new Vector4(.86f, .86f, .86f, 1));
                var green = ImGui.ColorConvertFloat4ToU32(new Vector4(.33f, .86f, .30f, 1));
                var muted = ImGui.ColorConvertFloat4ToU32(new Vector4(.73f, .67f, .46f, 1));
                var column = Math.Max(220 * scale, width * .30f);
                var tableX = Math.Max(column + 164 * scale, width * .52f);
                void Label(float x, float y, string value, uint color)
                {
                    var available = (x >= column ? tableX : column) - x - 8 * scale;
                    var measured = ImGui.CalcTextSize(value).X * fontSize / ImGui.GetFontSize();
                    var size = fontSize * Math.Min(1f, available / Math.Max(1f, measured));
                    draw.AddText(font, size, origin + new Vector2(x, y), color, value);
                }
                float y0 = 13 * scale, y1 = 45 * scale, y2 = 77 * scale;
                Icon(draw, origin + new Vector2(20 * scale, y0 + 11 * scale), scale, 0);
                Icon(draw, origin + new Vector2(20 * scale, y1 + 11 * scale), scale, 1);
                Icon(draw, origin + new Vector2(20 * scale, y2 + 11 * scale), scale, 2);
                Label(42 * scale, y0, text("hud.maps", "Maps:") + " " + model.MapCount, normal);
                Label(42 * scale, y1, text("hud.average_time", "Avg time:") + " " + model.AverageTime, normal);
                Label(42 * scale, y2, text("hud.average_value", "Avg value:") + " " + model.AverageValue, normal);
                var incomeColor = model.IncompletePrice ? muted : green;
                Label(column, y0, text("hud.total", "Total"), incomeColor);
                Icon(draw, origin + new Vector2(column + 59 * scale, y0 + 11 * scale), scale, 2);
                Label(column + 77 * scale, y0, model.Total, incomeColor);
                Icon(draw, origin + new Vector2(column + 12 * scale, y1 + 11 * scale), scale, 2);
                Label(column + 30 * scale, y1, model.Hourly, incomeColor);
                Icon(draw, origin + new Vector2(column + 12 * scale, y2 + 11 * scale), scale, 1);
                Label(column + 30 * scale, y2, text("hud.session", "Session:") + " " + model.SessionTime, normal);

                ImGui.SetCursorPos(new Vector2(tableX, 11 * scale));
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5, 2) * scale);
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(.19f, .19f, .20f, .98f));
                ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(.28f, .28f, .30f, .9f));
                ImGui.SetWindowFontScale(21 * scale / ImGui.GetFontSize());
                if (ImGui.BeginTable("##RecentMaps", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp,
                    new Vector2(width - tableX - 12 * scale, 105 * scale)))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn(text("hud.map", "Map"), ImGuiTableColumnFlags.WidthStretch, 1.8f);
                    ImGui.TableSetupColumn(text("hud.time", "Time"), ImGuiTableColumnFlags.WidthFixed, 64 * scale);
                    ImGui.TableSetupColumn(text("hud.value", "Value"), ImGuiTableColumnFlags.WidthFixed, 90 * scale);
                    ImGui.TableHeadersRow();
                    foreach (var row in model.Rows)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Name);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Time);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Value);
                    }
                    ImGui.EndTable();
                }
                ImGui.SetWindowFontScale(1);
                ImGui.PopStyleColor(2); ImGui.PopStyleVar();
            }
            ImGui.End();
            ImGui.PopStyleColor(2); ImGui.PopStyleVar(3);
        }

        // Small amber map/hourglass and metallic currency symbols, independent of external textures.
        private static void Icon(ImDrawListPtr d, Vector2 c, float s, int type)
        {
            var gold = 0xFF7DB0D4u; var dark = 0xFF263340u; var bright = 0xFFC9D7DFu;
            Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
            if (type == 0)
            {
                d.AddRectFilled(P(-7,-10), P(6,10), 0xFF2B4462, 2*s);
                d.AddRect(P(-7,-10), P(6,10), gold, 2*s);
                d.AddLine(P(-3,-8),P(-3,8),bright,s);
                d.AddLine(P(1,-7),P(1,7),gold,s);
                d.AddCircle(P(-1,-11),3*s,gold,8,s);
            }
            else if (type == 1)
            {
                d.AddLine(P(-7,-8),P(7,-8),gold,2*s); d.AddLine(P(-7,8),P(7,8),gold,2*s);
                d.AddLine(P(-5,-7),P(5,7),gold,s); d.AddLine(P(5,-7),P(-5,7),gold,s);
                d.AddTriangleFilled(P(-4,6),P(4,6),P(0,1),gold);
            }
            else
            {
                d.AddCircleFilled(c,9*s,dark,14); d.AddCircle(c,9*s,bright,14,s);
                d.AddCircle(c,6*s,gold,12,s); d.AddLine(P(-4,-4),P(4,4),bright,s);
                d.AddLine(P(-4,4),P(4,-4),gold,s);
                for(var i=0;i<6;i++) { var a=i*MathF.PI/3; d.AddCircleFilled(c+new Vector2(MathF.Cos(a),MathF.Sin(a))*7*s,1*s,gold,5); }
            }
        }
    }
}
