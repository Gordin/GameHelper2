// <copyright file="Buffs.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Concurrent;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.FilesStructures;
    using ImGuiNET;
    using Utils;

    /// <summary>
    ///     The <see cref="Buffs" /> component in the entity.
    /// </summary>
    public class Buffs : ComponentBase
    {

        /// <summary>
        ///     Initializes a new instance of the <see cref="Buffs" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Buffs" /> component.</param>
        public Buffs(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the Buffs/Debuffs associated with the entity.
        ///     This is not updated anymore once entity dies.
        /// </summary>
        public ConcurrentDictionary<string, StatusEffectStruct> StatusEffects { get; } = new();

        public bool[] FlaskActive { get; private set; } = new bool[5];

        /// <inheritdoc />
        internal override void ToImGui()
        {
            base.ToImGui();
            if (ImGui.TreeNode("Status Effect (Buffs/Debuffs)"))
            {
                foreach (var kv in this.StatusEffects)
                {
                    if (ImGui.TreeNode($"{kv.Key}"))
                    {
                        ImGuiHelper.DisplayTextAndCopyOnClick($"Name: {kv.Key}", kv.Key);
                        ImGuiHelper.IntPtrToImGui("BuffDefinationPtr", kv.Value.BuffDefinationPtr);
                        ImGuiHelper.DisplayFloatWithInfinitySupport("Total Time:", kv.Value.TotalTime);
                        ImGuiHelper.DisplayFloatWithInfinitySupport("Time Left:", kv.Value.TimeLeft);
                        ImGui.Text($"Source Entity Id: {kv.Value.SourceEntityId}");
                        ImGui.Text($"Raw Stage: {kv.Value.RawStage}");
                        ImGui.Text($"Charges: {kv.Value.Charges}");
                        ImGui.Text($"Source FlaskSlot: {kv.Value.FlaskSlot}");
                        ImGui.Text($"Source Effectiveness: {100 + kv.Value.Effectiveness} (raw value: {kv.Value.Effectiveness})");
                        ImGui.Text($"Source UnknownIdAndEquipmentInfo: {kv.Value.UnknownIdAndEquipmentInfo:X}");
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<BuffsOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.StatusEffects.Clear();
            var statusEffects = reader.ReadStdVector<IntPtr>(data.StatusEffectPtr);
            Array.Fill(this.FlaskActive, false);

            // F-129: snapshot the 4-level Player.Id chain once. Each access goes
            // through RemoteObjectBase.Address.get (a lock); re-traversing per-loop
            // is both costly and racy during state transitions. NRE during the
            // chain -> playerId stays uint.MaxValue, all flask matches fail
            // (statusEffectData.SourceEntityId is uint, max value is unreachable).
            uint playerId;
            try
            {
                playerId = Core.States.InGameStateObject.CurrentAreaInstance.Player.Id;
            }
            catch (NullReferenceException)
            {
                playerId = uint.MaxValue;
            }

            for (var i = 0; i < statusEffects.Length; i++)
            {
                var statusEffectData = reader.ReadMemory<StatusEffectStruct>(statusEffects[i]);
                if (statusEffectData.BuffDefinationPtr == IntPtr.Zero)
                {
                    continue;
                }

                if (playerId != statusEffectData.SourceEntityId)
                {
                    statusEffectData.FlaskSlot = -1;
                }

                MiscHelper.ActiveSkillGemDataParser(
                    statusEffectData.UnknownIdAndEquipmentInfo,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out var skillGemUnknownId);

                var (effectName, effectType) = ((string, byte))Core.GgpkObjectCache.AddOrGetExisting(
                    statusEffectData.BuffDefinationPtr, key =>
                    {
                        return this.GetNameFromBuffDefination(key);
                    });

                if (effectType != 0x4) // Flask Effect Type is 4.
                {
                    statusEffectData.FlaskSlot = -1;
                }
                else if (statusEffectData.FlaskSlot >= 0 && statusEffectData.FlaskSlot < 5)
                {
                    this.FlaskActive[statusEffectData.FlaskSlot] = true;
                }

                if (skillGemUnknownId != 0)
                {
                    effectName += $"_{skillGemUnknownId:X}";
                }

                this.StatusEffects.AddOrUpdate(effectName, statusEffectData, (key, oldValue) =>
                {
                    var incomingStacks = statusEffectData.Charges > 0 ? statusEffectData.Charges : (short)1;
                    statusEffectData.Charges = (short)(oldValue.Charges + incomingStacks);
                    statusEffectData.TimeLeft = Math.Max(oldValue.TimeLeft, statusEffectData.TimeLeft);
                    return statusEffectData;
                });
            }
        }

        /// <summary>
        ///     Adds a status effect read from an entity-backed player skill to the player's
        ///     normal status-effect collection. A real player status effect with the same
        ///     name wins unless the synthetic effect reports more stacks.
        /// </summary>
        /// <param name="effectName">Name used by normal status-effect consumers.</param>
        /// <param name="statusEffectData">Status-effect data with its stage represented as charges.</param>
        internal void AddSyntheticStatusEffect(string effectName, StatusEffectStruct statusEffectData)
        {
            this.StatusEffects.AddOrUpdate(effectName, statusEffectData, (_, oldValue) =>
            {
                return statusEffectData.Charges > oldValue.Charges ? statusEffectData : oldValue;
            });
        }

        private (string, byte) GetNameFromBuffDefination(IntPtr addr)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<BuffDefinitionsOffset>(addr);
            return (reader.ReadUnicodeString(data.Name), data.BuffType);
        }
    }
}
