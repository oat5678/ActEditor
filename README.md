# ActEditor with PixelLab & Batch Indexed8 Extension

An `.act` and `.spr` file editor for Ragnarok Online (built upon the TokeiLibrary ActEditor project), designed to help server developers and creators seamlessly import character animations from **PixelLab** and efficiently handle file format conversions.

---

## ✨ Key Features

1. **🚀 Automated PixelLab JSON Import**
   - Directly import animations by state, such as `IDLE`, `WALKING`, `ATTACK`, `HIT`, and `DEAD`.
   - Automatically slices the sprite sheet (cell sizing) and maps directions into the corresponding action indexes. **(Supports both standard 8-direction and paired 4-direction formats)**.
   - Supports varying frame counts per direction (e.g., 4 frames for IDLE or 8 frames for WALKING).

2. **⚡ Batch Indexed8 Conversion**
   - Eliminates the tedious task of converting images frame-by-frame using the **"Convert All Sprites to Indexed8"** feature.
   - Manages the color palette and links all layer data in a single click, bypassing annoying Format Conflict pop-up windows.

3. **🎯 Global Sprite Shifting (D-Pad Controls)**
   - Easily reposition all sprites across *every* frame and action simultaneously.
   - Use the new directional D-Pad (Red arrows) and Offset textbox on the top toolbar to shift X/Y coordinates in bulk.
   - Fully integrated with the Undo/Redo system for safe editing.

4. **🛡️ Game-Ready Safe Saving**
   - Fixes the corrupted `.spr` file bug (1 KB size) commonly encountered with custom script additions.
   - The saved `.act` and `.spr` file structures fully comply with the rAthena standard, allowing them to run smoothly on the actual client without crashing (`renderer.cpp`).

---

## 🛠️ How to Use

### 1. Importing Animations from PixelLab (Import JSON)
1. Open ActEditor and create a new file via `File > New Act > Monster template`.
2. Go to the top menu **Import PixelLab JSON**, choose your layout mode (**8 Directions** or **4 Directions**), and select the desired state (e.g., `1 - WALKING`).
3. Select the exported `.json` file from PixelLab; the system will automatically slice the images and arrange the frames into actions.

### 2. Bulk Shifting Sprite Positions
1. Look for the **Offset** textbox on the top toolbar (next to the Undo/Redo buttons) and enter your desired movement distance (e.g., `50`).
2. Click the Red Arrow buttons (↑, ↓, ←, →) to shift the position of all sprites across the entire project at once.
3. If you make a mistake, simply click the **Undo** button to revert the position safely.

### 3. Converting All Sprites to Indexed8 (For servers requiring indexed palettes)
1. After successfully importing and positioning the images, navigate to **Import PixelLab JSON > Convert All Sprites to Indexed8**.
2. Configure the palette merging settings (Format conflict) as needed and click OK. The system will convert all images in the project to Indexed8 in one go.
3. Click `File > Save` to save the `.act` and `.spr` files, ready for use in the game.

---

## 💻 System Requirements
- Windows 10 / 11
- .NET Framework 4.8
- Visual Studio 2019 or newer (for users who wish to build the solution from source)

---

## 🙏 Acknowledgements & Credits

- Special thanks to **[Tokeiburu](https://github.com/Tokeiburu)** for creating the original **ActEditor** project, which made this extension and customization possible.