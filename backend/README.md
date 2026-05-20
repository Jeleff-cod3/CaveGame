# CaveGame Backend

Django is the lobby, presence, validation, and relay layer for the Unity client.
Unity owns movement, physics, rotation, animation, and prediction. The backend
does not calculate movement.

## Quick Start

```powershell
cd backend
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python manage.py migrate
python manage.py runserver
```

For WebSockets with Redis:

```powershell
daphne config.asgi:application
```

For local no-Redis testing:

```powershell
$env:USE_IN_MEMORY_CHANNEL_LAYER="true"
python manage.py runserver
```

## Unity-Friendly Auth

Create a guest account/token:

```http
POST /api/accounts/guest/
```

Use the returned token for REST:

```http
Authorization: Token <token>
```

Use the same token for WebSockets:

```text
ws://localhost:8000/ws/lobby/1/?token=<token>
ws://localhost:8000/ws/game/1/?token=<token>
```

## REST Endpoints

```text
POST /api/lobbies/create/
POST /api/lobbies/{code}/join/
POST /api/lobbies/{id}/ready/
POST /api/lobbies/{id}/start/
GET  /api/lobbies/{id}/
```

## WebSocket Routes

```text
/ws/lobby/{lobby_id}/
/ws/game/{lobby_id}/
```

## Gameplay Rule

Clients send current state:

```json
{
  "type": "player_state",
  "seq": 123,
  "position": [1.25, 0.0, 6.8],
  "rotation": [0.0, 90.0, 0.0],
  "velocity": [0.0, 0.0, 4.5],
  "animationState": "run"
}
```

The backend validates shape, sequence, membership, and rate, then broadcasts the
state to other clients. It never computes movement.
