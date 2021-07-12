﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace SlotsSlotsSlots
{
    class Program
    {
        static Lazy<Settings> _LazySettings = null!;
        static Settings Settings => _LazySettings.Value;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out _LazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "SlotsSlotsSlots.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            float baseCarryWeightMult = Settings.BaseMultiplier;
            float effectMultiplier = Settings.CarryweightEffectMultiplier;
            float potionWeights = Settings.PotionSlotUse;
            bool noHealFromWeightless = Settings.WeightlessItemsOfferNoHealing;
            int minWeaponSlots = Settings.MinimumUsedWeaponSlots;
            int maxWeaponSlots = Settings.MaximumUsedWeaponSlots;
            int minArmorslots = Settings.MinimumUsedArmorSlots;
            int maxArmorslots = Settings.MaximumUsedArmorSlots;

            state.PatchMod.Races.Set(
                state.LoadOrder.PriorityOrder.Race().WinningOverrides()
                    .Where(r => r.HasKeyword(Skyrim.Keyword.ActorTypeNPC)
                        && !r.EditorID.Equals("TestRace"))
                    .Select(r => r.DeepCopy())
                    .Do(r =>
                    {
                        r.BaseCarryWeight *= baseCarryWeightMult;
                    })
            );

            (HashSet<IFormLinkGetter<IMagicEffectGetter>> carryWeight, HashSet<IFormLinkGetter<IMagicEffectGetter>> health) magicEffects = MagicEffects(state);

            var carryWeightSpells = new HashSet<(IFormLinkGetter<ISpellGetter> Spell, int OriginalCarryWeight, int SlotAmount)>();

            foreach (var spell in state.LoadOrder.PriorityOrder.Spell().WinningOverrides())
            {
                var deepCopySpell = spell.DeepCopy();
                foreach (var e in deepCopySpell.Effects)
                {
                    if (magicEffects.carryWeight.Contains(e.BaseEffect))
                    {
                        float startingMagnitude = e.Data.Magnitude;
                        e.Data.Magnitude *= effectMultiplier;
                        carryWeightSpells.Add((spell.AsLink(),(int) startingMagnitude, (int)e.Data.Magnitude));
                        if (!(spell.Description.ToString().IsNullOrWhitespace()))
                        {
                            if ((int)e.Data.Magnitude != 1)
                            {
                                deepCopySpell.Description = deepCopySpell.Description
                                    .ToString()
                                    .Replace($"{(int)startingMagnitude}", $"{(int)e.Data.Magnitude}")
                                    .Replace($"Carry Weight", $"Slots"); 
                            }
                            else
                            {
                                deepCopySpell.Description = deepCopySpell.Description
                                    .ToString()
                                    .Replace($"{(int)startingMagnitude}", $"{(int)e.Data.Magnitude}")
                                    .Replace($"Carry Weight", $"Slot");
                            }
                        }
                        Console.WriteLine($"{spell.EditorID.ToString()} was considered a CarryWeight altering Spell and adjusted.");
                        state.PatchMod.Spells.Set(deepCopySpell);                       
                    }
                }
            };

            var carryWeightSpellFormKeys = carryWeightSpells.Select(x => x.Spell.FormKey).ToHashSet();
            
            foreach (var perk in state.LoadOrder.PriorityOrder.Perk().WinningOverrides())
            {
                foreach (var effect in perk.ContainedFormLinks)
                {
                    if(carryWeightSpellFormKeys.Contains(effect.FormKey))
                    {
                        if (!perk.Description.ToString().IsNullOrWhitespace())
                        {
                            foreach (var e in perk.Effects)
                            {
                                foreach (var fl in e.ContainedFormLinks)
                                {
                                    foreach (var carryWeightSpell in carryWeightSpells)
                                    {
                                        if (fl.FormKey.Equals(carryWeightSpell.Spell.FormKey))
                                        {
                                            Console.WriteLine($"Reached {perk.EditorID.ToString()}.");
                                            var deepCopyPerk = perk.DeepCopy();
                                            if (!carryWeightSpell.SlotAmount.Equals(1))
                                            {
                                                deepCopyPerk.Description = deepCopyPerk.Description
                                                    .ToString()
                                                    .Replace($"{carryWeightSpell.OriginalCarryWeight}", $"{carryWeightSpell.SlotAmount}")
                                                    .Replace($"Carry Weight", $"Slots");
                                            }
                                            else
                                            {
                                                deepCopyPerk.Description = deepCopyPerk.Description
                                                    .ToString()
                                                    .Replace($"{carryWeightSpell.OriginalCarryWeight}", $"{carryWeightSpell.SlotAmount}")
                                                    .Replace($"Carry Weight", $"Slot");
                                            }
                                            Console.WriteLine($"{perk.EditorID.ToString()} was considered a CarryWeight altering Perk and the description adjusted.");
                                            state.PatchMod.Perks.Set(deepCopyPerk);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            state.PatchMod.MiscItems.Set(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides()
                    .Where(m => m.Weight != 0.0f)
                    .Select(m => m.DeepCopy())
                    .Do(m => m.Weight = 0.0f));

            foreach (var ingestible in state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides())
            {
                var ingestibleCopy = ingestible.DeepCopy();
                if (ingestible.HasKeyword(Skyrim.Keyword.VendorItemPotion))
                {
                    ingestibleCopy.Weight = potionWeights;
                }
                else if (!ingestible.EditorID.Equals("dunSleepingTreeCampSap"))
                {
                    ingestibleCopy.Weight = 0.0f;
                }
                foreach (var carryWeightEffect in magicEffects.carryWeight)
                {
                    foreach (var effect in ingestibleCopy.Effects)
                    {
                        if (carryWeightEffect.Equals(effect.BaseEffect))
                        {
                            effect.Data.Magnitude *= effectMultiplier;
                        }
                    }

                }
                if (noHealFromWeightless)
                {
                    foreach (var healthEffect in magicEffects.health)
                    {
                        foreach (var e in ingestibleCopy.Effects)
                        {
                            if (healthEffect.Equals(e.BaseEffect)
                            &&
                            !(ingestible.HasKeyword(Skyrim.Keyword.VendorItemPotion)
                            || ingestible.EditorID.Equals("dunSleepingTreeCampSap")))
                            {
                                e.Data.Magnitude = 0;
                            }
                        }
                    }
                }

                state.PatchMod.Ingestibles.Set(ingestibleCopy);            
            }



            foreach (var ingredient in state.LoadOrder.PriorityOrder.Ingredient().WinningOverrides())
            {           
                var ingredientCopy = ingredient.DeepCopy();
                ingredientCopy.Weight = 0.0f;
                foreach (var carryWeightEffect in magicEffects.carryWeight)
                {
                    foreach (var effect in ingredientCopy.Effects)
                    {
                        if (carryWeightEffect.Equals(effect.BaseEffect))
                        {
                            effect.Data.Magnitude *= effectMultiplier;
                        }
                    }
                
                }
                if (noHealFromWeightless)
                {
                    foreach (var healthMagicEffect in magicEffects.health)
                    {
                        foreach (var e in ingredientCopy.Effects)
                        {
                            if (healthMagicEffect.Equals(e.BaseEffect))
                            {
                                e.Data.Magnitude = 0;
                            }
                        }
                    }
                }
                state.PatchMod.Ingredients.Set(ingredientCopy);            
            }

            foreach (var objectEffect in state.LoadOrder.PriorityOrder.ObjectEffect().WinningOverrides()) 
            {
                foreach (var carryWeightEffect in magicEffects.carryWeight)
                {
                    var objectEffectCopy = objectEffect.DeepCopy();
                    foreach (var e in objectEffectCopy.Effects)
                    {
                        if (carryWeightEffect.Equals(e.BaseEffect))
                        {
                            e.Data.Magnitude *= effectMultiplier;
                            state.PatchMod.ObjectEffects.Set(objectEffectCopy);
                        }
                    }
                }
            }

            state.PatchMod.Books.Set(
                state.LoadOrder.PriorityOrder.Book().WinningOverrides()
                    .Where(m => m.Weight != 0.0f)
                    .Select(m => m.DeepCopy())
                    .Do(m => m.Weight = 0.0f));

            state.PatchMod.Ammunitions.Set(
                state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides()
                    .Where(m => m.Weight != 0.0f)
                    .Select(m => m.DeepCopy())
                    .Do(m => m.Weight = 0.0f));

            state.PatchMod.SoulGems.Set(
                state.LoadOrder.PriorityOrder.SoulGem().WinningOverrides()
                    .Where(m => m.Weight != 0.0f)
                    .Select(m => m.DeepCopy())
                    .Do(m => m.Weight = 0.0f));

            var weapons = state.LoadOrder.PriorityOrder.Weapon().WinningOverrides();
            var weaponWeights = weapons
                                .Where(w => w.BasicStats?.Weight != 0)
                                .Select(w => w.BasicStats?.Weight ?? 0.0f);
            var weaponDistributions = MakeDistributions(weaponWeights, minWeaponSlots, maxWeaponSlots);

            foreach (var weapon in weapons)
            {
                var calculatedWeight = FindWeight(weaponDistributions, weapon.BasicStats.Weight);
                if (weapon.BasicStats.Weight == 0 || weapon.BasicStats.Weight == calculatedWeight) continue;

                var weaponCopy = weapon.DeepCopy();
                weaponCopy.BasicStats.Weight = calculatedWeight;
                state.PatchMod.Weapons.Set(weaponCopy);
            }

            var armorWithWeights = state.LoadOrder.PriorityOrder.Armor()
                                                                .WinningOverrides()
                                                                .Where(w => w.Weight != 0 && w.Weight != FindWeight(weaponDistributions, w.Weight));

            var armorDistributions = MakeDistributions(armorWithWeights.Select(w => w.Weight), minArmorslots, maxArmorslots);
            state.PatchMod.Armors.Set(
                    armorWithWeights
                    .Select(m => m.DeepCopy())
                    .Do(w =>
                    {
                        var weight = FindWeight(armorDistributions, w.Weight);
                        w.Weight = weight;
                    })
                
            );
        }

        private static float FindWeight(IEnumerable<(float MaxWeight, int Slots)> distributions, float weight)
        {
            var found = distributions.FirstOrDefault(d => d.MaxWeight >= weight);
            if (found == default) 
                found = distributions.Last();
            return found.Slots;
        }

        private static HashSet<(float MaxWeight, int Slots)> MakeDistributions(IEnumerable<float> weights, int minSlots = 1, int maxSlots = 5)
        {
            var warr = weights.ToArray();
            var deltaSlots = maxSlots - minSlots;
            var minWeight = (float)warr.Min();
            var maxWeight = (float)warr.Max();
            var deltaWeight = maxWeight - minWeight;
            var sectionSize = deltaWeight / (deltaSlots + 1);

            var output = new HashSet<(float MaxWeight, int Slots)>();
            var weight = minWeight + sectionSize;
            for (var slots = minSlots; slots <= maxSlots; slots += 1)
            {
                output.Add((weight, slots));
                weight += sectionSize;
            }

            return output;
        }

        private static (HashSet<IFormLinkGetter<IMagicEffectGetter>>, HashSet<IFormLinkGetter<IMagicEffectGetter>>) MagicEffects(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var foundCarryWeight = new HashSet<IFormLinkGetter<IMagicEffectGetter>>();
            var foundHealth = new HashSet<IFormLinkGetter<IMagicEffectGetter>>();
            foreach (var e in state.LoadOrder.PriorityOrder.MagicEffect().WinningOverrides())
            {
                if (e.Archetype.ActorValue.Equals(ActorValue.CarryWeight)
                    //&& !e.TargetType.Equals(TargetType.Aimed)
                    //&& !e.TargetType.Equals(TargetType.Touch)
                    )
                {
                    foundCarryWeight.Add(e.AsLink());
                }
                if (e.Archetype.ActorValue.Equals(ActorValue.Health)
                    && !e.Flags.HasFlag(MagicEffect.Flag.Hostile)
                    && !e.Description.String.IsNullOrWhitespace())
                {
                    foundHealth.Add(e.AsLink());
                }
            }
            return (foundCarryWeight, foundHealth);
        }
    }
}
