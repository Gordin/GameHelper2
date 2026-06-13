// <copyright file="Actor.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Generic;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.FilesStructures;
    using ImGuiNET;
    using RemoteEnums;

    /// <summary>
    ///     The <see cref="Actor" /> component in the entity.
    /// </summary>
    public class Actor : ComponentBase
    {
        // private Dictionary<IntPtr, VaalSoulStructure> ActiveSkillsVaalSouls { get; } = new();

        private Dictionary<uint, ActiveSkillCooldown> ActiveSkillCooldowns { get; } = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Actor" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Actor" /> component.</param>
        public Actor(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating what the player is doing.
        /// </summary>
        public Animation Animation { get; private set; }

        /// <summary>
        ///     Gets the details of all known Active Skills.
        /// </summary>
        public Dictionary<string, ActiveSkillDetails> ActiveSkills { get; } = new();

        /// <summary>
        ///     Gets a value indicating if the skill can be used or not.
        /// </summary>
        public HashSet<string> IsSkillUsable { get; } = new();

        /// <summary>
        ///     Gets the "command minion" skills available on this player's summoned minions,
        ///     keyed by skill name (e.g. "KnifeThrow"). The value is <c>true</c> when the command
        ///     is usable on at least one summoned minion (i.e. that minion has it off cooldown).
        ///     These commands carry their cooldown on the minion, not the player, so they never
        ///     appear in <see cref="IsSkillUsable" />. Only populated for the player's own actor.
        /// </summary>
        public Dictionary<string, bool> MinionCommandSkills { get; } = new();

        /// <summary>
        ///     Gets the names of <see cref="MinionCommandSkills" /> that are currently usable on at
        ///     least one summoned minion. Parallels <see cref="IsSkillUsable" /> for minion commands.
        /// </summary>
        public HashSet<string> UsableMinionCommandSkills { get; } = new();

        /// <summary>
        ///     Gets the total number of entities of a given DeployedObjectType is deployed by this entity.
        /// </summary>
        public int[] DeployedEntities { get; private set; } = new int[256];

        /// <summary>
        ///     Converts the <see cref="Actor" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"AnimationId: 0x{(int)this.Animation:X}, Animation: {this.Animation}");
            // if (ImGui.TreeNode("Vaal Souls"))
            // {
            //     foreach(var (skillNamePtr, skillDetails) in this.ActiveSkillsVaalSouls)
            //     {
            //         if (ImGui.TreeNode($"{skillNamePtr.ToInt64():X}"))
            //         {
            //             ImGui.Text($"Required Souls: {skillDetails.RequiredSouls}");
            //             ImGui.Text($"Current Souls: {skillDetails.CurrentSouls}");
            //             ImGui.TreePop();
            //         }
            //     }
            //
            //     ImGui.TreePop();
            // }

            if (ImGui.TreeNode("Cooldowns"))
            {
                foreach (var (skillId, skillDetails) in this.ActiveSkillCooldowns)
                {
                    if (ImGui.TreeNode($"{skillId:X}"))
                    {
                        ImGui.Text($"Active Skill Id: {skillDetails.ActiveSkillsDatId}");
                        ImGuiHelper.IntPtrToImGui(
                            $"Cooldowns Vector (Length {skillDetails.TotalActiveCooldowns()})",
                            skillDetails.CooldownsList.First);
                        ImGui.Text($"Max Uses: {skillDetails.MaxUses}");
                        ImGui.Text($"Total Cooldown Time (ms): {skillDetails.TotalCooldownTimeInMs}");
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Active Skills"))
            {
                foreach (var (skillname, skilldetails) in this.ActiveSkills)
                {
                    if (ImGui.TreeNode($"{skillname}"))
                    {
                        ImGui.Text($"Use Stage: {skilldetails.UseStage}");
                        ImGui.Text($"Cast Type: {skilldetails.CastType}");
                        ImGui.Text($"Skill UnknownIdAndEquipmentInfo: {skilldetails.UnknownIdAndEquipmentInfo:X}");
                        MiscHelper.ActiveSkillGemDataParser(
                            skilldetails.UnknownIdAndEquipmentInfo,
                            out var iue,
                            out var iu,
                            out var si,
                            out var li,
                            out var inv,
                            out var uid);
                        ImGui.Text($"Can skill be on player item: {iue}");
                        ImGui.Text($"Not sure what this does (something related to vaal skill): {iu}");
                        ImGui.Text($"Skill Gem link Number: {li}");
                        ImGui.Text($"Skill Gem socket Number: {si}");
                        ImGui.Text($"Skill Gem Inventory Slot: {(InventoryName)inv}");
                        ImGui.Text($"Skill Gem Name Hash: {uid:X}");
                        ImGuiHelper.IntPtrToImGui("Granted Effects Ptr", skilldetails.GrantedEffectsPerLevelDatRow);
                        ImGuiHelper.IntPtrToImGui($"Active Skills Ptr", skilldetails.ActiveSkillsDatPtr);
                        ImGuiHelper.IntPtrToImGui("Granted Effect Stat Sets Per Level Ptr", skilldetails.GrantedEffectStatSetsPerLevelDatRow);
                        //ImGui.Text($"Can be used with weapons: {skilldetails.CanBeUsedWithWeapon}");
                        //ImGui.Text($"Can not be used: {skilldetails.CannotBeUsed}");
                        //ImGui.Text($"Current Vaal Soul (-1 if not vaal skill): {skilldetails.CurrentVaalSouls}");
                        //ImGui.Text($"Unknown0: {skilldetails.UnknownByte0}");
                        //ImGui.Text($"Unknown1: {skilldetails.UnknownByte1}");
                        ImGui.Text($"Total Uses: {skilldetails.TotalUses}");
                        ImGui.Text($"Cooldown Time (ms): {skilldetails.TotalCooldownTimeInMs}");
                        //ImGui.Text($"Souls per use: {skilldetails.SoulsPerUse}");
                        //ImGui.Text($"Total Vaal Uses: {skilldetails.TotalVaalUses}");
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Can use skills"))
            {
                foreach (var skill in this.IsSkillUsable)
                {
                    ImGui.Text($"Skill {skill} can be used.");
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Deployed Objects"))
            {
                ImGui.Text("Please throw mines, totem, minons, traps, etc to populate the data over here.");
                for (var i = 0; i < this.DeployedEntities.Length; i++)
                {
                    if (this.DeployedEntities[i] > 0)
                    {
                        ImGui.Text($"Object Type: {i}, Total Count: {this.DeployedEntities[i]}");
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Minion Cooldowns"))
            {
                ImGui.TextWrapped("Command skills grouped by minion. Each line: skill name, cooldown " +
                    "key, datId, active uses, and whether it is currently unusable (active == uses).");
                var cdReader = Core.Process.Handle;
                var cdAreaState = Core.States.InGameStateObject.CurrentAreaInstance;
                foreach (var (_, entity) in cdAreaState.AwakeEntities)
                {
                    if (!entity.IsValid || !entity.TryGetComponent<Actor>(out var minionActor))
                    {
                        continue;
                    }

                    if (minionActor.Address == this.Address || minionActor.ActiveSkillCooldowns.Count == 0)
                    {
                        continue;
                    }

                    if (ImGui.TreeNode($"{entity.Path}##cd{entity.Id}"))
                    {
                        // Resolve each cooldown's skill name from this minion's active skills (matched
                        // by the minion's own key, which is internally consistent within one entity).
                        var keyToName = new Dictionary<uint, string>();
                        var minionData = cdReader.ReadMemory<ActorOffset>(minionActor.Address);
                        var minionSkills = cdReader.ReadStdVector<ActiveSkillStructure>(minionData.ActiveSkillsPtr);
                        for (var i = 0; i < minionSkills.Length; i++)
                        {
                            if (minionSkills[i].ActiveSkillPtr == IntPtr.Zero)
                            {
                                continue;
                            }

                            var sd = cdReader.ReadMemory<ActiveSkillDetails>(minionSkills[i].ActiveSkillPtr);
                            if (sd.GrantedEffectsPerLevelDatRow == IntPtr.Zero)
                            {
                                continue;
                            }

                            var (skillName, _) = ((string, IntPtr))Core.GgpkObjectCache.AddOrGetExisting(
                                sd.GrantedEffectsPerLevelDatRow,
                                key => (cdReader.ReadUnicodeString(cdReader.ReadMemory<IntPtr>(key)), key));
                            keyToName[sd.UnknownIdAndEquipmentInfo] = skillName;
                        }

                        foreach (var (cdKey, cd) in minionActor.ActiveSkillCooldowns)
                        {
                            var name = keyToName.TryGetValue(cdKey, out var n) ? n : "?";
                            ImGui.Text(
                                $"{name}: key=0x{cdKey:X} datId={cd.ActiveSkillsDatId} " +
                                $"active={cd.TotalActiveCooldowns()} cannotUse={cd.CannotBeUsed()}");
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
            var data = reader.ReadMemory<ActorOffset>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Animation = (Animation)data.AnimationId;
            this.IsSkillUsable.Clear();
            this.ActiveSkills.Clear();
            this.ActiveSkillCooldowns.Clear();
            // var skillsvaalsouls = reader.ReadStdVector<VaalSoulStructure>(data.VaalSoulsPtr);
            // for (var i = 0; i < skillsvaalsouls.Length; i++)
            // {
            //     this.ActiveSkillsVaalSouls[skillsvaalsouls[i].ActiveSkillsDatPtr] = skillsvaalsouls[i];
            // }

            var cooldowns = reader.ReadStdVector<ActiveSkillCooldown>(data.CooldownsPtr);
            for (var i = 0; i < cooldowns.Length; i++)
            {
                this.ActiveSkillCooldowns[cooldowns[i].UnknownIdAndEquipmentInf0] = cooldowns[i];
            }

            var activeSkills = reader.ReadStdVector<ActiveSkillStructure>(data.ActiveSkillsPtr);
            for (var i = 0; i < activeSkills.Length; i++)
            {
                var skillDetails = reader.ReadMemory<ActiveSkillDetails>(activeSkills[i].ActiveSkillPtr);
                if (skillDetails.GrantedEffectsPerLevelDatRow == IntPtr.Zero ||
                    (skillDetails.UnknownIdAndEquipmentInfo >> 0x10) < 0x8000)
                {
                    // No usecase for these skills.
                    // this.ActiveSkills[i.ToString()] = skillDetails;
                }
                else
                {
                    (var name, skillDetails.ActiveSkillsDatPtr) = ((string, IntPtr))Core.GgpkObjectCache.
                        AddOrGetExisting(skillDetails.GrantedEffectsPerLevelDatRow, (key) =>
                        {
                            return (reader.ReadUnicodeString(reader.ReadMemory<IntPtr>(key)), key);
                        });

                    // skillDetails.CurrentVaalSouls = -1;
                    var cannotbeused = false;
                    if (this.ActiveSkillCooldowns.TryGetValue(skillDetails.UnknownIdAndEquipmentInfo, out var cooldownInfo))
                    {
                        cannotbeused |= cooldownInfo.CannotBeUsed();
                    }
                    // else if (this.ActiveSkillsVaalSouls.TryGetValue(skillDetails.ActiveSkillsDatPtr, out var vaalSoulInfo))
                    // {
                    //     skillDetails.CurrentVaalSouls = vaalSoulInfo.CurrentSouls;
                    //     cannotbeused |= vaalSoulInfo.CannotBeUsed();
                    // }

                    this.ActiveSkills[name] = skillDetails;
                    if (!cannotbeused)
                    {
                        this.IsSkillUsable.Add(name);
                    }
                }
            }

            Array.Fill(this.DeployedEntities, 0);
            var deployedEntities = reader.ReadStdVector<DeployedEntityStructure>(data.DeployedEntityArray);
            for (var i = 0; i < deployedEntities.Length; i++)
            {
                if (deployedEntities[i].DeployedObjectType < this.DeployedEntities.Length &&
                    deployedEntities[i].DeployedObjectType >= 0)
                {
                    this.DeployedEntities[deployedEntities[i].DeployedObjectType]++;
                }
            }

            this.UpdateMinionCommandSkills(deployedEntities);
        }

        /// <summary>
        ///     Aggregates the "command minion" skills off the player's summoned minions into
        ///     <see cref="MinionCommandSkills" /> / <see cref="UsableMinionCommandSkills" />. Only the
        ///     player's own actor scans (gated on the owner being the current player); other actors
        ///     just clear their (empty) collections cheaply.
        /// </summary>
        /// <param name="deployedEntities">the deployed-entity array already read for this actor.</param>
        private void UpdateMinionCommandSkills(DeployedEntityStructure[] deployedEntities)
        {
            this.MinionCommandSkills.Clear();
            this.UsableMinionCommandSkills.Clear();

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            if (deployedEntities.Length == 0 ||
                this.OwnerEntityAddress == IntPtr.Zero ||
                this.OwnerEntityAddress != area.Player.Address)
            {
                return;
            }

            var deployedIds = new HashSet<uint>();
            for (var i = 0; i < deployedEntities.Length; i++)
            {
                deployedIds.Add((uint)deployedEntities[i].EntityId);
            }

            foreach (var (_, entity) in area.AwakeEntities)
            {
                if (!entity.IsValid ||
                    !deployedIds.Contains(entity.Id) ||
                    !entity.TryGetComponent<Actor>(out var minionActor))
                {
                    continue;
                }

                minionActor.CollectCommandSkills(this.MinionCommandSkills);
            }

            foreach (var (name, usable) in this.MinionCommandSkills)
            {
                if (usable)
                {
                    this.UsableMinionCommandSkills.Add(name);
                }
            }
        }

        /// <summary>
        ///     Reads this (minion) actor's "command minion" skills (keys in the <c>0x40000000</c>
        ///     range) and folds them into <paramref name="aggregate" /> by name, OR-ing usability so
        ///     a command counts as usable when any minion has it off cooldown.
        /// </summary>
        /// <param name="aggregate">player-level name -> usable map to add to.</param>
        private void CollectCommandSkills(Dictionary<string, bool> aggregate)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ActorOffset>(this.Address);
            var skills = reader.ReadStdVector<ActiveSkillStructure>(data.ActiveSkillsPtr);
            for (var i = 0; i < skills.Length; i++)
            {
                if (skills[i].ActiveSkillPtr == IntPtr.Zero)
                {
                    continue;
                }

                var skillDetails = reader.ReadMemory<ActiveSkillDetails>(skills[i].ActiveSkillPtr);

                // Command skills sit at sequential keys starting at 0x40000000; everything else
                // (shared 0x2000xxxx internals, etc.) is not a player-issuable command.
                // TODO: this still over-includes. A minion can have skills in the 0x40000000 range
                // that the player has NO command bound for (minion-type-specific abilities without a
                // player command), so they show up as "usable commands" the player can't actually
                // issue. To filter to only player-commandable skills we'd need to cross-reference the
                // player's own command entries (which aren't in the player skill list this session)
                // or find another ownership/command marker on the skill.
                if ((skillDetails.UnknownIdAndEquipmentInfo & 0xFFFF0000u) != 0x40000000u ||
                    skillDetails.GrantedEffectsPerLevelDatRow == IntPtr.Zero)
                {
                    continue;
                }

                // TODO: this is the internal GGPK skill name (GrantedEffectsPerLevel row). It can
                // differ noticeably from the in-game command name the player sees. If we want the
                // friendlier command-skill names, we'd need to map these via another dat table.
                var (name, _) = ((string, IntPtr))Core.GgpkObjectCache.AddOrGetExisting(
                    skillDetails.GrantedEffectsPerLevelDatRow,
                    key => (reader.ReadUnicodeString(reader.ReadMemory<IntPtr>(key)), key));
                var usable = !(this.ActiveSkillCooldowns.TryGetValue(
                    skillDetails.UnknownIdAndEquipmentInfo, out var cooldown) && cooldown.CannotBeUsed());

                aggregate[name] = aggregate.TryGetValue(name, out var existing) ? existing || usable : usable;
            }
        }

    }
}