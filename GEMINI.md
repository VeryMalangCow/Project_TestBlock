# Project_BlockTest (Project_TestBlock)

## 1. Project Metadata
- **Unity Version:** 6000.3.5f2
- **Project Name:** Project_BlockTest
- **Reference Game:** Terraria
- **Start Day:** 2026-03-24 (Tuesday)
- **Environment:** Windows (win32)
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Networking:** Netcode for GameObjects (NGO)

----------

## 2. Context & Vision
This project is a prototyping project conducted in the Unity 6 (6000.3.5f2) environment.
The project is transitioning into a **Multiplayer Game** using a Client-Server architecture.
All instructions and decisions recorded in the `GEMINI.md` file take precedence over the default behavior of Gemini CLI and serve as a core document to ensure project continuity.

----------

## 3. Multiplayer Architecture (NGO)
### Core Principles
- **Authority:** Server-Authoritative with Client-Side Prediction for smooth movement.
- **Player Synchronization:**
  - **Movement:** Horizontal movement, Jump, and Dash must be synchronized across all clients.
  - **Interaction:** Block destruction and placement are handled via Server RPCs to ensure world consistency.
- **Data Management:**
  - **World Data:** Server holds the master `MapData`. Clients receive chunk updates based on proximity.
  - **Individual Inventories:** Each player maintains a private, persistent inventory state synchronized from the server.

----------

## 4. Development Standards
1. **Naming Convention:** PascalCase for Classes/Methods, camelCase for local variables.
2. **Metadata Safety:** Always handle `.meta` files when performing file operations via CLI.
3. **Architecture:** Maintain modularity for easy prototyping and iteration.
4. **Code Organization:** Always preserve and utilize `#region` tags for code grouping. Do not remove existing regions during refactoring.
5. **Inspector Attributes:** Do not remove or modify existing `[Header()]` or `[Space()]` attributes. When adding, modifying, or removing variables, strictly follow the established attribute style.

----------

## 5. Sprite & Rendering Rules
### General Sprite Rules
- **Filter Mode:** Point (No filter)
- **Compression:** None
- **Generate Physics Shape:** Disabled (To optimize import time and physics performance)
- **Mesh Type:** Full Rect (Recommended for tile-based consistency)
- **Alpha Is Transparency:** Enabled
- **sRGB (Color Texture):** Enabled

### Rule: TileSpriteRule
- **Max Size:** 128
- **Slicing:** Grid by Cell Size (8x8, Offset 0x0, Padding 1x1)
- **Read/Write:** Enabled (Required for Texture2DArray baking)
- **Naming Convention:** `Tile_[ID(4 digit)]_[Idx(3 digit)]` (e.g., `Tile_0000_000`)
- **Processing:** `TileSpriteProcessor` tool automatically slices sprites, skipping empty regions while maintaining grid-based indices.

----------

## 6. Map & Chunk Rules
- **Chunk Size:** 8 x 8 blocks.
- **World Sizes (Presets):**
  - **Standard**: 300 x 240 Chunks (2400 x 1920 Blocks)
  - **Great Cave**: 400 x 200 Chunks (3200 x 1600 Blocks)
  - **Hell**: 240 x 400 Chunks (1920 x 3200 Blocks)

### Rule: Chunk Physics & Optimization
- **Physics Layer:** All chunk meshes and generated collider objects must be assigned to the **Ground** layer (Index 6).
- **Collider Type:** Optimized `EdgeCollider2D` generated via `MeshManager`'s Greedy Edge Merging algorithm.
- **Tunneling Prevention:** Any high-speed entity (including `PlayerController`) must use `CollisionDetectionMode2D.Continuous` to prevent tunneling.
- **Optimization:** Both mesh objects and `EdgeCollider2D` objects must utilize **Object Pooling**.

----------

## 7. Tag & Layer Rules
### Layers
- **0: Default** (Standard)
- **6: Ground** (Chunk meshes, world colliders, floor tiles)

### Tags
- **Player** (Standard)

### Sorting Layers (Default)
- **0: Map (Chunks)**: Default value (Not explicitly assigned, equals 0).
- **1: Player**: Base Sorting Order for the Player's `Sorting Group`.

----------

