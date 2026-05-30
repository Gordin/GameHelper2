namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct BuffsOffsets
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x160)] public StdVector StatusEffectPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StatusEffectStruct
    {
        [FieldOffset(0x0008)] public IntPtr BuffDefinationPtr; //// BuffDefination.DAT file
        [FieldOffset(0x0018)] public float TotalTime;

        [FieldOffset(0x001C)] public float TimeLeft;

        // [FieldOffset(0x0020)] public float unknown0; // always set to 1.
        [FieldOffset(0x0028)] public uint SourceEntityId;

        //[FieldOffset(0x0030)] public long Unknown1;
        //[FieldOffset(0x0038)] public int unknown2;
        [FieldOffset(0x40)] public short Charges;
        [FieldOffset(0x42)] public short FlaskSlot; // read flask buff
        [FieldOffset(0x48)] public short Effectiveness; // read Withering Step skill gem buff
        //[FieldOffset(0x0044)] public short PAD_0x44;
        [FieldOffset(0x4A)] public uint UnknownIdAndEquipmentInfo; // same as in Actor.cs offset file -> ActiveSkillDetails struct.

        public override string ToString()
        {
            return $"BuffDefinationPtr: {this.BuffDefinationPtr.ToInt64():X} Total Time: {this.TotalTime} Time Left: {this.TimeLeft} " +
                $"Entity Id: {this.SourceEntityId} Charges: {this.Charges} Flask Slot: {this.FlaskSlot} " +
                $"Effectiveness: {100 + this.Effectiveness} (raw: {this.Effectiveness}) UnknownIdAndEquipmentInfo: {this.UnknownIdAndEquipmentInfo:X}";
        }
    }
}