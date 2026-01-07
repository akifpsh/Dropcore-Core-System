# Dropcore Multiplayer Core Architecture

This repository showcases the core architectural systems developed for **Dropcore**, a fast-paced multiplayer action game built with **Unity** and **Photon PUN 2**. 

The focus of this project is to demonstrate scalable C# patterns, network optimization, and decoupled gameplay systems.

## 🛠 Technical Highlights

### 1. Advanced Networking & Synchronization
* **Custom Interpolation/Extrapolation:** Implemented a manual synchronization layer (`PlayerNetworkSync.cs`) to handle high-latency scenarios, bypassing default Photon components for smoother movement.
* **Byte-Level Event System:** Developed `NetworkEventManager.cs` to handle game events via Byte codes instead of strings, significantly reducing network overhead and bandwidth usage.

### 2. Scalable Game Mode Architecture
* **Strategy Pattern:** Utilized `IGameMode` interface and `ModeManager` to allow hot-swapping of game rules (Deathmatch, Team Battle, etc.) without modifying core game logic.
* **Data-Driven Design:** Leveraged **ScriptableObjects** (`ModeData.cs`) to allow designers to tune game parameters and modes directly from the Unity Editor.

### 3. Performance Optimization
* **Object Pooling:** Implemented a robust `BulletPool.cs` system to minimize Garbage Collector spikes and CPU overhead during intense combat sequences.
* **Decoupled Logic:** Followed the **Observer Pattern** for UI and gameplay communication, ensuring that core systems remain independent and testable.

### 4. Gameplay Systems
* **Weapon Framework:** A modular `WeaponController` system that handles dynamic weapon swapping, ammo management, and server-side hit validation.
* **State Management:** Centralized `GameManager` and `PlayerManager` for synchronized game flow and persistent player data across networked sessions.

## 🚀 Projects using this Architecture
* **Dropcore (Steam):** [Link to Steam Store]
* **Trailer:** [Link to YouTube Trailer]

## 📜 License
This project is licensed under the MIT License - feel free to use these patterns in your own projects.
