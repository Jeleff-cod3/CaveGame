# Project Instructions

## Project Map
    This details of this repo are irrelevant and outside the scope of your jurisdiction. Only and only work and look at the livegibberish folder at the repo root and it's contents.

## Commands
- Install:
  - Unity project: install Unity Editor `6000.4.6f1`, then open the repo root in Unity Hub so packages restore from `Packages/manifest.json`.
  - Backend: `cd backend; python -m venv .venv; .\.venv\Scripts\Activate.ps1; pip install -r requirements.txt; python manage.py migrate`
  - Live Gibberish: `cd livegibberish; py -3.11 -m venv .venv; .\.venv\Scripts\Activate.ps1; pip install -r requirements.txt; python scripts\setup_real_backends.py --openai-whisper --coqui-xtts`
  - Seamless UI: `cd seamless-ui-temp\streaming-react-app; npm install -g yarn; yarn`
- Run:
  - Unity: press Play in the Unity Editor, or run the existing Windows build at `WAllowCaveBuildSingularity\My project.exe`.
  - Backend API: `cd backend; .\.venv\Scripts\Activate.ps1; $env:USE_IN_MEMORY_CHANNEL_LAYER="true"; python manage.py runserver`
  - Backend WebSockets with Redis: `cd backend; .\.venv\Scripts\Activate.ps1; daphne config.asgi:application`
  - Live Gibberish: `cd livegibberish; .\.venv\Scripts\python.exe -m daphne -b 127.0.0.1 -p 8000 live_gibberish_web.asgi:application`
  - Seamless Docker: `docker compose up -d seamless-streaming; docker logs -f seamless-streaming`
  - Seamless UI dev server: `cd seamless-ui-temp\streaming-react-app; yarn dev`
- Test:
  - Backend: `cd backend; .\.venv\Scripts\Activate.ps1; python manage.py test`
  - Live Gibberish: `cd livegibberish; .\.venv\Scripts\python.exe -m unittest discover tests`
  - Unity EditMode: `& "$env:ProgramFiles\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml -quit`
- Lint/typecheck:
  - Seamless UI: `cd seamless-ui-temp\streaming-react-app; yarn ts-check; yarn lint`
  - No committed Python or Unity C# lint/typecheck command is configured.
- Build:
  - Seamless UI: `cd seamless-ui-temp\streaming-react-app; yarn build`
  - Seamless Docker: `docker compose build seamless-streaming`
  - Unity: use Unity Editor Build Profiles -> Windows. No committed CLI build method exists yet.
- Known setup gotchas:
  - Live Gibberish requires Python 3.11; Python 3.13 cannot install Coqui `TTS`.
  - Live Gibberish has no CPU fallback: CUDA, cuBLAS, Whisper, and Coqui XTTS must load successfully.
  - Coqui TTS may need Visual Studio C++ Build Tools and a Windows SDK if native extension builds fail with `io.h` or `cl.exe`.
  - Run Live Gibberish Daphne from `livegibberish` and prefer `.\.venv\Scripts\python.exe -m daphne` to avoid stale/global Windows launchers.
  - Backend local WebSocket testing can avoid Redis with `$env:USE_IN_MEMORY_CHANNEL_LAYER="true"`.
  - Seamless Docker needs Docker Desktop WSL2 plus working NVIDIA GPU passthrough; verify with `docker run --rm --gpus all nvidia/cuda:12.3.1-base-ubuntu22.04 nvidia-smi`.
  - Docker compose maps host `8000` to container `7860`, which can conflict with the Django or Live Gibberish servers if they are already on port `8000`.

## Engineering Rules
- Use Django and python, make many functions and instead of adding comments just make the code readable from itself meaning that the functions variables and the overall strucutre is like a story which you can read without knowing the context of the whole file's purpose and implementation. Put functions one above another logically meaning that the most basic functions are at the the top and the more they beome nested the lower they're placed. Try and use simple, readable syntaxis which again is like a painting not a cipher. Most of all I want you to name files very honestly and objectively - exactly what the file does - if it prints shit, call it shit printer, if it sends an audio stream to a specified port which just says shit - shit stream and so on if it's just a wrapper than say what it is in it's essence not definition - audio shit wrapper. The only comments I want you to ad should be at the top where the mdoel should write what this file does just an overall summary and which other files use it. Always have one arhcitecutre.md file. In it include the most possibly apt, short and starightforward explanation of the project structure and where to look for what. Also list what each folder does again in the same manner and spirit as the file itself - with a path to it and the services it exposes and what they do exactly - what data does it take in and what does it spit out that' all and again jsut a short epxlanation of the fable of the whole process continuity for each api endpoint.
- Never make mocks. Never. If you make any you will be punsihed. I can't stress it enough never make mocks of any feature make only mocks as tests like for testing features but those mock tests should never be contained within the actual main backend - they should be in some way exiled form the main logic of the backend as compeltely seperate service which in no manner interfere with the backend's actions. Thy shoudl simply act as external services mock calls to the api but not mock api's which just tes thte backend's work. Never create anything that could be interprteted as a mock function in the backend itself. Never. When developing the porject create a fodler where to store the backend's responses so they can be analyzed. That folder's path should be included in the architetcure.md file so you alawys know where to find it. It will also be included in one of your skill files with mcp guidelines on how to access the produced data and extract it's properties for further examination. When creating folders just like with files don't name them with sharp oneliners but with recongizable and properly described defenitions of the folders guts.Don't make disguistingly intricate folder nests keep it simple and shallow so everything is easily accessible.
- Secutiry really isn''t a concern for now most of all make sure to have a very error transmitabble system meaning that I want you to make logs of everything and I mean everything. These logs need to be easy to read and understand and show from which file they are and what they're actually displaying. Make very low level error checks meaning that each functions has ait's own specified error log basedo nwhat it does and what exactly crashes isntead of some broad errors like the nedpoint died - the erros should be very detailed and per step so it's easy to localize exactly where the program crashed. Don't make the error logs too long but include all the important stuff wihtout missing a single detial. In the logs themselves make them look clean so someone can easily scroll through them being careful not too make duplicates and such and to clear the logs when they've become too long. When clearing the logs though save them in a new file there will be a skill for that so jsut go though your skills to see what mcp to call.
- Don't modfy env's, ports and overall computer level hyperparamaters like whether to use the cpu or gpu always read the detials.md file for these kinds of details. This includes libraries and such never install libraries and run commands for installation. Never. And never change requirements.txt file always ask the user first and tell him what the current versions of the respectful libraries on his computers are and why they need updating. Whether to create or edit the virtual enviroment is also something that's specified in the details.md file.
- Make seperate md files if you see that a file is starting to bloat up with text data like long json structure schemas, prompts and others - instead save them as their file type in a sperate folder which is mentioned in teh acrhitecture.md file.

## Verification
- The tests you make should pass before you finish the feature.
- Call the endpoint you're developing with some real data if you don't have any ask the user to provide you with it instead of making mock tests. 
- To verify the behaviour of each endpoint as mentioned above you should go though your skills and check for any which expose some mcp endpoint providing the data and url needed to test out the endpoint. For the ui it's probably going to be a template so just call the url to access it's html to see if it's rendering properly.

## Review Expectations
- Check diff for regressions, dead code, missing tests, and risky shortcuts.
- At the end explain exactly how you fixed what was broken meaning why was it crashing and how you fixed and base don what evidence.

## Rule Maintenance
- When the agent repeats a mistake, propose an `AGENTS.md` update. Never edit the AGENTS.md file yourself. JSut propose what the edit itself should be like what text should be added removed or modified.