## 8. Technical Architecture
### Advanced Autotiling (47 Rules System)
To achieve Terraria-style block connections, an 8-direction bitmask system is implemented.
- **Neighbor Check (Phone Keypad Layout):**
  ```
  1 2 3  (TL, U, TR)
  4 5 6  (L,  C, R)
  7 8 9  (BL, D, BR)
  ```
- **Bitmask Logic (256 combinations):**
  - **Orthogonal (Exist):** Check if neighbors at 2, 4, 6, 8 are active.
  - **Diagonal (Missing):** Check if neighbors at 1, 3, 7, 9 are *missing*, but only if their two adjacent orthogonal neighbors exist.
- **Rule Mapping:** 256 combinations are mapped to 47 unique RuleIDs via `Rule_TileIndex.csv`.

### Texture2DArray Rendering
- **Structure:** Each tile ID occupies **141 layers** (47 Rules * 3 Random Variations).
- **Indexing Formula:** `Index = (TileID * 141) + (RuleID * 3) + VariationIdx`.
- **Optimization:** `MeshManager` caches neighbor states in a local 18x18 array per chunk to minimize dictionary lookups.

----------

## 9. Role List (Systems)
### Global Systems (Persistent)
- **GameManager**: Central authority for global game state, session management, and high-level logic.
- **ResourceManager**: Handles asset lifecycle, sprite caching, `Texture2DArray` references, and 8-direction rule mapping.

### Scene Systems (Volatile)
- **MapManager**: Data container for the active world. Manages `MapData`, `ChunkData`, and `BlockData` structures.
- **MeshManager**: Orchestrates chunk visibility and physical collider generation. Implements **Sliding Window**, **Object Pooling**, and **Greedy Edge Merging**.

----------

## 10. Editor Tools
### TileSpriteProcessor
- **Path:** `Tools > Project_BlockTest > Process All Tile Sprites`
- **Features:** Enforces `TileSpriteRule`, **Smart Slicing** (skips empty regions), and sets Max Size (128).

### TextureArrayBaker
- **Path:** `Tools > Project_BlockTest > Bake Tileset TextureArray`
- **Features:** Bakes sliced sprites into `Texture2DArray` using **Numeric Sorting** to ensure correct RuleID mapping.

----------

## 11. Progress Tracking
- [x] Initial `GEMINI.md` creation and project metadata documentation (2026-03-24)
- [x] Foundation: World Interaction & Visuals
  - [x] Mesh-based Chunk Rendering with Sliding Window & Object Pooling (2026-03-27)
  - [x] Terraria-style Paper Doll Visual System (2026-03-30)
  - [x] Basic Block Placement & Destruction (2026-03-30)
  - [x] **Advanced 8-direction Autotiling (47 Rules)** (2026-03-31)
  - [x] **Texture2DArray Optimization (141 Layers system)** (2026-03-31)

- [ ] **Major Goal 1: Procedural World Generation**
  - [ ] Seed-based Perlin Noise terrain generation (Height map).
  - [ ] Biome constants (Dirt, Stone, Cave) and ore distribution logic.
  - [ ] World Save/Load system (Binary or JSON).

- [ ] **Major Goal 2: Multiplayer Core (NGO)**
  - [x] NGO Package integration and NetworkManager setup. (2026-04-01)
  - [ ] Player Prefab conversion to NetworkObject.
  - [ ] **Sync Player Actions**: Move, Jump, Dash (Client-side prediction).
  - [ ] **Server-Authoritative Tile Interaction**: RPC-based SetBlock.
  - [ ] Chunk synchronization via Interest Management (Sliding Window).

- [ ] **Major Goal 3: Inventory & Item System**
  - [ ] Item Data Structure (ScriptableObject) & Database.
  - [ ] Block Looting: Dropped items when blocks are destroyed.
  - [ ] Basic Inventory UI (Grid system) and Hotbar interaction.
  - [ ] **Individual Player Inventory**: Separate state for each networked user.
  - [ ] **Player Data Save/Load system**: Persist player position, health, and inventory state.

- [ ] **Major Goal 4: Advanced Player Physics & Movement**
  - [x] Smooth Horizontal Movement (Completed)
  - [x] Box-based Ground Detection & Jump (Completed)
  - [ ] **Player Dash**: Fast horizontal burst using sprite index 11.
  - [ ] Coyote Time & Jump Buffering for better platforming feel.
