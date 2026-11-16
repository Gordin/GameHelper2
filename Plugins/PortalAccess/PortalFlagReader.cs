// <copyright file="PortalFlagReader.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Buffers.Binary;
    using System.Text;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States.InGameState;

    /// <summary>Read flags only after the client's own debug method identifies their offsets.</summary>
    internal static class PortalFlagReader
    {
        internal sealed record Flags(int TargetOffset, int HiddenOffset, int QuestOffset, int ItemOffset,
            byte Targetable, byte HiddenFromPlayer, byte MeetsQuestState, byte MeetsItemRequirements);

        internal static bool TryRead(IRemoteMemory memory, PortalIdentity identity, long imageStart, int imageSize, out Flags? flags)
        {
            flags = null;
            bool InImage(long address, int count) => imageStart >= 0x10000 && imageSize > count &&
                address >= imageStart && address - imageStart <= imageSize - count;
            if (!memory.Read<EntityOffsets>(identity.Entity, out var entity) || entity.Id != identity.Id ||
                entity.ItemBase.EntityDetailsPtr.ToInt64() != identity.Details ||
                !memory.Read<ComponentHeader>(identity.Targetable, out var header) || header.EntityPtr.ToInt64() != identity.Entity ||
                !InImage(header.StaticPtr.ToInt64(), 14 * 8) ||
                !memory.Read<long>(header.StaticPtr.ToInt64() + 13 * 8, out var method) || !InImage(method, 240) ||
                !memory.ReadBytes(method, 240, out var code)) return false;

            // Match lea rdx,[rip+label]; mov rcx,rbx; call ...;
            // movzx edx,byte ptr [rdi+offset]; mov rcx,rax; call ... .
            // These are diagnostic prints, never instructions executed by this plugin.
            var target = -1; var hidden = -1; var quest = -1; var item = -1;
            for (var i = 0; i + 27 <= code.Length; i++)
            {
                if (code[i] != 0x48 || code[i + 1] != 0x8D || code[i + 2] != 0x15 ||
                    !code.AsSpan(i + 7, 4).SequenceEqual(new byte[] { 0x48, 0x8B, 0xCB, 0xE8 }) ||
                    !code.AsSpan(i + 15, 3).SequenceEqual(new byte[] { 0x0F, 0xB6, 0x57 }) ||
                    !code.AsSpan(i + 19, 4).SequenceEqual(new byte[] { 0x48, 0x8B, 0xC8, 0xE8 })) continue;
                var offset = code[i + 18];
                if (offset < 0x10 || offset >= 0x80) continue;
                var label = method + i + 7 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(i + 3, 4));
                if (!InImage(label, 64) || !memory.ReadBytes(label, 64, out var bytes)) return false;
                var name = Encoding.Unicode.GetString(bytes).Split('\0')[0];
                switch (name)
                {
                    case "Targetable: ": if (target != -1) return false; target = offset; break;
                    case "Hidden from Player: ": if (hidden != -1) return false; hidden = offset; break;
                    case "Meets Quest State: ": if (quest != -1) return false; quest = offset; break;
                    case "Meets Item Requirements: ": if (item != -1) return false; item = offset; break;
                }
            }
            if (target < 0 || hidden < 0 || quest < 0 || item < 0 ||
                target == hidden || target == quest || target == item || hidden == quest || hidden == item || quest == item ||
                !memory.ReadBytes(identity.Targetable, 0x80, out var data) ||
                data[target] > 1 || data[hidden] > 1 || data[quest] > 1 || data[item] > 1 ||
                !memory.Read<ComponentHeader>(identity.Targetable, out var after) ||
                after.EntityPtr != header.EntityPtr || after.StaticPtr != header.StaticPtr ||
                !memory.Read<EntityOffsets>(identity.Entity, out var afterEntity) || afterEntity.Id != identity.Id ||
                afterEntity.ItemBase.EntityDetailsPtr != entity.ItemBase.EntityDetailsPtr) return false;
            flags = new(target, hidden, quest, item, data[target], data[hidden], data[quest], data[item]);
            return true;
        }
    }
}
