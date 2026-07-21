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

        private readonly ConcurrentDictionary<string, IntPtr> statusEffectAddresses = new();

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
                        if (this.statusEffectAddresses.TryGetValue(kv.Key, out var effectAddr))
                        {
                            ImGuiHelper.IntPtrToImGui("Effect Address", effectAddr);
                            if (ImGui.TreeNode("Raw Bytes Hex Dump"))
                            {
                                var rawBytes = Core.Process.Handle.ReadMemoryArray<byte>(effectAddr, 80);
                                if (rawBytes != null && rawBytes.Length > 0)
                                {
                                    for (int offset = 0; offset < rawBytes.Length; offset += 16)
                                    {
                                        var hexPart = new System.Text.StringBuilder();
                                        for (int j = 0; j < 16 && offset + j < rawBytes.Length; j++)
                                        {
                                            hexPart.Append($"{rawBytes[offset + j]:X2} ");
                                        }
                                        ImGui.Text($"+0x{offset:X2}: {hexPart}");
                                    }
                                }
                                ImGui.TreePop();
                            }
                        }
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
            this.statusEffectAddresses.Clear();
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

                this.statusEffectAddresses[effectName] = statusEffects[i];
                this.StatusEffects.AddOrUpdate(effectName, statusEffectData, (key, oldValue) =>
                {
                    var incomingStacks = statusEffectData.Charges > 0 ? statusEffectData.Charges : (short)1;
                    statusEffectData.Charges = (short)(oldValue.Charges + incomingStacks);
                    statusEffectData.TimeLeft = Math.Max(oldValue.TimeLeft, statusEffectData.TimeLeft);
                    return statusEffectData;
                });
            }
        }

        private (string, byte) GetNameFromBuffDefination(IntPtr addr)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<BuffDefinitionsOffset>(addr);
            return (reader.ReadUnicodeString(data.Name), data.BuffType);
        }
    }
}