// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.HomeAssistant.Models;

namespace FateTakesYouHome.ViewModels;

/// <summary>
/// The live tiles on one surface, by entity, so a state change reaches every tile showing it.
/// </summary>
/// <remarks>
/// One entity can appear on a surface more than once: pinned and in the layout, or as both a tile
/// and a quick action. Keeping one tile per entity is what left layout widgets frozen at the state
/// they were built with.
/// </remarks>
public sealed class EntityTileIndex
{
    private readonly Dictionary<string, List<EntityTileViewModel>> _byEntityId =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(EntityTileViewModel tile)
    {
        if (!_byEntityId.TryGetValue(tile.EntityId, out List<EntityTileViewModel>? tiles))
        {
            tiles = [];
            _byEntityId[tile.EntityId] = tiles;
        }

        tiles.Add(tile);
    }

    public bool Contains(string entityId) => _byEntityId.ContainsKey(entityId);

    /// <summary>Feeds <paramref name="state"/> to every tile showing the entity.</summary>
    public void Update(string entityId, HaEntityState state)
    {
        if (_byEntityId.TryGetValue(entityId, out List<EntityTileViewModel>? tiles))
        {
            foreach (EntityTileViewModel tile in tiles)
            {
                tile.Update(state);
            }
        }
    }

    public void Clear() => _byEntityId.Clear();
}
