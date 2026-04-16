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
- **Connection Method (Relay):** Uses **Unity Relay Service** to bypass firewalls and complex network environments (e.g., School/Office networks).
- **Access Control:** Connection is established via a **6-digit Join Code** instead of direct IP addresses.
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
- **Max Size:** 256
- **Slicing:** Grid by Cell Size (16x16, Offset 0x0, Padding 1x1)
- **Read/Write:** Enabled (Required for Texture2DArray baking)
- **Naming Convention:** `Tile_[ID(4 digit)]_[Idx(3 digit)]` (e.g., `Tile_0000_000`)
- **Processing:** `TileSpriteProcessor` tool automatically slices sprites, skipping empty regions while maintaining grid-based indices.

----------

## 6. Map & Chunk Rules
- **Chunk Size:** 16 x 16 blocks. (Source of Truth: `ChunkData.Size`)
- **World Sizes (Presets):**
  - **Standard**: 150 x 120 Chunks (2400 x 1920 Blocks)
  - **Great Cave**: 200 x 100 Chunks (3200 x 1600 Blocks)
  - **Hell**: 120 x 200 Chunks (1920 x 3200 Blocks)

### Rule: Chunk Physics & Optimization
- **Physics Layer:** All chunk meshes and generated collider objects must be assigned to the **Ground** layer (Index 6).
- **Collider Type:** Optimized `EdgeCollider2D` generated via `MeshManager`'s Greedy Edge Merging algorithm.
- **Tunneling Prevention:** Any high-speed entity (including `PlayerController`) must use `CollisionDetectionMode2D.Continuous` to prevent tunneling.
- **Optimization:** Both mesh objects and `EdgeCollider2D` objects must utilize **Object Pooling**.
- **Sliding Window:** Supports asymmetrical view distances (`viewDistanceX`, `viewDistanceY`) to optimize for widescreen environments.

----------

## 7. Tag & Layer Rules
### Layers
- **0: Default** (Standard)
- **6: Ground** (Chunk meshes, world colliders, floor tiles)
- **7: Player** (Character body and collision)
- **8: Item** (Dropped items in the world)

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
- **Rule Mapping:** 256 combinations are mapped to 47 unique RuleIDs via `Rule_TileIndex.CSV` (Now loaded via Addressables).

### Addressables & ScriptableObject System
- **Data Management:** All item data is managed via `ItemData` SO and `ItemDatabase` SO.
- **Zero-Latency Visuals:** Implemented a central cache in `ItemDataManager` and `ResourceManager` using `WaitForCompletion()` to eliminate Addressable's 1-frame delay.
- **Resource Optimization:** Minimized `Resources` folder usage (only `UnityPlayerAccountSettings` remains) to optimize build size and initial loading time.
- **Character Visuals:** Dynamically loaded sliced sprites for bodies and armors via Addressables with automatic name-based sorting (12 frames).

### Texture2DArray Rendering
- **Structure:** Each tile ID occupies **141 layers** (47 Rules * 3 Random Variations).
- **Indexing Formula:** `Index = (TileID * 141) + (RuleID * 3) + VariationIdx`.
- **Optimization:** `MeshManager` caches neighbor states in a local 18x18 array per chunk to minimize dictionary lookups.
- **Loading:** `TilesetArray.asset` is managed via Addressables for memory efficiency.

----------

## 9. Role List (Systems)
### Global Systems (Persistent)
- **GameManager**: Central authority for global game state, session management, and high-level logic.
- **ResourceManager**: Manages Addressable character visuals, tileset arrays, and auto-tiling rules with a high-performance cache.
- **ItemDataManager**: Handles ScriptableObject-based item databases and provides instant sprite access via Addressable sync-loading.
- **NetworkObjectPoolManager**: High-performance multiplayer pooling using `INetworkPrefabInstanceHandler` to minimize Instantiate/Destroy overhead.

### Scene Systems (Volatile)
- **MapManager**: Data container for the active world. Manages `MapData`, `ChunkData`, and `BlockData` structures.
- **MeshManager**: Orchestrates chunk visibility and physical collider generation. Implements **Sliding Window**, **Object Pooling**, and **Greedy Edge Merging**.

----------

## 10. Editor Tools
### Converter Tools
#### Item CSV to SO
- **Path:** `Tools > Project > Converter > Item CSV to SO`
- **Features:** Converts `ItemDatabase.csv` to `ItemData` SOs, auto-registers to Addressables, and links icons.

#### Character Visuals to Addressable
- **Path:** `Tools > Project > Converter > Character Visuals to Addressable`
- **Features:** Automatically scans `Bodies` and `Armors` folders, assigns standardized addresses (e.g., `Body_Head_000`), and manages Addressable groups.

