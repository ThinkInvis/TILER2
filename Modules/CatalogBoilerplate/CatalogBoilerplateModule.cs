﻿using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal class CatalogBoilerplateModule : T2Module<CatalogBoilerplateModule> {
        public override bool managedEnable => false;

        internal static readonly FilingDictionary<CatalogBoilerplate> allInstances = new FilingDictionary<CatalogBoilerplate>();

        public override void SetupConfig() {
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
            ItemDropAPI.ChestItems.NormalEquipment += CatalogBoilerplateNonLunarEquipmentListProvider;
            ItemDropAPI.ChestItems.LunarEquipment += CatalogBoilerplateLunarEquipmentListProvider;
        }

        private IEnumerable<PickupIndex> CatalogBoilerplateLunarEquipmentListProvider(IEnumerable<PickupIndex> input) {
            return input.Except(CollectEquipment(false, true)).Concat(CollectEquipment(true, true));
        }

        private IEnumerable<PickupIndex> CatalogBoilerplateNonLunarEquipmentListProvider(IEnumerable<PickupIndex> input) {
            return input.Except(CollectEquipment(false, false)).Concat(CollectEquipment(true, false));
        }

        private IEnumerable<PickupIndex> CollectEquipment(bool mustBeEnabled, bool mustBeLunar) {
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Equipment equipment && (equipment.enabled == mustBeEnabled) && (equipment.isLunar == mustBeLunar))
                    yield return equipment.pickupIndex;
            }
        }

        private IEnumerable<ItemIndex> CollectItemsOfTier(ItemTier tier, bool mustBeEnabled) {
            foreach(CatalogBoilerplate bpl in allInstances) {
                if(bpl is Item item && item.itemTier == tier && (mustBeEnabled == item.enabled) && (mustBeEnabled ? item.itemDef.DoesNotContainTag(ItemTag.WorldUnique) : true))
                    yield return item.catalogIndex;
            }
        }

        readonly ItemTier[] TIERS_TO_SCAN = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Lunar, ItemTier.Boss };

        private void On_RunBuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self) {
            foreach(var tier in TIERS_TO_SCAN) {
                ItemDropAPI.AddItemByTier(tier, CollectItemsOfTier(tier, true).ToArray());
                ItemDropAPI.RemoveItemByTier(tier, CollectItemsOfTier(tier, false).ToArray());
            }
            orig(self);
            //should force-update most cached drop tables
            PickupDropTable.RegenerateAll(Run.instance);
            //update existing Command droplets. part of an effort to disable items mid-stage, may not be necessary while that's prevented
            //may be causing issues with command droplet selections as of Anniversary Update
            /*foreach(var picker in UnityEngine.Object.FindObjectsOfType<PickupPickerController>()) {
                picker.SetOptionsFromPickupForCommandArtifact(picker.options[0].pickupIndex);
            }*/
        }

        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            foreach(CatalogBoilerplate bpl in allInstances) {
                PickupIndex pind;
                if(bpl is Equipment) pind = PickupCatalog.FindPickupIndex(((Equipment)bpl).catalogIndex);
                else if(bpl is Item) pind = PickupCatalog.FindPickupIndex(((Item)bpl).catalogIndex);
                else continue;
                var pickup = PickupCatalog.GetPickupDef(pind);

                bpl.pickupDef = pickup;
                bpl.pickupIndex = pind;
            }
        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig) {
            var retv = orig();
            var bplsLeft = allInstances.ToList();
            foreach(var entry in retv) {
                if(!(entry.extraData is PickupIndex)) continue;
                CatalogBoilerplate matchedBpl = null;
                foreach(CatalogBoilerplate bpl in bplsLeft) {
                    if((PickupIndex)entry.extraData == bpl.pickupIndex) {
                        matchedBpl = bpl;
                        break;
                    }
                }
                if(matchedBpl != null) {
                    matchedBpl.logbookEntry = entry;
                    bplsLeft.Remove(matchedBpl);
                }
            }
            return retv;
        }
    }
}
