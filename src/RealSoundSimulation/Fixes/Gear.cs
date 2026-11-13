using System;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using RealSoundSimulation.Diagnostics;

namespace RealSoundSimulation.Fixes
{
    // What you wear on your head, for every feature that asks. Same lookup as the game's Player.UpdatePhonesReally, which picks
    // the headset sound: the ear slot first, then a headset built into the helmet. So "headset on" here is always the headset the
    // game is playing through. A helmet's own "deaf" sound without a headset is not a headset (no ear protection for the cut).
    internal static class Gear
    {
        internal static bool WearsHeadset(Player player)
        {
            if (player == null) return false;
            try
            {
                if (player.Equipment.GetSlot(EquipmentSlot.Earpiece).ContainedItem is Headphones) return true;
                if (player.Equipment.GetSlot(EquipmentSlot.Headwear).ContainedItem is CompoundItem helmet)
                    return helmet.GetAllItemsFromCollection().OfType<Headphones>().Any();
            }
            catch (Exception e) { DiagRunner.ReportOnce("headset check", e); }
            return false;
        }
    }
}
