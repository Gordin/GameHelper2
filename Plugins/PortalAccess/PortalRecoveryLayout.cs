// <copyright file="PortalRecoveryLayout.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using GameOffsets.Objects.Components;

    /// <summary>An explicitly verified client layout, never a fallback to framework offsets.</summary>
    internal static class PortalRecoveryLayout
    {
        internal const int TargetOffset = 0x69;
        internal const int HighlightOffset = 0x6A;
        internal sealed record VerifiedLayout(string Name, long VtableRva);
        internal sealed record CodeFingerprint(int Rva, int Length, string Sha256);
        internal sealed record ClientProfile(VerifiedLayout Layout, IReadOnlyList<CodeFingerprint> Code);

        internal static readonly VerifiedLayout October5 = new("poe2-2026-10-05-targetable-69-6a", 0x33A6028);
        internal static readonly VerifiedLayout October6 = new("poe2-2026-10-06-targetable-69-6a", 0x33A7338);
        internal static readonly VerifiedLayout October10 = new("poe2-2026-10-10-targetable-69-6a", 0x33A7308);
        private static readonly ClientProfile[] KnownProfiles =
        {
            new(October10, new CodeFingerprint[]
            {
                new(0x1729820, 608, "386623AD54CAB674B53A898488DE78C050484C5A7A88ABFB9C23E9FB0A740082"),
                new(0x172B700, 240, "20A12BAF44B5BBBC1DE7DABE6E76381457F883C667FEEA1AA3569E334FB6C278"),
                new(0x172B940, 80, "65F64DA3AF977255CE016B950A36B889C211023BE529E512D3742D6B258FDCFB"),
                new(0x1729EBE, 9, "A497DC80926369A87E18ABCA88FDE4FAB76758FE7114DE45EF0025081D2C893B"),
            }),
            new(October6, new CodeFingerprint[]
            {
                new(0x1729820, 608, "DF314CD23EC4AABD6C1872A55655CE882621EA2F9162B8D218F5026EA7548639"),
                new(0x172B700, 240, "A19C0BC0C0E3F4AE69745613F546700AFF33B8EDF39EB2C22BC18D60EB3F4CDA"),
                new(0x172B940, 80, "65F64DA3AF977255CE016B950A36B889C211023BE529E512D3742D6B258FDCFB"),
                new(0x1729EBE, 9, "A497DC80926369A87E18ABCA88FDE4FAB76758FE7114DE45EF0025081D2C893B"),
            }),
            new(October5, new CodeFingerprint[]
            {
                new(0x1729590, 608, "125F2D4B70B33E7BD47C061B8024F6B8ABA2807ED0A76933E5CB927066766835"),
                new(0x172B470, 240, "4D2CB2DE011119979BE72CF5C651C297ED24D1CE6FAB476EF490B07596CA6CAB"),
                new(0x172B6B0, 80, "65F64DA3AF977255CE016B950A36B889C211023BE529E512D3742D6B258FDCFB"),
                new(0x1729C2E, 9, "31C5077B4D28B469A92D0476B0E02DF7EA04FE0ECAC80643D9900DB440890CB3"),
            }),
        };

        // Full instruction blocks from the inspected image: highlight/outline update,
        // named diagnostic prints, target predicate, and vector destruction at +0x50.
        // No code is executed or patched. ASLR does not change these relative instructions.
        internal static VerifiedLayout? IdentifyImage(IRemoteMemory memory, long image, int size)
            => IdentifyImage(memory, image, size, KnownProfiles);

        // The overload permits synthetic profiles in own-process tests. The live handle
        // always calls the overload above, which uses only the reviewed built-in profiles.
        internal static VerifiedLayout? IdentifyImage(IRemoteMemory memory, long image, int size, IReadOnlyList<ClientProfile> profiles)
        {
            foreach (var profile in profiles)
                if (MatchesProfile(memory, image, size, profile)) return profile.Layout;
            return null;
        }

        internal static bool MatchesProfile(IRemoteMemory memory, long image, int size, ClientProfile profile)
        {
            if (!PortalMemory.AddressValid(image) || size <= 0 || !PortalMemory.AddressValid(image + size - 1L) ||
                profile.Layout.VtableRva < 0 || profile.Layout.VtableRva > size - 14 * 8L || profile.Code.Count != 4) return false;
            foreach (var part in profile.Code)
            {
                if (part.Rva < 0 || part.Length <= 0 || part.Length > 8192 || part.Rva > size - part.Length ||
                    !memory.ReadBytes(image + part.Rva, part.Length, out var code) || code.Length != part.Length ||
                    Convert.ToHexString(SHA256.HashData(code)) != part.Sha256) return false;
            }
            return true;
        }

        internal static bool ValidatePortal(IRemoteMemory memory, PortalIdentity identity, long image, int size, VerifiedLayout layout)
        {
            if (!new PortalScanner(memory).TryReadCandidate(identity.Entity, identity.Id, "recovery-validation", out var candidate) ||
                candidate!.Identity != identity || identity.Portal == 0 || candidate.EntityState != 12 ||
                !PortalScanner.IsSupportedPortalPath(candidate.Path) ||
                !memory.Read<ComponentHeader>(identity.Portal, out var gate) || gate.EntityPtr.ToInt64() != identity.Entity ||
                !memory.Read<ComponentHeader>(identity.Targetable, out var target) || target.EntityPtr.ToInt64() != identity.Entity ||
                target.StaticPtr.ToInt64() != image + layout.VtableRva ||
                !PortalFlagReader.TryRead(memory, identity, image, size, out var flags) ||
                flags!.TargetOffset != TargetOffset || flags.HiddenOffset != 0x73 || flags.QuestOffset != 0x6E || flags.ItemOffset != 0x6F ||
                flags.HiddenFromPlayer != 0 || flags.MeetsQuestState != 1 || flags.MeetsItemRequirements != 1 ||
                !memory.ReadByte(identity.Targetable + HighlightOffset, out var highlight) || highlight > 1) return false;
            return true;
        }
    }

    /// <summary>Restricts both repair and undo to the two verified boolean fields.</summary>
    internal sealed class PortalWriteGate : IByteMemory
    {
        private readonly PortalIdentity portal;
        private readonly IByteMemory memory;
        private readonly Func<bool> validate;
        internal PortalWriteGate(PortalIdentity portal, IByteMemory memory, Func<bool> validate)
        { this.portal = portal; this.memory = memory; this.validate = validate; }
        private bool Allowed(long address) => address == this.portal.Targetable + PortalRecoveryLayout.TargetOffset ||
            address == this.portal.Targetable + PortalRecoveryLayout.HighlightOffset;
        public bool ReadByte(long address, out byte value)
        { value = default; return this.Allowed(address) && this.memory.ReadByte(address, out value); }
        public bool WriteByte(long address, byte value) => value <= 1 && this.Allowed(address) && this.validate() &&
            this.memory.ReadByte(address, out var current) && current <= 1 && this.validate() && this.memory.WriteByte(address, value);
    }
}
