namespace GameOffsets.Objects.Components
{
    using GameOffsets.Natives;
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ActorOffset
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x8B0)] public int AnimationId;
        [FieldOffset(0xB08)] public StdVector ActiveSkillsPtr; // ActiveSkillStructure // take and remove skill gem.
        [FieldOffset(0xB20)] public StdVector CooldownsPtr;
        // [FieldOffset(0xA70)] public StdVector VaalSoulsPtr;
        [FieldOffset(0xC28)] public StdVector DeployedEntityArray; // DeployedEntityStructure
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ActiveSkillStructure
    {
        public IntPtr ActiveSkillPtr; // ActiveSkillsDetails
        public IntPtr UselessPtr0;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ActiveSkillDetails // based on heuristics this structure size is 0x100
    {
        //[FieldOffset(0x00)] public IntPtr UselessPtr0;
        [FieldOffset(0x08)] public int UseStage;
        [FieldOffset(0x0C)] public int CastType;
        [FieldOffset(0x40)] public uint UnknownIdAndEquipmentInfo;
        //[FieldOffset(0x14)] public int Pad0x14;
        [FieldOffset(0x48)] public IntPtr GrantedEffectsPerLevelDatRow;
        [FieldOffset(0x20)] public IntPtr ActiveSkillsDatPtr; // this is just a placeholder. Caller is suppose to update this one.
        //[FieldOffset(0x28)] public int CurrentVaalSouls; // this is just a placeholder. Caller is suppose to update this one.
        //[FieldOffset(0x20)] public IntPtr UselessPtr1;
        //[FieldOffset(0x28)] public IntPtr Pad0x28;
        [FieldOffset(0x50)] public IntPtr GrantedEffectStatSetsPerLevelDatRow;
        //[FieldOffset(0x38)] public IntPtr UselessPtr2;
        //[FieldOffset(0x40)] public IntPtr Pad0x40;
        //[FieldOffset(0x80)] public bool CanBeUsedWithWeapon;
        //[FieldOffset(0x81)] public bool CannotBeUsed;
        //[FieldOffset(0x82)] public byte UnknownByte0;
        //[FieldOffset(0x83)] public byte UnknownByte1;
        [FieldOffset(0xE4)] public int TotalUses;
        [FieldOffset(0xE8)] public int TotalCooldownTimeInMs;
        //[FieldOffset(0x9C)] public int SoulsPerUse;
        //[FieldOffset(0xA0)] public int TotalVaalUses;
        //[FieldOffset(0xB0)] public IntPtr StatsPtr;
        //[FieldOffset(0xB8)] public IntPtr UselessPtr3;
        //[FieldOffset(0xD8)] public StdVector LinkedSupportedGems; // according to poe.exe each support gem structure size is 0x80
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ActiveSkillCooldown
    {
        [FieldOffset(0x00)] public int Unknown0;
        [FieldOffset(0x04)] public int Unknown1;
        [FieldOffset(0x08)] public int ActiveSkillsDatId;
        [FieldOffset(0x0C)] public int Unknown2;
        [FieldOffset(0x10)] public StdVector CooldownsList;
        [FieldOffset(0x28)] public IntPtr PtrToActorComponentPlusOffset;
        [FieldOffset(0x30)] public int MaxUses;
        [FieldOffset(0x34)] public int TotalCooldownTimeInMs;
        //[FieldOffset(0x38)] public int ActiveSkillsDatId;
        [FieldOffset(0x3C)] public uint UnknownIdAndEquipmentInf0; // same as in ActiveSkillDetails.
        //[FieldOffset(0x40)] public int Unknown2;
        [FieldOffset(0x44)] public int PAD_0x44;

        public int TotalActiveCooldowns()
        {
            return (int)this.CooldownsList.TotalElements(0x10);
        }

        public bool CannotBeUsed()
        {
            return this.TotalActiveCooldowns() >= this.MaxUses;
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct VaalSoulStructure
    {
        [FieldOffset(0x00)] public IntPtr ActiveSkillsDatPtr;
        [FieldOffset(0x08)] public IntPtr UselessPtr;
        [FieldOffset(0x10)] public int RequiredSouls;
        [FieldOffset(0x14)] public int CurrentSouls;
        [FieldOffset(0x18)] public long PAD_0x18;

        public readonly bool CannotBeUsed()
        {
            return this.CurrentSouls < this.RequiredSouls;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeployedEntityStructure
    {
        public int EntityId;
        public int ActiveSkillsDatId;
        public int DeployedObjectType;
        public int PAD_0x014;
        public int Counter;

        // PoE2 grew this element from 20 to 24 bytes. ReadStdVector rejects the
        // whole vector when its byte length isn't a multiple of the element size,
        // so an undersized struct here makes the deployed-entity count read 0.
        public int PAD_0x18;

        public override readonly string ToString()
        {
            return $"{this.DeployedObjectType} - {this.ActiveSkillsDatId} - {this.EntityId}";
        }
    }
}
