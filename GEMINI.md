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

## Rules
### Sprite Rule
#### Rule: TileSpriteRule
- **Filter Mode:** Point (No filter)
- **Max Size:** 64
- **Compression:** None
- **Slicing:** Grid by Cell Size
  - **Cell Size:** 8 x 8
  - **Offset:** 0, 1
  - **Padding:** 1, 1
- **Location:** `Assets\Resources\Sprites\Tiles`
- **Naming Convention:** `Tile_ID(4 digit: Tile Id)_ID(2 digit: kind of tile Id)` (e.g., `Tile_0000_00`)
- **Rendering Constraint:** Multi-variation rendering within a single chunk mesh requires all tile sprites to be packed into a single **Sprite Atlas**.

## Future Scalability
### Proposed Strategy: Texture2DArray
To handle thousands of tile variations efficiently without the overhead of massive Sprite Atlases or frequent draw calls, the following architecture is proposed:
1. **Automated Baking Tool:** An Editor script to bake individual 8x8 sprites into a `Texture2DArray` asset.
2. **Custom Shader:** A URP-compatible shader that reads tile indices from vertex data to index into the `Texture2DArray`.
3. **Draw Call Optimization:** Allows rendering all tile types in a single draw call with zero pixel bleeding.

## Scenes
- **MainScene:** The primary scene for the world generator prototype.

## Role List
- **GameManager**: Persistent manager for global game state and logic.
- **ResourceManager**: Persistent manager for resource loading and access. Handles tile sprite sets and variations.
- **MapManager**: Scene-specific manager for map-related logic. Contains MapData, ChunkData, and BlockData.
- **RenderManager**: Scene-specific manager for mesh generation. Implements Sliding Window and Object Pooling for optimized chunk rendering.
- **CameraController**: Camera script for smooth following of the target (e.g., Player).
- **Singleton / PermanentSingleton**: Base classes for providing Singleton patterns (Scene-specific / Persistent).
- **MapGenerator**: Handles the procedural generation of map and background data.
- **PlayerController**: Handles player inputs and movement logic using the Unity Input System.

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
