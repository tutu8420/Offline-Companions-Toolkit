using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChineseCanFly.OfflineCompanionsToolkit
{
    /// <summary>
    /// Picks up allowed items lying on the ground around a companion.
    ///
    /// The mod's own auto-pickup skips anything with m_autoPickup == false, and Valheim sets
    /// exactly that flag on every item a character drops (ItemDrop.OnPlayerDrop), so meat you
    /// throw down for the wolves is invisible to companions. This sweep looks at what is actually
    /// lying there and calls Humanoid.Pickup, which still goes through the whitelist.
    /// </summary>
    internal static class GroundSweep
    {
        private static readonly Collider[] Buffer = new Collider[64];
        private static int _mask = -1;
        private static float _next;
        private static float _nextReport;
        private static int _pickedSinceReport;

        internal static void Tick()
        {
            if (CompanionToolkitPlugin.SweepEnabled == null || !CompanionToolkitPlugin.SweepEnabled.Value) return;
            if (Time.time < _next) return;
            _next = Time.time + Mathf.Max(0.5f, CompanionToolkitPlugin.SweepInterval.Value);

            try
            {
                var chars = Character.GetAllCharacters();
                if (chars == null) return;

                float radius = Mathf.Clamp(CompanionToolkitPlugin.SweepRadius.Value, 1f, 20f);
                int budget = Mathf.Max(1, CompanionToolkitPlugin.SweepPerPass.Value);
                int mask = Mask();

                for (int i = 0; i < chars.Count; i++)
                {
                    var hum = chars[i] as Humanoid;
                    if (hum == null || hum.IsDead() || !FilterPatches.IsCompanion(hum)) continue;

                    Vector3 center = hum.transform.position + Vector3.up * 1.2f;
                    int count = Physics.OverlapSphereNonAlloc(center, radius, Buffer, mask, QueryTriggerInteraction.Ignore);

                    int picked = 0;
                    for (int c = 0; c < count && picked < budget; c++)
                    {
                        var col = Buffer[c];
                        if (col == null || col.attachedRigidbody == null) continue;
                        var drop = col.attachedRigidbody.GetComponent<ItemDrop>();
                        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;

                        string name = NameOf(drop);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!FilterPatches.Allowed(name)) continue;

                        try
                        {
                            if (drop.InTar()) continue;
                        }
                        catch (Exception) { }

                        if (!drop.CanPickup(false))
                        {
                            drop.RequestOwn();
                            continue;
                        }

                        drop.Load();
                        if (hum.Pickup(drop.gameObject, false, false))
                        {
                            picked++;
                            _pickedSinceReport++;
                            if (CompanionToolkitPlugin.Verbose.Value)
                                CompanionToolkitPlugin.Log.LogInfo("[sweep] picked up " + name);
                        }
                    }
                }

                if (_pickedSinceReport > 0 && Time.time >= _nextReport)
                {
                    _nextReport = Time.time + 60f;
                    if (CompanionToolkitPlugin.Verbose.Value)
                        CompanionToolkitPlugin.Log.LogInfo($"[sweep] {_pickedSinceReport} items picked up in the last minute");
                    _pickedSinceReport = 0;
                }
            }
            catch (Exception ex)
            {
                CompanionToolkitPlugin.Log.LogWarning("[sweep] " + ex.Message);
            }
        }

        private static int Mask()
        {
            if (_mask >= 0) return _mask;
            try { _mask = LayerMask.GetMask("item"); }
            catch (Exception) { _mask = 0; }
            if (_mask == 0) _mask = Physics.DefaultRaycastLayers;
            return _mask;
        }

        private static string NameOf(ItemDrop drop)
        {
            var prefab = drop.m_itemData.m_dropPrefab;
            if (prefab != null) return prefab.name;
            return drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
        }
    }
}
