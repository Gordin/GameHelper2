// <copyright file="PortalAccessCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class PortalAccessCore : PCore<PortalAccessSettings>
    {
        private readonly PortalMemory memory = new();
        private readonly PatchLedger ledger = new();
        private int modifiedFields;
        private int failedAttempts;
        private int lastUndo;
        private readonly List<Observation> observations = new();
        private string areaKey = string.Empty;
        private string processSession = string.Empty;
        private long stableAt;
        private long nextSample;
        private string status = "waiting";
        private readonly Dictionary<PortalIdentity, PortalCandidate> known = new();
        private readonly PortalScanCursor awakeCursor = new();
        private readonly PortalScanCursor sleepingCursor = new();
        private bool sleepingFirst;
        private int scanPass;
        private int awakeSize;
        private int sleepingSize;
        private int scanReads;
        private string scanStopReason = "none";
        private string awakeStopReason = "none";
        private string sleepingStopReason = "none";
        private int awakeVisited;
        private int sleepingVisited;
        private bool scanComplete = true;
        private long lastDiagnosticAt;
        private string SettingsPath => Path.Combine(this.DllDirectory, "config", "settings.json");
        private sealed record Observation(PortalIdentity Identity, string Path, bool Valid, PortalFlagReader.Flags? VerifiedFlags,
            string Source, byte EntityState, string[] Components, string TargetableBytes);

        public override void OnEnable(bool isGameOpened)
        {
            try
            {
                if (File.Exists(this.SettingsPath))
                    this.Settings = JsonConvert.DeserializeObject<PortalAccessSettings>(File.ReadAllText(this.SettingsPath)) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PortalAccess] Settings: {ex.Message}");
                this.Settings = new();
            }
            this.Settings.RefreshIntervalMs = Math.Clamp(this.Settings.RefreshIntervalMs, 500, 10000);
            this.Settings.AreaDelayMs = Math.Clamp(this.Settings.AreaDelayMs, 500, 10000);
            this.ResetArea();
        }

        public override void OnDisable()
        {
            try { this.UndoCurrentArea(); this.SaveSettings(); }
            finally { this.memory.Dispose(); this.ResetArea(); }
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingsPath)!);
            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.TextWrapped(this.PluginText.T("settings.explanation", "Restore interaction and highlighting on closed portal entities that still exist in the current area."));
            ImGui.TextWrapped(this.PluginText.T("settings.experimental", "Only verified client layouts can be modified. Recovery cannot recreate a deleted portal or guarantee that its destination still accepts entry."));
            if (ImGui.Checkbox(this.PluginText.Label("settings.restore", "Restore portal interaction", "PortalRestore"), ref this.Settings.RestoreInteraction))
            {
                if (!this.Settings.RestoreInteraction) this.UndoCurrentArea();
                this.nextSample = 0;
                this.SaveSettings();
            }
            ImGui.TextWrapped(this.PluginText.T("settings.observe", "With recovery off, this plugin only reads portal data. Turning it off restores this plugin's changes only while the same area and portal identities remain valid."));
            var refreshChanged = ImGui.SliderInt(this.PluginText.Label("settings.interval", "Refresh interval (ms)", "PortalInterval"), ref this.Settings.RefreshIntervalMs, 500, 10000);
            var delayChanged = ImGui.SliderInt(this.PluginText.Label("settings.delay", "Wait after area changes (ms)", "PortalDelay"), ref this.Settings.AreaDelayMs, 500, 10000);
            if (refreshChanged || delayChanged) this.SaveSettings();
            ImGui.Separator();
            ImGui.TextWrapped(this.PluginText.F("status.line", "Status: {0}", this.StatusText()));
            ImGui.TextWrapped(this.PluginText.F("status.counts", "Portals: {0} | Modified fields: {1} | Failed attempts: {2} | Last undo: {3}", this.observations.Count, this.modifiedFields, this.failedAttempts, this.lastUndo));
            if (this.status == "access")
                ImGui.TextWrapped(this.PluginText.F("status.error", "Process access failed (code {0}). Check that both applications run with matching privileges.", this.memory.LastError));
            if (ImGui.Button(this.PluginText.Label("settings.export", "Export portal diagnostics", "PortalExport")))
                this.SaveDiagnostics();
            ImGui.TextWrapped(this.PluginText.T("settings.export_path", "Saved to Plugins/PortalAccess/config/portal-diagnostics.json every 10 seconds. Flag offsets are identified from the client's debug method; unrecognized layouts show no values. These flags cannot prove that a portal is usable."));
            ImGui.TextWrapped(this.PluginText.F("status.scan", "Scanned entities: awake {0}, sleeping {1}; retained portals {2}", this.awakeVisited, this.sleepingVisited, this.known.Count));
            if (!this.scanComplete) ImGui.TextWrapped(this.PluginText.T("status.scan_incomplete", "Discovery is continuing in batches. Found portals are retained; area changes reset progress. Export diagnostics to see the stopping reason."));
            if (ImGui.BeginTable("PortalStates", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, 180)))
            {
                ImGui.TableSetupColumn(this.PluginText.T("table.portal", "Portal ID"));
                ImGui.TableSetupColumn(this.PluginText.T("table.check", "Identity check"));
                ImGui.TableSetupColumn(this.PluginText.T("table.target", "Targetable flag"));
                ImGui.TableSetupColumn(this.PluginText.T("table.hidden", "Hidden from player"));
                ImGui.TableSetupColumn(this.PluginText.T("table.quest", "Quest requirements"));
                ImGui.TableSetupColumn(this.PluginText.T("table.item", "Item requirements"));
                ImGui.TableHeadersRow();
                foreach (var item in this.observations)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Identity.Id.ToString());
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Path);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(this.PluginText.T(item.Valid ? "table.valid" : "table.invalid", item.Valid ? "Passed" : "Skipped"));
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.VerifiedFlags?.Targetable.ToString() ?? "—");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.VerifiedFlags?.HiddenFromPlayer.ToString() ?? "—");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.VerifiedFlags?.MeetsQuestState.ToString() ?? "—");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(item.VerifiedFlags?.MeetsItemRequirements.ToString() ?? "—");
                }
                ImGui.EndTable();
            }
        }

        private string StatusText() => this.status switch
        {
            "settling" => this.PluginText.T("status.settling", "Waiting for the area to stabilize"),
            "access" => this.PluginText.T("status.access", "Cannot open the game process"),
            "observe" => this.PluginText.T("status.observe", "Read-only diagnostics; recovery is switched off"),
            "active" => this.PluginText.T("status.active", "Recovery enabled for the verified client layout"),
            "unsupported" => this.PluginText.T("status.unsupported", "Client layout not recognized; all recovery writes are blocked"),
            "scanning" => this.PluginText.T("status.scanning", "Scanning the area in batches; continuing from the previous position"),
            "empty" => this.PluginText.T("status.empty", "No portal entities found nearby"),
            "failed" => this.PluginText.T("status.failed", "Some checks were incomplete or rejected; unverified portals were skipped"),
            "error" => this.PluginText.T("status.exception", "Sampling stopped after an error; see Error.log"),
            _ => this.PluginText.T("status.waiting", "Waiting for the game or an area to finish loading"),
        };

        public override void DrawUI()
        {
            try { this.Sample(); }
            catch (Exception ex)
            {
                this.Settings.RestoreInteraction = false;
                this.memory.Dispose();
                this.ResetArea();
                this.status = "error";
                this.nextSample = Environment.TickCount64 + 5000;
                Console.WriteLine($"[PortalAccess] Sampling: {ex}");
            }
        }

        private string GetAreaKey()
        {
            if (Core.Process.Pid == 0 || Core.States.AreaLoading.Address == IntPtr.Zero || Core.States.AreaLoading.IsLoading ||
                Core.States.GameCurrentState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState)) return string.Empty;
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            if (area.Address == IntPtr.Zero || area.Player.Address == IntPtr.Zero || !area.Player.IsValid ||
                string.IsNullOrEmpty(area.AreaHash) || area.AreaHash == "0") return string.Empty;
            return $"{Core.Process.Pid}:{Core.States.AreaLoading.AreaRevision}:{area.Address.ToInt64():X}:{area.AreaHash}:{area.Player.Id}";
        }

        private void Sample()
        {
            var key = this.GetAreaKey();
            var now = Environment.TickCount64;
            if (key.Length == 0)
            {
                this.ResetArea();
                this.memory.Dispose();
                this.status = "waiting";
                return;
            }
            if (key != this.areaKey)
            {
                this.ResetArea();
                this.memory.Dispose();
                this.areaKey = key;
                this.stableAt = now + this.Settings.AreaDelayMs;
            }
            if (now < this.stableAt) { this.status = "settling"; return; }
            if (now < this.nextSample) return;
            this.nextSample = now + this.Settings.RefreshIntervalMs;
            if (!this.memory.Attach(Core.Process.Pid, false))
            {
                this.status = "access"; return;
            }
            if (this.processSession != this.memory.Session)
            {
                this.ledger.Clear();
                this.known.Clear();
                this.awakeCursor.Reset();
                this.sleepingCursor.Reset();
                this.processSession = this.memory.Session;
            }
            this.Collect();
            var errors = this.observations.Count(x => !x.Valid);
            this.status = errors > 0 ? "failed" : this.observations.Count == 0 ? (this.scanComplete ? "empty" : "scanning") : "observe";
            if (this.Settings.RestoreInteraction)
            {
                if (!this.memory.RecoveryLayoutVerified) this.status = "unsupported";
                else this.RecoverCurrentArea();
            }
            if (now - this.lastDiagnosticAt >= 10000)
            {
                this.SaveDiagnostics();
                this.lastDiagnosticAt = now;
            }
        }

        private void RecoverCurrentArea()
        {
            if (!this.TryRecoveryFence(out var fence)) return;
            var key = this.areaKey;
            var deadline = Environment.TickCount64 + 40;
            bool Ready() => Environment.TickCount64 <= deadline && this.areaKey == key && key == this.GetAreaKey() &&
                this.processSession == this.memory.Session && fence!.IsCurrent(this.memory);
            var failures = 0;
            foreach (var observation in this.observations)
            {
                if (!Ready()) break;
                var identity = observation.Identity;
                var writer = this.memory.CreateRecoveryWriter(identity, Ready);
                if (writer == null) { failures++; continue; }
                var result = this.ledger.Maintain(writer, identity, PortalRecoveryLayout.TargetOffset, PortalRecoveryLayout.HighlightOffset, _ => Ready());
                this.modifiedFields += result.Changed;
                failures += result.Failed;
            }
            this.failedAttempts += failures;
            if (failures > 0) this.status = "failed";
            else if (this.observations.Count > 0) this.status = "active";
        }

        private bool TryRecoveryFence(out PortalAreaFence? fence)
        {
            fence = null;
            return this.areaKey.Length > 0 && this.areaKey == this.GetAreaKey() && this.processSession == this.memory.Session &&
                this.memory.IsAlive() && this.memory.RecoveryLayoutVerified &&
                PortalAreaFence.TryCapture(this.memory, Core.States.AreaLoading.Address.ToInt64(),
                    Core.States.InGameStateObject.Address.ToInt64(), out fence) &&
                fence!.Area == Core.States.InGameStateObject.CurrentAreaInstance.Address.ToInt64() &&
                fence.Hash.ToString("X") == Core.States.InGameStateObject.CurrentAreaInstance.AreaHash &&
                fence.Player == Core.States.InGameStateObject.CurrentAreaInstance.Player.Address.ToInt64() &&
                fence.PlayerId == Core.States.InGameStateObject.CurrentAreaInstance.Player.Id;
        }

        private void UndoCurrentArea()
        {
            this.lastUndo = 0;
            if (this.ledger.Pending > 0 && this.TryRecoveryFence(out var fence))
            {
                var key = this.areaKey;
                var deadline = Environment.TickCount64 + 40;
                bool Ready() => Environment.TickCount64 <= deadline && key == this.GetAreaKey() &&
                    this.processSession == this.memory.Session && fence!.IsCurrent(this.memory);
                this.lastUndo = this.ledger.Restore(id => this.memory.CreateRecoveryWriter(id, Ready), _ => Ready());
            }
            this.ledger.Clear();
        }

        private void Collect()
        {
            this.observations.Clear();
            if (this.areaKey != this.GetAreaKey()) return;
            var budget = new ScanBudget(this.memory, () => this.areaKey == this.GetAreaKey() && this.memory.IsAlive());
            var instance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!budget.Read<AreaInstanceOffsets>(instance.Address.ToInt64(), out var data) ||
                data.CurrentAreaHash.ToString("X") != instance.AreaHash || data.PlayerInfo.LocalPlayerPtr != instance.Player.Address)
            { this.scanComplete = false; return; }
            this.awakeSize = data.Entities.AwakeEntities.Size;
            this.sleepingSize = data.Entities.SleepingEntities.Size;
            // Reserve only part of the shared budget for retained gates so discovery
            // still advances. Found candidates survive even if display/flag reads time out.
            var retainedBudget = new ScanBudget(budget, () => budget.CanContinue, maxReads: 1200, milliseconds: 5);
            var retainedScanner = new PortalScanner(retainedBudget, () => retainedBudget.CanContinue);
            foreach (var identity in this.known.Keys.ToArray())
            {
                if (!retainedBudget.CanContinue) break;
                if (retainedScanner.TryReadCandidate(identity.Entity, identity.Id, "retained", out var candidate) && candidate!.Identity == identity)
                    this.Observe(candidate, retainedBudget);
                else if (retainedBudget.CanContinue) this.known.Remove(identity);
            }

            if (this.awakeCursor.Complete && this.sleepingCursor.Complete)
            {
                this.awakeCursor.Reset(); this.sleepingCursor.Reset(); this.scanPass++;
            }
            var scanner = new PortalScanner(budget, () => budget.CanContinue);
            ScanResult awake, sleeping;
            if (this.sleepingFirst)
            {
                sleeping = scanner.ScanPage(data.Entities.SleepingEntities, "sleeping-native", this.sleepingCursor);
                awake = scanner.ScanPage(data.Entities.AwakeEntities, "awake-native", this.awakeCursor);
            }
            else
            {
                awake = scanner.ScanPage(data.Entities.AwakeEntities, "awake-native", this.awakeCursor);
                sleeping = scanner.ScanPage(data.Entities.SleepingEntities, "sleeping-native", this.sleepingCursor);
            }
            this.sleepingFirst = !this.sleepingFirst;
            foreach (var candidate in awake.Portals.Concat(sleeping.Portals))
            {
                // This bookkeeping needs no further game reads. Do it before checking
                // the exhausted budget, or a portal found in a large area is lost forever.
                if (this.known.Count < 128 || this.known.ContainsKey(candidate.Identity)) this.known[candidate.Identity] = candidate;
                if (budget.CanContinue && !this.observations.Any(x => x.Identity == candidate.Identity)) this.Observe(candidate, budget);
            }
            this.awakeVisited = this.awakeCursor.Examined;
            this.sleepingVisited = this.sleepingCursor.Examined;
            this.scanComplete = awake.Complete && sleeping.Complete;
            this.awakeStopReason = awake.StopReason;
            this.sleepingStopReason = sleeping.StopReason;
            this.scanReads = budget.ReadsUsed;
            this.scanStopReason = budget.StopReason;
            if (this.areaKey != this.GetAreaKey() || !this.memory.IsAlive())
            {
                this.observations.Clear();
                this.known.Clear();
                this.awakeCursor.Reset(); this.sleepingCursor.Reset();
                this.scanComplete = false;
            }
        }

        private void Observe(PortalCandidate candidate, ScanBudget budget)
        {
            if (!budget.CanContinue) return;
            var identity = candidate.Identity;
            var valid = budget.Read<GameOffsets.Objects.Components.ComponentHeader>(identity.Targetable, out var header) && header.EntityPtr.ToInt64() == identity.Entity;
            PortalFlagReader.Flags? flags = null;
            if (valid) PortalFlagReader.TryRead(budget, identity, this.memory.ImageStart, this.memory.ImageSize, out flags);
            var raw = budget.ReadBytes(identity.Targetable, 0x80, out var bytes) ? Convert.ToHexString(bytes) : string.Empty;
            if (budget.CanContinue) this.observations.Add(new(identity, candidate.Path, valid, flags, candidate.Source, candidate.EntityState, candidate.Components, raw));
        }

        private void SaveDiagnostics()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingsPath)!);
                File.WriteAllText(Path.Combine(this.DllDirectory, "config", "portal-diagnostics.json"),
                    JsonConvert.SerializeObject(new { CapturedUtc = DateTime.UtcNow, Status = this.status,
                        Area = this.areaKey, this.Settings.RestoreInteraction,
                        Mode = this.Settings.RestoreInteraction ? "verified-two-flag-recovery" : "read-only",
                        WritesBlocked = !this.Settings.RestoreInteraction || !this.memory.RecoveryLayoutVerified,
                        RecoveryProfile = this.memory.RecoveryLayout?.Name,
                        FlagOffsetsVerified = this.observations.Count > 0 && this.observations.All(x => x.VerifiedFlags != null),
                        FlagVerification = "client-debug-labels-read-only", RecoveryVerified = false,
                        ModifiedFields = this.modifiedFields, FailedAttempts = this.failedAttempts, LastUndo = this.lastUndo,
                        Collector = "native-paged-read-only-v4", AwakeVisited = this.awakeVisited,
                        SleepingVisited = this.sleepingVisited, ScanComplete = this.scanComplete,
                        AwakeSize = this.awakeSize, SleepingSize = this.sleepingSize,
                        ScanPass = this.scanPass, ScanReads = this.scanReads, ScanStopReason = this.scanStopReason,
                        AwakeStopReason = this.awakeStopReason, SleepingStopReason = this.sleepingStopReason,
                        AwakeAfterId = this.awakeCursor.AfterId, SleepingAfterId = this.sleepingCursor.AfterId,
                        RetainedPortals = this.known.Count, LastProcessError = this.memory.LastError,
                        Portals = this.observations }, Formatting.Indented));
            }
            catch (IOException ex) { Console.WriteLine($"[PortalAccess] Diagnostics: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Console.WriteLine($"[PortalAccess] Diagnostics: {ex.Message}"); }
        }

        private void ResetArea()
        {
            // Never write undo bytes through an address from a previous area/session.
            this.ledger.Clear();
            this.modifiedFields = 0;
            this.failedAttempts = 0;
            this.areaKey = string.Empty;
            this.known.Clear();
            this.awakeCursor.Reset(); this.sleepingCursor.Reset();
            this.sleepingFirst = false;
            this.scanPass = 0;
            this.awakeSize = 0; this.sleepingSize = 0;
            this.scanReads = 0; this.scanStopReason = "none";
            this.awakeStopReason = "none"; this.sleepingStopReason = "none";
            this.observations.Clear();
            this.awakeVisited = 0;
            this.sleepingVisited = 0;
            this.scanComplete = true;
            this.lastDiagnosticAt = 0;
            this.nextSample = 0;
        }
    }
}