### Texture2D Baker
#### Tile Texture2DArray
- **Path:** `Tools > Project > Texture2D Baker > Tile Texture2DArray`
- **Features:** Bakes sliced tile sprites into a `Texture2DArray` (16x16 resolution) and auto-registers it to Addressables as 'TilesetArray'.

### Sprite Processor
#### Tile
- **Path:** `Tools > Project > Sprite Processor > Tile`
- **Features:** Enforces `TileSpriteRule` (16x16 Grid, Max Size 256), **Smart Slicing** (skips empty regions), and Pivot (8, 8).

----------

## 11. Progress Tracking
- [x] Initial `GEMINI.md` creation and project metadata documentation (2026-03-24)
- [x] Foundation: World Interaction & Visuals (2026-03-31)
- [x] **Major Goal 2: Multiplayer Core (NGO & Relay)** (2026-04-02)

- [ ] **Major Goal 3: Inventory & Item System**
  - [x] **Advanced Interaction System**: Integrated `InputSystem_Actions` for complex UI behavior. (2026-04-15)
  - [x] **Item Dropping & Pickup**: Server-authoritative item system. (2026-04-15)
  - [x] **SO & Addressable Database**: Complete transition from CSV to SO/Addressables. (2026-04-16)
  - [x] **Instant UI Feedback**: Synchronous Addressable loading with central caching. (2026-04-16)
  - [ ] Block Looting: Dropped items when blocks are destroyed.
  - [ ] Basic Inventory UI (Grid system) and Hotbar interaction.
  - [ ] **Individual Player Inventory**: Separate state for each networked user.
  - [ ] **Inventory Delta Optimization**: Optimize sync by sending only changed slots.

- [ ] **Major Goal 4: Advanced Player Physics & Movement**
  - [x] Smooth Horizontal Movement & Ground Detection.
  - [x] **Terraria-style Terrain Following**: Step-up and Slope handling. (2026-04-06)
  - [x] **Physics Stabilization**: FixedUpdate-based server physics for items. (2026-04-16)
  - [x] **Addressable Character Visuals**: High-performance body/armor loading with caching. (2026-04-16)
  - [ ] Coyote Time & Jump Buffering for better platforming feel.

----------

## 12. Multiplayer Development Rules (CRITICAL)
### 1. Authority & Movement
- **Movement:** All player movement MUST use `ClientNetworkTransform`. The Owner has authority over their position to ensure zero latency feel.
- **Physics:** Physics calculations (Gravity, Velocity) should only run on the `IsOwner` side to prevent jitter and redundant calculations.

### 2. State Synchronization
- **Events (Jump):** Use incrementing `int` counters (e.g., `jumpCountSync`) instead of `bool` for discrete events to ensure they trigger exactly once across the network.
- **Continuous State (Dash, Armor):** Use `NetworkVariable` for states that persist. Always bind visual updates to `OnValueChanged` to handle "late-joiners" automatically.
- **RPCs:** Use `ServerRpc` for "Requests" (Interactions, Block Update) and `ClientRpc` for "Broadcasts" or "Direct Deliveries" (Map Data).

### 3. Map & Data Consistency
- **Server is Truth:** The Server (Host) holds the master `MapData`. Clients must NEVER generate their own terrain; they must request it from the server.
- **Verification:** Any `ServerRpc` that modifies the world MUST include distance and permission checks on the server side to prevent cheating or invalid operations.

### 4. Performance & GC
- **Object Pooling**: Mandatory for items and projectiles via `NetworkObjectPoolManager`.
- **Collider Layer Overrides**: Use `includeLayers` and `excludeLayers` to surgically isolate physics interactions (Map vs Player/Item only).
- **Throttling**: CPU-intensive tasks like `OverlapCircle` must use a `searchInterval` (e.g., 0.2s) instead of running every frame.

----------

## Next Actions (Todo)
### 1. Multiplayer Validation (Cross-PC)
- **Goal:** Perform a real-world connection test between two different PCs using the **6-digit Join Code**.
- **Checklist:**
  - [ ] Verify if the Join Code is generated correctly on the Host.
  - [ ] Confirm if the Client can join using the code without firewall issues.
  - [ ] Ensure player movement and world data (chunks) are synchronized.

### 2. Item Database & Mining
- **Goal:** Implement the logic to drop specific items when blocks are mined.
- **Detail:** Connect `MapManager.SetBlock` to `ItemController` spawning.

### 3. Inventory Delta Optimization
- **Goal:** Optimize inventory synchronization by sending only changed slots instead of the entire array.
