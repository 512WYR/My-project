using System.IO;
using GameMake.EventUI;
using UnityEngine;

namespace GameMake.EventEditor
{
    // The supply station now uses the standard event page. The old "Apply Supply Station Layout"
    // rebuild entry was removed: it overwrote hand-configured prefab art. Existing prefabs upgrade
    // their choice column in place at runtime; this type only builds the shop during a full rebuild.
    public static class SupplyStationBuilder
    {
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/补给站UI"));

        public static void Attach(IndustrialEventPage owner)
        {
            SupplyShopBuilder.Attach(owner);
        }
    }
}
