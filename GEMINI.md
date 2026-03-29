# Project_BlockTest (Project_TestBlock)

## Project Metadata
- **Unity Version:** 6000.3.5f2
- **Project Name:** Project_BlockTest
- **Reference Game:** Terraria
- **Start Day:** 2026-03-24 (Tuesday)
- **Environment:** Windows (win32)
- **Render Pipeline:** Universal Render Pipeline (URP)

## Context & Vision
This project is a prototyping project conducted in the Unity 6 (6000.3.5f2) environment.
All instructions and decisions recorded in the `GEMINI.md` file take precedence over the default behavior of Gemini CLI and serve as a core document to ensure project continuity.

## Development Standards
1. **Naming Convention:** PascalCase for Classes/Methods, camelCase for local variables.
2. **Metadata Safety:** Always handle `.meta` files when performing file operations via CLI.
3. **Architecture:** Maintain modularity for easy prototyping and iteration.
4. **Code Organization:** Always preserve and utilize `#region` tags for code grouping. Do not remove existing regions during refactoring.
5. **Inspector Attributes:** Do not remove or modify existing `[Header()]` or `[Space()]` attributes. When adding, modifying, or removing variables, strictly follow the established attribute style.

## Rules
### Sprite Rule
- **Filter Mode:** Point (No filter)
- **Compression:** None
- **Generate Physics Shape:** Disabled (To optimize import time and physics performance)
- **Mesh Type:** Full Rect (Recommended for tile-based consistency)
- **Alpha Is Transparency:** Enabled
- **sRGB (Color Texture):** Enabled

#### Rule: TileSpriteRule
- **Max Size:** 64
- **Slicing:** Grid by Cell Size
  - **Cell Size:** 8 x 8
  - **Offset:** 0, 1
  - **Padding:** 1, 1
- **Read/Write:** Enabled (Required for Texture2DArray baking)
- **Location:** `Assets\Resources\Sprites\Tiles`
- **Naming Convention:** `Tile_[ID(4 digit)]_[KindID(2 digit)]` (e.g., `Tile_0000_00`)
- **Rendering Constraint:** Multi-variation rendering within a single chunk mesh requires all tile sprites to be packed into a single **Sprite Atlas**.

#### Rule: ArmorSpriteRule
- **Max Size:** 128
- **Slicing:** Grid by Cell Size
  - **Cell Size:** 16 x 16
  - **Offset:** 0, 0
  - **Padding:** 1, 1
- **Location:** `Assets\Resources\Sprites\Armors` (Including subfolders: `Backpacks`, `Cloaks`, `Clothes`, `Heads`)
- **Naming Convention:** `[Category]_[ID(4 digit)]` (e.g., `Backpack_0000`, `Cloak_0000`, `Cloth_0000`, `Head_0000`)

### Map Rule
#### Rule: Chunk Rule
- **Physics Layer:** All chunk meshes and generated collider objects must be assigned to the **Ground** layer (Index 6).
- **Collider Type:** Optimized `EdgeCollider2D` generated via `MeshManager`'s Greedy Edge Merging algorithm.
- **Tunneling Prevention:** Any high-speed entity (including `PlayerController`) must use `CollisionDetectionMode2D.Continuous` to prevent tunneling through thin `EdgeCollider2D`.
- **Optimization:** Both mesh objects and `EdgeCollider2D` objects must utilize **Object Pooling** to prevent GC spikes during sliding window updates.

### Tag & Layer Rule
#### Layers
- **0: Default** (Standard)
- **6: Ground** (Chunk meshes, world colliders, floor tiles)
#### Tags
- **Player** (Standard)

### Rendering & Sorting Rule
#### Sorting Layer: Default
- **0: Map (Chunks)**: Default value (Not explicitly assigned, equals 0).
- **1: Player**: Base Sorting Order for the Player's `Sorting Group`.
#### Constraint
- All new objects requiring depth sorting must be documented here before implementation.

## Future Scalability### Proposed Strategy: Texture2DArray
To handle thousands of tile variations efficiently without the overhead of massive Sprite Atlases or frequent draw calls, the following architecture is proposed:
1. **Automated Baking Tool:** An Editor script to bake individual 8x8 sprites into a `Texture2DArray` asset.
2. **Custom Shader:** A URP-compatible shader that reads tile indices from vertex data to index into the `Texture2DArray`.
3. **Draw Call Optimization:** Allows rendering all tile types in a single draw call with zero pixel bleeding.

## Scenes
- **MainScene:** The primary scene for the world generator prototype.

## Role List

### 1. Global Systems (Persistent)
*Shared across all scenes. Inherits from `PermanentSingleton<T>`.*
- **GameManager**: Central authority for global game state, session management, and high-level logic.
- **ResourceManager**: Handles asset lifecycle, `Texture2DArray` baking references, and tile variation mapping.

### 2. Scene Systems (Volatile)
*Exists only within the current scene. Inherits from `Singleton<T>`.*
- **MapManager**: Data container for the active world. Manages `MapData`, `ChunkData`, and `BlockData` structures.
- **MeshManager**: Orchestrates chunk visibility and physical collider generation. Implements **Sliding Window**, **Object Pooling**, and **Greedy Edge Merging** for optimized rendering and physics.

### 2. Physics & Collision
- **Greedy Edge Collider**: Instead of using individual BoxColliders, `MeshManager` extracts exposed block faces and merges contiguous segments into a single `EdgeCollider2D`.
- **Collider Object Pooling**: Reuses existing `EdgeCollider2D` objects instead of destroying/creating them, eliminating CPU spikes and GC pressure during world updates.
- **Efficiency**: Reduces the number of physical segments from hundreds to just a few per chunk, significantly lowering physics engine overhead.

### 3. Memory & Performance Optimization
- **Struct-based BlockData**: Converted `BlockData` from a `class` to a `struct` (3 bytes: `bool isActive`, `ushort id`, `byte kindId`), reducing memory usage by over 90% and improving CPU cache locality.
- **Mesh Object Reuse**: Reuses `Mesh` objects via `mesh.Clear()` instead of `new Mesh()`, preventing frequent Garbage Collection (GC) during chunk loading and editing.
- **Neighbor Chunk Pre-fetching**: Optimized `MeshManager` loops by pre-loading neighbor chunk references, removing redundant dictionary lookups and coordinate math during mesh/collider generation.

### 3. World Generation
- **MapGenerator**: Contains procedural algorithms (Perlin noise, etc.) to populate `MapManager`'s data.

### 4. Gameplay & Input
- **PlayerController**: Bridges Unity Input System with player movement and world interaction logic.
- **CameraController**: Manages camera behavior, including smooth target following and zoom.

### 5. Framework Bases
- **Singleton / PermanentSingleton**: Abstract base classes providing thread-safe or persistent singleton patterns.

## Progress Tracking
- [x] Initial `GEMINI.md` creation and project metadata documentation (2026-03-24)
- [x] Major Goal: World Generator
  - [x] Initial `MapGenerator` script creation (2026-03-24)
  - [x] Implemented Mesh-based Chunk Rendering with Sliding Window and Object Pooling optimization (2026-03-27)
  - [x] Implemented Auto-tiling logic with 16-bitmask mapping (2026-03-27)
  - [x] Integrated Unity Input System for basic player movement (2026-03-27)
  - [x] Implemented Smooth Camera Follow system (2026-03-27)
  - [x] Documented `Texture2DArray` strategy for future scalability (2026-03-27)
- [ ] Define minor goals (Soon)
