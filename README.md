# Ghostline

Ghostline is a top-down 2D arena shooter demonstrating **authoritative server-side replay verification** for anti-cheat. 

Rather than trusting client-submitted high scores, the Godot game client records player inputs and serializes them into a highly compact `GHST` binary format (15 bytes per frame). The ASP.NET Core backend deserializes this payload and completely re-simulates the match headlessly. Only runs where the server's deterministically calculated score matches the client's claim are committed to the PostgreSQL leaderboard.

## Project Structure
* **`src/Ghostline.Simulation`**: A pure C# .NET 8 class library containing the deterministic fixed-point physics and game logic. Shared by both the client and server.
* **`src/Ghostline.Api`**: An ASP.NET Core backend that receives binary replays, verifies them using the shared simulation library, and persists valid runs.
* **`client/`**: The Godot 4 (C#) game client that renders the simulation, polls player input, and submits runs to the backend.

## How to Run

1. **Start the Backend:**
   Ensure Docker Desktop is running, then spin up the API and PostgreSQL database:
   ```bash
   docker compose up --build -d
   ```
   The API will be available at `http://localhost:5000`.

2. **Start the Client:**
   Open the `client/` folder in Godot 4.
   Press **F5** to launch the game.

## Controls
* **WASD / Arrows**: Move
* **Left Click**: Fire
* **Space**: Dash
* **Q**: Shockwave | **E**: Shield | **R**: Laser Beam
* **F7**: Submit Run to Server
* **F8**: Toggle Replay Spectator Mode
* **Tab**: View Leaderboard
