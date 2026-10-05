# Epic Loot Storage

Bulk storage for [Epic Loot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/)'s crafting materials. **Requires Epic Loot.** Five buildable stores, one per material family, each built from vanilla Valheim parts so it fits in any base.

| Store | Holds | Built from |
|---|---|---|
| **Essence Keg** | Essence | A small fermenter, with Essence bottles at its foot |
| **Dust Sack** | Dust | A big flour sack, with Dust pouches beside it |
| **Reagent Barrel** | Reagent | A sealed barrel, with Reagent jugs at its base |
| **Shard Bucket** | Shards | A forge cooling bucket, heaped over the rim with crystals |
| **Runestone Chest** | Runestones | A small open black metal chest, runestones standing inside |

## How it works

- **One kind per store.** Each store takes only its own family, then locks to the first rarity you put in. A Dust Sack holding Rare Dust takes only Rare Dust until you empty it.
- **See what's inside at a glance.** A filled store wears a sticker of the item it holds, and its props take on that rarity's colour. The Dust Sack takes the colour too. Empty stores go back to their plain look.
- **Hover for details.** Looking at a store shows what it holds and how many, for example *Rare Dust x340*.
- **Nothing gets lost.** Items a store won't accept stay where they were, whether you drag them, swap them or use "place stacks".
- **Rarity colours follow Epic Loot.** If you've recoloured rarities in Epic Loot's config, the stores use your colours.

All five are under **Hammer → Furniture** and need a **Workbench**.

| Store | Cost |
|---|---|
| Essence Keg | 10 Fine Wood, 2 Bronze |
| Dust Sack | 6 Leather Scraps, 4 Wood |
| Reagent Barrel | 10 Wood, 4 Resin |
| Shard Bucket | 10 Wood, 2 Copper |
| Runestone Chest | 10 Wood, 4 Stone, 2 Flint |

## Configuration

`BepInEx/config/com.hippotech.epiclootstorage.cfg` has one section per store, with:

- **Columns**: 1 to 8 (default 5)
- **Rows**: 1 to 20 (default 4)

Changes apply to stores already built. A store never shrinks so far that it hides items already inside. On a server, the server's values are used for everyone.

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
- **[Epic Loot](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) (required).** These stores hold Epic Loot's materials, so you need Epic Loot installed. Mod managers install it for you.

Everyone on a server needs this mod, including the server itself.

## Installation

Use a mod manager such as r2modman, or copy `EpicLootStorage.dll` into `BepInEx/plugins`.

## Source and issues

https://github.com/Hippoflotomas/EpicLootStorage
